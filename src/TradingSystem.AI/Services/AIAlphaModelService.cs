using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// AI Alpha Model - Generates predictions using meta-factors and market regime
/// </summary>
public class AIAlphaModelService
{
    private readonly FeatureEngineeringService _featureEngineering;
    private readonly MetaFactorService _metaFactorService;
    private readonly MarketRegimeService _regimeService;
    private readonly TradePredictionService _mlService;
    private readonly IFeatureStoreRepository _featureStore;
    private readonly ILogger<AIAlphaModelService> _logger;

    // Model parameters (can be ML-learned)
    private readonly Dictionary<MarketRegimeType, float> _regimeReturnMultipliers = new()
    {
        [MarketRegimeType.BULL_MARKET] = 1.3f,
        [MarketRegimeType.BEAR_MARKET] = 0.7f,
        [MarketRegimeType.SIDEWAYS_MARKET] = 0.9f,
        [MarketRegimeType.HIGH_VOLATILITY_MARKET] = 0.8f,
        [MarketRegimeType.LOW_LIQUIDITY_MARKET] = 0.85f,
        [MarketRegimeType.TRANSITIONAL_MARKET] = 1.0f
    };

    public AIAlphaModelService(
        FeatureEngineeringService featureEngineering,
        MetaFactorService metaFactorService,
        MarketRegimeService regimeService,
        TradePredictionService mlService,
        IFeatureStoreRepository featureStore,
        ILogger<AIAlphaModelService> logger)
    {
        _featureEngineering = featureEngineering;
        _metaFactorService = metaFactorService;
        _regimeService = regimeService;
        _mlService = mlService;
        _featureStore = featureStore;
        _logger = logger;
    }

    /// <summary>
    /// Generate AI alpha prediction for an instrument
    /// </summary>
    public async Task<AIAlphaPrediction> GeneratePredictionAsync(
        int instrumentId,
        string symbol,
        string sector,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating AI alpha prediction for {Symbol}", symbol);

        // Step 1: Get or generate feature vector
        var features = await GetOrGenerateFeatures(instrumentId, symbol, cancellationToken);

        // Step 2: Compress to meta-factors
        var metaFactors = _metaFactorService.CompressToMetaFactors(features);

        // Step 3: Detect market regime
        var marketRegime = await _regimeService.DetectRegimeAsync(cancellationToken);

        // Step 4: Generate prediction
        var prediction = GenerateAlphaPrediction(
            instrumentId,
            symbol,
            sector,
            metaFactors,
            marketRegime,
            features);

        _logger.LogInformation(
            "Generated prediction for {Symbol}: Return={Return:F2}%, Probability={Prob:P0}, Risk={Risk:F1}",
            symbol, prediction.ExpectedReturn, prediction.SuccessProbability, prediction.RiskScore);

        return prediction;
    }

    /// <summary>
    /// Generate predictions for multiple instruments and rank them
    /// </summary>
    public async Task<List<AIAlphaPrediction>> GenerateRankedPredictionsAsync(
        List<(int Id, string Symbol, string Sector)> instruments,
        int topN = 20,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating ranked predictions for {Count} instruments", instruments.Count);

        var predictions = new List<AIAlphaPrediction>();
        var marketRegime = await _regimeService.DetectRegimeAsync(cancellationToken);

        foreach (var (id, symbol, sector) in instruments)
        {
            try
            {
                var features = await GetOrGenerateFeatures(id, symbol, cancellationToken);
                var metaFactors = _metaFactorService.CompressToMetaFactors(features);
                var prediction = GenerateAlphaPrediction(id, symbol, sector, metaFactors, marketRegime, features);
                predictions.Add(prediction);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error generating prediction for {Symbol}", symbol);
            }
        }

        // Rank by risk-adjusted return (Sharpe-like ranking)
        var rankedPredictions = predictions
            .Where(p => p.SuccessProbability >= 0.5f)
            .OrderByDescending(p => p.ExpectedReturn / Math.Max(p.RiskScore, 1))
            .ThenByDescending(p => p.SuccessProbability)
            .Take(topN)
            .ToList();

        _logger.LogInformation("Ranked top {Count} predictions", rankedPredictions.Count);

        return rankedPredictions;
    }

    private async Task<QuantFeatureVector> GetOrGenerateFeatures(
        int instrumentId,
        string symbol,
        CancellationToken cancellationToken)
    {
        // Try to get latest from feature store
        var cached = await _featureStore.GetLatestAsync(instrumentId, cancellationToken);

        if (cached != null && (DateTimeOffset.UtcNow - cached.Timestamp).TotalMinutes < 30)
        {
            return cached;
        }

        // Generate fresh features
        return await _featureEngineering.GenerateFeatureVectorAsync(
            instrumentId,
            symbol,
            cancellationToken);
    }

    private AIAlphaPrediction GenerateAlphaPrediction(
        int instrumentId,
        string symbol,
        string sector,
        MetaFactors metaFactors,
        MarketRegimeDetection regime,
        QuantFeatureVector features)
    {
        // Calculate base expected return from meta-factors
        var baseReturn = CalculateExpectedReturn(metaFactors, regime);

        // Calculate success probability
        var successProbability = CalculateSuccessProbability(metaFactors, regime);

        // Calculate risk score
        var riskScore = CalculateRiskScore(metaFactors, regime, features);

        // Calculate confidence
        var confidence = CalculatePredictionConfidence(metaFactors, regime);

        // Determine recommended action
        var action = DetermineAction(baseReturn, successProbability, riskScore, regime);

        // Calculate risk-adjusted metrics
        var sharpeRatio = CalculateSharpeRatio(baseReturn, riskScore);
        var sortinoRatio = CalculateSortinoRatio(baseReturn, metaFactors.RiskMetaFactor);

        // Get feature importance from composite score
        var featureImportance = GetFeatureImportance(metaFactors);

        // Generate trade levels
        var (entry, stopLoss, target) = GenerateTradeLevels(
            features,
            baseReturn,
            riskScore,
            action);

        // Position sizing based on regime
        var positionSize = CalculatePositionSize(regime, riskScore, successProbability);

        return new AIAlphaPrediction
        {
            InstrumentId = instrumentId,
            Symbol = symbol,
            Sector = sector,
            PredictionTime = DateTimeOffset.UtcNow,
            ExpectedReturn = baseReturn,
            SuccessProbability = successProbability,
            RiskScore = riskScore,
            PredictionConfidence = confidence,
            ConfidenceLevel = GetConfidenceLevel(confidence),
            MetaFactors = metaFactors,
            MarketRegime = regime,
            FeatureImportance = featureImportance,
            SharpeRatio = sharpeRatio,
            SortinoRatio = sortinoRatio,
            MaxDrawdownEstimate = EstimateMaxDrawdown(riskScore),
            RecommendedAction = action,
            SuggestedEntry = entry,
            SuggestedStopLoss = stopLoss,
            SuggestedTarget = target,
            PositionSizeMultiplier = positionSize
        };
    }

    private float CalculateExpectedReturn(MetaFactors mf, MarketRegimeDetection regime)
    {
        // Base return from composite meta-factor score (-100 to +100 -> -10% to +10%)
        var baseReturn = mf.CompositeScore * 0.1f;

        // Adjust by individual meta-factors
        baseReturn += mf.MomentumMetaFactor * 0.05f;
        baseReturn += mf.TrendMetaFactor * 0.04f;
        baseReturn += mf.RelativeStrengthMetaFactor * 0.03f;
        baseReturn += mf.SentimentMetaFactor * 0.02f;

        // Apply regime multiplier
        var regimeMultiplier = _regimeReturnMultipliers.GetValueOrDefault(
            regime.Regime, 1.0f);
        baseReturn *= regimeMultiplier;

        // Dampen by risk
        var riskDamping = 1 - (mf.RiskMetaFactor / 200f);  // 0 to 100 -> 1.0 to 0.5
        baseReturn *= riskDamping;

        return Math.Clamp(baseReturn, -15f, 15f);
    }

    private float CalculateSuccessProbability(MetaFactors mf, MarketRegimeDetection regime)
    {
        // Base probability from composite score
        var baseProbability = (mf.CompositeScore + 100) / 200f;  // -100 to +100 -> 0 to 1

        // Boost for strong trend alignment
        if (Math.Abs(mf.TrendMetaFactor) > 50 && Math.Sign(mf.TrendMetaFactor) == Math.Sign(mf.MomentumMetaFactor))
        {
            baseProbability += 0.1f;
        }

        // Boost for sentiment alignment
        if (Math.Sign(mf.SentimentMetaFactor) == Math.Sign(mf.CompositeScore))
        {
            baseProbability += 0.05f;
        }

        // Penalty for high risk
        baseProbability -= mf.RiskMetaFactor / 500f;  // 0-100 -> 0 to -0.2

        // Regime confidence multiplier
        baseProbability *= regime.Confidence;

        return Math.Clamp(baseProbability, 0.2f, 0.95f);
    }

    private float CalculateRiskScore(MetaFactors mf, MarketRegimeDetection regime, QuantFeatureVector features)
    {
        var riskScore = mf.RiskMetaFactor;

        // Add volatility component
        riskScore += mf.VolatilityMetaFactor * 0.3f;

        // Liquidity penalty (low liquidity = higher risk)
        if (mf.LiquidityMetaFactor < 0)
        {
            riskScore += Math.Abs(mf.LiquidityMetaFactor) * 0.2f;
        }

        // Regime risk adjustment
        riskScore *= regime.Guidance.RiskMultiplier;

        // Market volatility
        riskScore += regime.VolatilityLevel * 0.5f;

        return Math.Clamp(riskScore, 5f, 100f);
    }

    private float CalculatePredictionConfidence(MetaFactors mf, MarketRegimeDetection regime)
    {
        var confidence = 0.5f;

        // Strong meta-factor signals increase confidence
        if (Math.Abs(mf.CompositeScore) > 50) confidence += 0.2f;
        if (Math.Abs(mf.MomentumMetaFactor) > 60) confidence += 0.1f;
        if (Math.Abs(mf.TrendMetaFactor) > 60) confidence += 0.1f;

        // Regime confidence
        confidence += regime.Confidence * 0.1f;

        return Math.Clamp(confidence, 0.3f, 0.95f);
    }

    private string DetermineAction(float expectedReturn, float probability, float risk, MarketRegimeDetection regime)
    {
        // Risk-adjusted threshold
        var threshold = regime.Regime switch
        {
            MarketRegimeType.BULL_MARKET => 2.0f,
            MarketRegimeType.BEAR_MARKET => 4.0f,
            MarketRegimeType.HIGH_VOLATILITY_MARKET => 3.5f,
            _ => 2.5f
        };

        if (expectedReturn > threshold && probability > 0.6f)
            return "BUY";
        
        if (expectedReturn < -threshold && probability > 0.6f)
            return "SELL";
        
        return "HOLD";
    }

    private string GetConfidenceLevel(float confidence)
    {
        return confidence switch
        {
            >= 0.75f => "HIGH",
            >= 0.6f => "MEDIUM",
            _ => "LOW"
        };
    }

    private float CalculateSharpeRatio(float expectedReturn, float risk)
    {
        var riskFreeRate = 0.05f;  // 5% annual
        var annualizedReturn = expectedReturn * 12;  // Assuming monthly return
        var annualizedRisk = risk / 10f;  // Normalize

        return annualizedRisk > 0 ? (annualizedReturn - riskFreeRate) / annualizedRisk : 0f;
    }

    private float CalculateSortinoRatio(float expectedReturn, float downsideRisk)
    {
        var riskFreeRate = 0.05f;
        var annualizedReturn = expectedReturn * 12;
        var annualizedDownsideRisk = downsideRisk / 10f;

        return annualizedDownsideRisk > 0 ? (annualizedReturn - riskFreeRate) / annualizedDownsideRisk : 0f;
    }

    private float EstimateMaxDrawdown(float riskScore)
    {
        // Estimate max drawdown from risk score
        return riskScore * 0.3f;  // Risk 100 -> ~30% max drawdown
    }

    private Dictionary<string, float> GetFeatureImportance(MetaFactors mf)
    {
        var importance = new Dictionary<string, float>
        {
            ["Momentum"] = Math.Abs(mf.MomentumMetaFactor) / 100f,
            ["Trend"] = Math.Abs(mf.TrendMetaFactor) / 100f,
            ["Volatility"] = Math.Abs(mf.VolatilityMetaFactor) / 100f,
            ["Liquidity"] = Math.Abs(mf.LiquidityMetaFactor) / 100f,
            ["RelativeStrength"] = Math.Abs(mf.RelativeStrengthMetaFactor) / 100f,
            ["Sentiment"] = Math.Abs(mf.SentimentMetaFactor) / 100f,
            ["Risk"] = Math.Abs(mf.RiskMetaFactor) / 100f
        };

        return importance.OrderByDescending(kv => kv.Value)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    private (decimal? entry, decimal? stopLoss, decimal? target) GenerateTradeLevels(
        QuantFeatureVector features,
        float expectedReturn,
        float riskScore,
        string action)
    {
        if (action == "HOLD") return (null, null, null);

        // Use last close as reference
        var lastClose = features.EMA_9;  // Approximate current price
        if (lastClose <= 0) return (null, null, null);

        var entry = (decimal)lastClose;
        
        // ATR-based stop loss
        var atrMultiplier = riskScore > 50 ? 2.0f : 1.5f;
        var stopDistance = features.ATR_14 * atrMultiplier;

        // Target based on expected return
        var targetDistance = Math.Abs(expectedReturn) / 100f * lastClose;

        if (action == "BUY")
        {
            var stopLoss = entry - (decimal)stopDistance;
            var target = entry + (decimal)targetDistance;
            return (entry, stopLoss, target);
        }
        else  // SELL
        {
            var stopLoss = entry + (decimal)stopDistance;
            var target = entry - (decimal)targetDistance;
            return (entry, stopLoss, target);
        }
    }

    private float CalculatePositionSize(MarketRegimeDetection regime, float risk, float probability)
    {
        var baseSize = regime.Guidance.RecommendedExposure;
        
        // Adjust by probability
        var probabilityAdjustment = probability > 0.7f ? 1.2f : (probability < 0.55f ? 0.8f : 1.0f);
        
        // Adjust by risk
        var riskAdjustment = risk > 60 ? 0.7f : (risk < 30 ? 1.1f : 1.0f);
        
        return baseSize * probabilityAdjustment * riskAdjustment;
    }
}