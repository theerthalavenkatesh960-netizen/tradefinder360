using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using TradingSystem.AI.Models;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.AI.Services;

/// <summary>
/// Service for training and using ML models to predict trade success
/// </summary>
public class TradePredictionService
{
    private readonly MLContext _mlContext;
    private readonly ILogger<TradePredictionService> _logger;
    private ITransformer? _model;
    private DataViewSchema? _modelSchema;
    private readonly string _modelPath;

    public TradePredictionService(ILogger<TradePredictionService> logger)
    {
        _mlContext = new MLContext(seed: 42);
        _logger = logger;
        _modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "trade_predictor.zip");

        // Try to load existing model
        LoadModel();
    }

    /// <summary>
    /// Train the ML model with historical trade data
    /// </summary>
    public void TrainModel(IEnumerable<TradeFeatures> trainingData)
    {
        _logger.LogInformation("Starting model training with {Count} samples", trainingData.Count());

        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);

        // Split data for training and testing
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        // Define the training pipeline
        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(TradeFeatures.RSI),
                nameof(TradeFeatures.MACD),
                nameof(TradeFeatures.MACDSignal),
                nameof(TradeFeatures.MACDHistogram),
                nameof(TradeFeatures.EMAFast),
                nameof(TradeFeatures.EMASlow),
                nameof(TradeFeatures.ADX),
                nameof(TradeFeatures.ATR),
                nameof(TradeFeatures.VolumeRatio),
                nameof(TradeFeatures.VolumeMA),
                nameof(TradeFeatures.VWAP),
                nameof(TradeFeatures.BollingerWidth),
                nameof(TradeFeatures.BollingerPosition),
                nameof(TradeFeatures.HistoricalVolatility),
                nameof(TradeFeatures.PriceChangePercent),
                nameof(TradeFeatures.PriceToEMAFast),
                nameof(TradeFeatures.PriceToEMASlow),
                nameof(TradeFeatures.MarketSentimentScore),
                nameof(TradeFeatures.MarketVolatilityIndex),
                nameof(TradeFeatures.MarketBreadth),
                nameof(TradeFeatures.RiskRewardRatio),
                nameof(TradeFeatures.StopLossDistance),
                nameof(TradeFeatures.StrategyScore),
                nameof(TradeFeatures.StrategyConfidence))
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: nameof(TradeFeatures.IsSuccessful),
                featureColumnName: "Features",
                numberOfLeaves: 50,
                numberOfTrees: 100,
                minimumExampleCountPerLeaf: 10,
                learningRate: 0.2))
            .Append(_mlContext.Transforms.CopyColumns("Probability", "Score"));

        // Train the model
        _logger.LogInformation("Training model...");
        _model = pipeline.Fit(split.TrainSet);
        _modelSchema = split.TrainSet.Schema;

        // Evaluate the model
        var predictions = _model.Transform(split.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions,
            labelColumnName: nameof(TradeFeatures.IsSuccessful));

        _logger.LogInformation(
            "Model training complete. Accuracy: {Accuracy:P2}, AUC: {AUC:F4}, F1Score: {F1:F4}",
            metrics.Accuracy, metrics.AreaUnderRocCurve, metrics.F1Score);

        // Save the model
        SaveModel();
    }

    /// <summary>
    /// Predict trade success probability
    /// </summary>
    public TradePrediction? PredictTradeSuccess(TradeFeatures features)
    {
        if (_model == null)
        {
            _logger.LogWarning("Model not loaded. Cannot make predictions.");
            return null;
        }

        try
        {
            var predictionEngine = _mlContext.Model.CreatePredictionEngine<TradeFeatures, TradePrediction>(_model);
            var prediction = predictionEngine.Predict(features);

            return prediction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making prediction");
            return null;
        }
    }

    /// <summary>
    /// Extract features from trade opportunity
    /// </summary>
    public TradeFeatures ExtractFeatures(
        TradingInstrument instrument,
        List<Candle> candles,
        IndicatorValues indicators,
        StrategySignal signal,
        MarketContext marketContext)
    {
        var latestCandle = candles.LastOrDefault();
        if (latestCandle == null)
            throw new InvalidOperationException("No candles available");

        // Calculate volume metrics
        var recentVolumes = candles.TakeLast(20).Select(c => (float)c.Volume).ToList();
        var avgVolume = recentVolumes.Average();
        var volumeRatio = avgVolume > 0 ? (float)latestCandle.Volume / avgVolume : 1;

        // Calculate volatility
        var returns = new List<float>();
        for (int i = 1; i < candles.Count; i++)
        {
            var ret = (float)((candles[i].Close - candles[i - 1].Close) / candles[i - 1].Close);
            returns.Add(ret);
        }
        var historicalVolatility = CalculateStandardDeviation(returns) * 100;

        // Bollinger position
        var bollingerPosition = 0.5f;
        if (indicators.BollingerUpper != indicators.BollingerLower)
        {
            bollingerPosition = (float)(((decimal)latestCandle.Close - indicators.BollingerLower) /
                                       (indicators.BollingerUpper - indicators.BollingerLower));
        }

        // Price changes
        var priceChange = candles.Count > 1
            ? (float)(((latestCandle.Close - candles[^2].Close) / candles[^2].Close) * 100)
            : 0;

        var priceToEMAFast = indicators.EMAFast > 0
            ? (float)(((decimal)latestCandle.Close / indicators.EMAFast - 1) * 100)
            : 0;

        var priceToEMASlow = indicators.EMASlow > 0
            ? (float)(((decimal)latestCandle.Close / indicators.EMASlow - 1) * 100)
            : 0;

        // Risk metrics
        var stopLossDistance = signal.EntryPrice > 0
            ? (float)(Math.Abs(signal.StopLoss - signal.EntryPrice) / signal.EntryPrice * 100)
            : 0;

        var riskReward = 0f;
        var risk = Math.Abs(signal.EntryPrice - signal.StopLoss);
        var reward = Math.Abs(signal.Target - signal.EntryPrice);
        if (risk > 0)
            riskReward = (float)(reward / risk);

        return new TradeFeatures
        {
            // Technical Indicators
            RSI = (float)indicators.RSI,
            MACD = (float)indicators.MacdLine,
            MACDSignal = (float)indicators.MacdSignal,
            MACDHistogram = (float)indicators.MacdHistogram,
            EMAFast = (float)indicators.EMAFast,
            EMASlow = (float)indicators.EMASlow,
            ADX = (float)indicators.ADX,
            ATR = (float)indicators.ATR,

            // Volume
            VolumeRatio = volumeRatio,
            VolumeMA = avgVolume,
            VWAP = (float)indicators.VWAP,

            // Volatility
            BollingerWidth = (float)indicators.BollingerWidth,
            BollingerPosition = bollingerPosition,
            HistoricalVolatility = historicalVolatility,

            // Price Action
            PriceChangePercent = priceChange,
            PriceToEMAFast = priceToEMAFast,
            PriceToEMASlow = priceToEMASlow,

            // Market Sentiment
            MarketSentimentScore = (float)marketContext.SentimentScore,
            MarketVolatilityIndex = (float)marketContext.VolatilityIndex,
            MarketBreadth = (float)marketContext.MarketBreadth,

            // Risk
            RiskRewardRatio = riskReward,
            StopLossDistance = stopLossDistance,

            // Strategy
            StrategyScore = signal.Score,
            StrategyConfidence = (float)signal.Confidence
        };
    }

    /// <summary>
    /// Get feature importance for interpretability
    /// </summary>
    public Dictionary<string, float> GetFeatureImportance()
    {
        // This would require using permutation feature importance
        // For now, return a static ranking based on typical importance
        return new Dictionary<string, float>
        {
            ["StrategyConfidence"] = 0.15f,
            ["RSI"] = 0.12f,
            ["RiskRewardRatio"] = 0.11f,
            ["MACD"] = 0.10f,
            ["MarketSentimentScore"] = 0.09f,
            ["ADX"] = 0.08f,
            ["VolumeRatio"] = 0.07f,
            ["ATR"] = 0.06f,
            ["StrategyScore"] = 0.05f,
            ["BollingerPosition"] = 0.05f,
            ["MarketVolatilityIndex"] = 0.04f,
            ["HistoricalVolatility"] = 0.04f,
            ["PriceToEMAFast"] = 0.04f
        };
    }

    private float CalculateStandardDeviation(List<float> values)
    {
        if (values.Count < 2) return 0;

        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return (float)Math.Sqrt(sumOfSquares / values.Count);
    }

    private void SaveModel()
    {
        if (_model == null || _modelSchema == null) return;

        try
        {
            var modelDir = Path.GetDirectoryName(_modelPath);
            if (modelDir != null && !Directory.Exists(modelDir))
            {
                Directory.CreateDirectory(modelDir);
            }

            _mlContext.Model.Save(_model, _modelSchema, _modelPath);
            _logger.LogInformation("Model saved to {Path}", _modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving model");
        }
    }

    private void LoadModel()
    {
        if (!File.Exists(_modelPath))
        {
            _logger.LogInformation("No existing model found at {Path}", _modelPath);
            return;
        }

        try
        {
            _model = _mlContext.Model.Load(_modelPath, out _modelSchema);
            _logger.LogInformation("Model loaded from {Path}", _modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading model from {Path}", _modelPath);
        }
    }

    public bool IsModelTrained() => _model != null;
}