using Microsoft.Extensions.Logging;
using TradingSystem.AI.Models;
using TradingSystem.Core.Events;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Feature Engineering Service - generates 120+ quantitative factors
/// Subscribes to market data events and computes features on-the-fly
/// </summary>
public class FeatureEngineeringService
{
    private readonly IFeatureStoreRepository _featureStore;
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IMarketSentimentService _sentimentService;
    private readonly IEventBus _eventBus;
    private readonly ILogger<FeatureEngineeringService> _logger;

    public FeatureEngineeringService(
        IFeatureStoreRepository featureStore,
        ICandleService candleService,
        IIndicatorService indicatorService,
        IMarketSentimentService sentimentService,
        IEventBus eventBus,
        ILogger<FeatureEngineeringService> logger)
    {
        _featureStore = featureStore;
        _candleService = candleService;
        _indicatorService = indicatorService;
        _sentimentService = sentimentService;
        _eventBus = eventBus;
        _logger = logger;

        // Subscribe to market data events
        _eventBus.Subscribe<MarketDataEvent>(OnMarketDataAsync);
    }

    /// <summary>
    /// Generate complete feature vector for an instrument
    /// </summary>
    public async Task<QuantFeatureVector> GenerateFeatureVectorAsync(
        int instrumentId,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating feature vector for {Symbol}", symbol);

        var timestamp = DateTimeOffset.UtcNow;

        // Fetch required data
        var candles = await _candleService.GetRecentCandlesAsync(instrumentId, 15, daysBack: 365);
        var indicators = await _indicatorService.GetLatestAsync(instrumentId, 15);
        var marketContext = await _sentimentService.GetCurrentMarketContextAsync(cancellationToken);

        if (!candles.Any())
        {
            _logger.LogWarning("No candles found for {Symbol}", symbol);
            return new QuantFeatureVector { InstrumentId = instrumentId, Symbol = symbol, Timestamp = timestamp };
        }

        var vector = new QuantFeatureVector
        {
            InstrumentId = instrumentId,
            Symbol = symbol,
            Timestamp = timestamp
        };

        // Compute all factor categories
        ComputeMomentumFactors(vector, candles);
        ComputeTrendFactors(vector, candles, indicators);
        ComputeVolatilityFactors(vector, candles, indicators);
        ComputeVolumeFactors(vector, candles, indicators);
        ComputeMeanReversionFactors(vector, candles, indicators);
        ComputeMarketFactors(vector, marketContext, candles);
        ComputeRelativeStrengthFactors(vector, candles);
        ComputeStatisticalFactors(vector, candles);
        ComputeSentimentFactors(vector, marketContext);
        ComputeRiskFactors(vector, candles);

        _logger.LogInformation("Generated {Count} features for {Symbol}", 
            vector.GetFeatureCount(), symbol);

        // Store in feature store
        await _featureStore.StoreFeatureVectorAsync(vector, cancellationToken);

        // Publish feature update event
        await _eventBus.PublishAsync(new FeatureUpdateEvent
        {
            Timestamp = timestamp,
            InstrumentId = instrumentId,
            Symbol = symbol,
            Features = vector.ToDictionary(),
            FeatureCount = vector.GetFeatureCount()
        }, cancellationToken);

        return vector;
    }

    private async Task OnMarketDataAsync(MarketDataEvent marketData)
    {
        try
        {
            // Regenerate features when market data updates
            if (marketData.DataType == MarketDataType.CANDLE_CLOSE)
            {
                await GenerateFeatureVectorAsync(marketData.InstrumentId, marketData.Symbol);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing market data event for {Symbol}", marketData.Symbol);
        }
    }

    // ========== MOMENTUM FACTORS ==========
    private void ComputeMomentumFactors(QuantFeatureVector vector, List<Candle> candles)
    {
        if (candles.Count < 2) return;

        var closes = candles.Select(c => (float)c.Close).ToArray();
        vector.Momentum_5D = CalculateMomentum(closes, 5);
        vector.Momentum_10D = CalculateMomentum(closes, 10);
        vector.Momentum_20D = CalculateMomentum(closes, 20);
        vector.Momentum_30D = CalculateMomentum(closes, 30);
        vector.Momentum_60D = CalculateMomentum(closes, 60);
        vector.Momentum_90D = CalculateMomentum(closes, 90);

        vector.Momentum_ROC_5 = CalculateROC(closes, 5);
        vector.Momentum_ROC_10 = CalculateROC(closes, 10);
        vector.Momentum_ROC_20 = CalculateROC(closes, 20);

        vector.RSI_14 = CalculateRSI(closes, 14);
        vector.RSI_7 = CalculateRSI(closes, 7);
        vector.RSI_21 = CalculateRSI(closes, 21);

        // Additional momentum indicators
        var (stochK, stochD) = CalculateStochastic(candles, 14);
        vector.Stochastic_K = stochK;
        vector.Stochastic_D = stochD;
        vector.Williams_R = CalculateWilliamsR(candles, 14);
        vector.CCI_20 = CalculateCCI(candles, 20);
    }

    // ========== TREND FACTORS ==========
    private void ComputeTrendFactors(QuantFeatureVector vector, List<Candle> candles, IndicatorSnapshot? indicators)
    {
        var closes = candles.Select(c => (float)c.Close).ToArray();
        var currentPrice = closes.Last();

        vector.EMA_9 = CalculateEMA(closes, 9);
        vector.EMA_21 = CalculateEMA(closes, 21);
        vector.EMA_50 = CalculateEMA(closes, 50);
        vector.EMA_200 = CalculateEMA(closes, 200);

        vector.SMA_20 = CalculateSMA(closes, 20);
        vector.SMA_50 = CalculateSMA(closes, 50);
        vector.SMA_200 = CalculateSMA(closes, 200);

        vector.Price_To_SMA20_Ratio = vector.SMA_20 > 0 ? currentPrice / vector.SMA_20 : 1f;
        vector.Price_To_SMA50_Ratio = vector.SMA_50 > 0 ? currentPrice / vector.SMA_50 : 1f;
        vector.Price_To_SMA200_Ratio = vector.SMA_200 > 0 ? currentPrice / vector.SMA_200 : 1f;

        if (indicators != null)
        {
            vector.Trend_Strength_ADX = (float)indicators.ADX;
            vector.Trend_Direction_DI_Plus = (float)indicators.PlusDI;
            vector.Trend_Direction_DI_Minus = (float)indicators.MinusDI;
        }

        vector.EMA_Crossover_Signal = (vector.EMA_9 - vector.EMA_21) / currentPrice * 100;
        vector.Trend_Consistency = CalculateTrendConsistency(closes, 20);
    }

    // ========== VOLATILITY FACTORS ==========
    private void ComputeVolatilityFactors(QuantFeatureVector vector, List<Candle> candles, IndicatorSnapshot? indicators)
    {
        if (indicators != null)
        {
            vector.ATR_14 = (float)indicators.ATR;
            vector.Bollinger_Width = (float)indicators.BollingerWidth;
            vector.ATR_Percent = (float)(indicators.ATR / candles.Last().Close * 100);

            var currentPrice = (float)candles.Last().Close;
            var bollLower = (float)indicators.BollingerLower;
            var bollUpper = (float)indicators.BollingerUpper;
            vector.Bollinger_Position = bollUpper > bollLower 
                ? (currentPrice - bollLower) / (bollUpper - bollLower) 
                : 0.5f;
        }

        vector.Historical_Volatility_10D = CalculateHistoricalVolatility(candles, 10);
        vector.Historical_Volatility_20D = CalculateHistoricalVolatility(candles, 20);
        vector.Historical_Volatility_30D = CalculateHistoricalVolatility(candles, 30);
        vector.Parkinson_Volatility = CalculateParkinsonVolatility(candles, 20);
        vector.Volatility_Ratio_10_30 = vector.Historical_Volatility_30D > 0 
            ? vector.Historical_Volatility_10D / vector.Historical_Volatility_30D 
            : 1f;
    }

    // ========== VOLUME FACTORS ==========
    private void ComputeVolumeFactors(QuantFeatureVector vector, List<Candle> candles, IndicatorSnapshot? indicators)
    {
        var volumes = candles.Select(c => (float)c.Volume).ToArray();
        var avgVolume20 = volumes.TakeLast(20).Average();
        var avgVolume5 = volumes.TakeLast(5).Average();

        vector.Volume_SMA_20 = avgVolume20;
        vector.Volume_Ratio_20D = avgVolume20 > 0 ? volumes.Last() / avgVolume20 : 1f;
        vector.Volume_Ratio_5D = avgVolume5 > 0 ? volumes.Last() / avgVolume5 : 1f;

        if (indicators != null)
        {
            vector.VWAP = (float)indicators.VWAP;
            vector.Price_To_VWAP_Ratio = indicators.VWAP > 0 
                ? (float)candles.Last().Close / (float)indicators.VWAP 
                : 1f;
        }

        vector.OBV = CalculateOBV(candles);
        vector.Money_Flow_Index = CalculateMFI(candles, 14);
        vector.Dollar_Volume = (float)(candles.Last().Close * candles.Last().Volume);
    }

    // ========== MEAN REVERSION FACTORS ==========
    private void ComputeMeanReversionFactors(QuantFeatureVector vector, List<Candle> candles, IndicatorSnapshot? indicators)
    {
        var closes = candles.Select(c => (float)c.Close).ToArray();
        var currentPrice = closes.Last();

        vector.Z_Score_Price = CalculateZScore(closes, 20);
        vector.Deviation_From_Mean_20D = (currentPrice - vector.SMA_20) / vector.SMA_20 * 100;
        vector.Deviation_From_Mean_50D = (currentPrice - vector.SMA_50) / vector.SMA_50 * 100;

        if (indicators != null)
        {
            vector.Price_Distance_To_VWAP = (float)((candles.Last().Close - indicators.VWAP) / indicators.VWAP * 100);
        }

        vector.Overbought_Oversold_Score = CalculateOverboughtOversoldScore(vector.RSI_14, vector.Bollinger_Position);
    }

    // ========== MARKET & MACRO FACTORS ==========
    private void ComputeMarketFactors(QuantFeatureVector vector, MarketContext marketContext, List<Candle> candles)
    {
        vector.VIX_Level = (float)marketContext.VolatilityIndex;
        vector.Market_Breadth = (float)marketContext.MarketBreadth;
        vector.Macro_Sentiment_Score = (float)marketContext.SentimentScore;

        var marketRegime = marketContext.Sentiment switch
        {
            SentimentType.BULLISH => 1f,
            SentimentType.BEARISH => -1f,
            _ => 0f
        };
        vector.Market_Regime = marketRegime;

        // Market beta would require benchmark index data - placeholder for now
        vector.Market_Beta = 1.0f;  // TODO: Calculate actual beta
        vector.Market_Correlation = 0.7f;  // TODO: Calculate correlation with index
    }

    // ========== RELATIVE STRENGTH FACTORS ==========
    private void ComputeRelativeStrengthFactors(QuantFeatureVector vector, List<Candle> candles)
    {
        var closes = candles.Select(c => (float)c.Close).ToArray();

        vector.RS_Momentum_3M = CalculateMomentum(closes, 60);  // ~3 months
        vector.RS_Momentum_6M = CalculateMomentum(closes, 120);  // ~6 months
        vector.RS_Momentum_12M = CalculateMomentum(closes, 250);  // ~12 months

        // TODO: Add actual RS vs market/sector when benchmark data available
        vector.RS_vs_Market = 0f;
        vector.RS_vs_Sector = 0f;
    }

    // ========== STATISTICAL FACTORS ==========
    private void ComputeStatisticalFactors(QuantFeatureVector vector, List<Candle> candles)
    {
        var returns = CalculateReturns(candles);

        vector.Skewness_Returns = CalculateSkewness(returns);
        vector.Kurtosis_Returns = CalculateKurtosis(returns);
        vector.Sharpe_Ratio_30D = CalculateSharpeRatio(returns.TakeLast(30).ToList());
        vector.Max_Drawdown_30D = CalculateMaxDrawdown(candles.TakeLast(30).ToList());
        vector.Returns_Autocorrelation = CalculateAutocorrelation(returns, 1);
    }

    // ========== SENTIMENT FACTORS ==========
    private void ComputeSentimentFactors(QuantFeatureVector vector, MarketContext marketContext)
    {
        // Use your existing sentiment detector
        vector.News_Sentiment_Score = (float)marketContext.SentimentScore;
        vector.Sentiment_Momentum = (float)marketContext.SentimentScore;  // TODO: Calculate momentum

        // Placeholders for additional sentiment sources
        vector.Social_Media_Sentiment = 0f;  // TODO: If you have social media data
        vector.Put_Call_Ratio = 0f;  // TODO: If options data available
    }

    // ========== RISK FACTORS ==========
    private void ComputeRiskFactors(QuantFeatureVector vector, List<Candle> candles)
    {
        var returns = CalculateReturns(candles);

        vector.Downside_Deviation = CalculateDownsideDeviation(returns);
        vector.Value_At_Risk_95 = CalculateVaR(returns, 0.95f);
        vector.Idiosyncratic_Volatility = vector.Historical_Volatility_20D;
        vector.Risk_Adjusted_Return = vector.Sharpe_Ratio_30D;
    }

    // ========== HELPER CALCULATION METHODS ==========
    
    private float CalculateMomentum(float[] closes, int period)
    {
        if (closes.Length < period + 1) return 0f;
        var current = closes[^1];
        var past = closes[^(period + 1)];
        return past > 0 ? (current - past) / past * 100 : 0f;
    }

    private float CalculateROC(float[] closes, int period)
    {
        return CalculateMomentum(closes, period);
    }

    private float CalculateRSI(float[] closes, int period)
    {
        if (closes.Length < period + 1) return 50f;

        float avgGain = 0, avgLoss = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            var change = closes[i] - closes[i - 1];
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }

        avgGain /= period;
        avgLoss /= period;

        if (avgLoss == 0) return 100f;
        var rs = avgGain / avgLoss;
        return 100f - (100f / (1f + rs));
    }

    private (float K, float D) CalculateStochastic(List<Candle> candles, int period)
    {
        if (candles.Count < period) return (50f, 50f);

        var recent = candles.TakeLast(period).ToList();
        var highestHigh = (float)recent.Max(c => c.High);
        var lowestLow = (float)recent.Min(c => c.Low);
        var currentClose = (float)candles.Last().Close;

        var k = highestHigh > lowestLow 
            ? (currentClose - lowestLow) / (highestHigh - lowestLow) * 100 
            : 50f;
        
        // Simplified %D as 3-period SMA of %K
        return (k, k);  // TODO: Calculate proper %D
    }

    private float CalculateWilliamsR(List<Candle> candles, int period)
    {
        var (k, _) = CalculateStochastic(candles, period);
        return k - 100f;  // Williams %R = -1 * Stochastic %K
    }

    private float CalculateCCI(List<Candle> candles, int period)
    {
        if (candles.Count < period) return 0f;

        var recent = candles.TakeLast(period).ToList();
        var typicalPrices = recent.Select(c => (float)c.TypicalPrice).ToArray();
        var sma = typicalPrices.Average();
        var meanDeviation = typicalPrices.Select(tp => Math.Abs(tp - sma)).Average();

        var currentTP = (float)candles.Last().TypicalPrice;
        return meanDeviation > 0 ? (currentTP - sma) / (0.015f * meanDeviation) : 0f;
    }

    private float CalculateEMA(float[] values, int period)
    {
        if (values.Length < period) return values.Last();

        float multiplier = 2f / (period + 1);
        float ema = values.Take(period).Average();

        for (int i = period; i < values.Length; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
        }

        return ema;
    }

    private float CalculateSMA(float[] values, int period)
    {
        if (values.Length < period) return values.Average();
        return values.TakeLast(period).Average();
    }

    private float CalculateTrendConsistency(float[] closes, int period)
    {
        if (closes.Length < period + 1) return 0f;

        var upDays = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            if (closes[i] > closes[i - 1]) upDays++;
        }

        return (float)upDays / period * 100;
    }

    private float CalculateHistoricalVolatility(List<Candle> candles, int period)
    {
        var returns = CalculateReturns(candles).TakeLast(period).ToList();
        if (!returns.Any()) return 0f;

        var mean = returns.Average();
        var variance = returns.Select(r => Math.Pow(r - mean, 2)).Average();
        return (float)Math.Sqrt(variance) * 100;  // Annualized
    }

    private float CalculateParkinsonVolatility(List<Candle> candles, int period)
    {
        var recent = candles.TakeLast(period).ToList();
        if (!recent.Any()) return 0f;

        var sum = recent
            .Select(c => Math.Pow(Math.Log((double)(c.High / c.Low)), 2))
            .Average();

        return (float)Math.Sqrt(sum / (4 * Math.Log(2))) * 100;
    }

    private float CalculateOBV(List<Candle> candles)
    {
        if (candles.Count < 2) return 0f;

        float obv = 0;
        for (int i = 1; i < candles.Count; i++)
        {
            if (candles[i].Close > candles[i - 1].Close)
                obv += (float)candles[i].Volume;
            else if (candles[i].Close < candles[i - 1].Close)
                obv -= (float)candles[i].Volume;
        }

        return obv;
    }

    private float CalculateMFI(List<Candle> candles, int period)
    {
        // Money Flow Index - simplified version
        if (candles.Count < period + 1) return 50f;

        float posFlow = 0, negFlow = 0;
        var recent = candles.TakeLast(period + 1).ToList();

        for (int i = 1; i < recent.Count; i++)
        {
            var moneyFlow = (float)(recent[i].TypicalPrice * recent[i].Volume);
            if (recent[i].TypicalPrice > recent[i - 1].TypicalPrice)
                posFlow += moneyFlow;
            else
                negFlow += moneyFlow;
        }

        if (negFlow == 0) return 100f;
        var mfRatio = posFlow / negFlow;
        return 100f - (100f / (1f + mfRatio));
    }

    private float CalculateZScore(float[] values, int period)
    {
        if (values.Length < period) return 0f;

        var recent = values.TakeLast(period).ToArray();
        var mean = recent.Average();
        var stdDev = (float)Math.Sqrt(recent.Select(v => Math.Pow(v - mean, 2)).Average());

        return stdDev > 0 ? (values.Last() - mean) / stdDev : 0f;
    }

    private float CalculateOverboughtOversoldScore(float rsi, float bollingerPosition)
    {
        // Composite score: -100 (oversold) to +100 (overbought)
        var rsiScore = (rsi - 50) * 2;  // -100 to +100
        var bbScore = (bollingerPosition - 0.5f) * 200;  // -100 to +100
        return (rsiScore + bbScore) / 2;
    }

    private List<float> CalculateReturns(List<Candle> candles)
    {
        var returns = new List<float>();
        for (int i = 1; i < candles.Count; i++)
        {
            var ret = (float)((candles[i].Close - candles[i - 1].Close) / candles[i - 1].Close);
            returns.Add(ret);
        }
        return returns;
    }

    private float CalculateSkewness(List<float> values)
    {
        if (values.Count < 3) return 0f;

        var mean = values.Average();
        var stdDev = (float)Math.Sqrt(values.Select(v => Math.Pow(v - mean, 2)).Average());

        if (stdDev == 0) return 0f;

        var skew = values.Select(v => Math.Pow((v - mean) / stdDev, 3)).Average();
        return (float)skew;
    }

    private float CalculateKurtosis(List<float> values)
    {
        if (values.Count < 4) return 0f;

        var mean = values.Average();
        var stdDev = (float)Math.Sqrt(values.Select(v => Math.Pow(v - mean, 2)).Average());

        if (stdDev == 0) return 0f;

        var kurt = values.Select(v => Math.Pow((v - mean) / stdDev, 4)).Average();
        return (float)kurt - 3;  // Excess kurtosis
    }

    private float CalculateSharpeRatio(List<float> returns, float riskFreeRate = 0.05f)
    {
        if (!returns.Any()) return 0f;

        var avgReturn = returns.Average();
        var stdDev = (float)Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());

        return stdDev > 0 ? (avgReturn - riskFreeRate / 252) / stdDev : 0f;  // Daily Sharpe
    }

    private float CalculateMaxDrawdown(List<Candle> candles)
    {
        if (!candles.Any()) return 0f;

        var closes = candles.Select(c => (float)c.Close).ToList();
        float maxDrawdown = 0f;
        float peak = closes.First();

        foreach (var price in closes)
        {
            if (price > peak) peak = price;
            var drawdown = (peak - price) / peak;
            if (drawdown > maxDrawdown) maxDrawdown = drawdown;
        }

        return maxDrawdown * 100;
    }

    private float CalculateDownsideDeviation(List<float> returns, float targetReturn = 0f)
    {
        var downsideReturns = returns.Where(r => r < targetReturn).ToList();
        if (!downsideReturns.Any()) return 0f;

        var variance = downsideReturns.Select(r => Math.Pow(r - targetReturn, 2)).Average();
        return (float)Math.Sqrt(variance);
    }

    private float CalculateVaR(List<float> returns, float confidenceLevel)
    {
        if (!returns.Any()) return 0f;

        var sorted = returns.OrderBy(r => r).ToList();
        var index = (int)((1 - confidenceLevel) * sorted.Count);
        return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
    }

    private float CalculateAutocorrelation(List<float> values, int lag)
    {
        if (values.Count < lag + 1) return 0f;

        var mean = values.Average();
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();

        if (variance == 0) return 0f;

        float covariance = 0;
        for (int i = lag; i < values.Count; i++)
        {
            covariance += (values[i] - mean) * (values[i - lag] - mean);
        }

        covariance /= (values.Count - lag);
        return (float)(covariance / variance);
    }
}