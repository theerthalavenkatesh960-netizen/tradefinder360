using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;

namespace TradingSystem.AI.Services;

/// <summary>
/// Service for compressing 120+ factors into 7 meta-factors
/// </summary>
public class MetaFactorService
{
    private readonly ILogger<MetaFactorService> _logger;

    // Weights for meta-factor calculation (can be ML-optimized later)
    private readonly Dictionary<string, float> _momentumWeights = new()
    {
        ["Momentum_20D"] = 0.25f,
        ["RSI_14"] = 0.20f,
        ["MACD_Histogram"] = 0.20f,
        ["Momentum_ROC_20"] = 0.15f,
        ["Stochastic_K"] = 0.10f,
        ["Williams_R"] = 0.10f
    };

    private readonly Dictionary<string, float> _trendWeights = new()
    {
        ["Trend_Strength_ADX"] = 0.30f,
        ["EMA_Crossover_Signal"] = 0.25f,
        ["Price_To_SMA50_Ratio"] = 0.20f,
        ["Trend_Consistency"] = 0.15f,
        ["EMA_200"] = 0.10f
    };

    public MetaFactorService(ILogger<MetaFactorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Compress 120+ factors into 7 meta-factors
    /// </summary>
    public MetaFactors CompressToMetaFactors(QuantFeatureVector features)
    {
        var metaFactors = new MetaFactors
        {
            InstrumentId = features.InstrumentId,
            Symbol = features.Symbol,
            Timestamp = features.Timestamp
        };

        // Calculate each meta-factor
        metaFactors.MomentumMetaFactor = CalculateMomentumMetaFactor(features);
        metaFactors.TrendMetaFactor = CalculateTrendMetaFactor(features);
        metaFactors.VolatilityMetaFactor = CalculateVolatilityMetaFactor(features);
        metaFactors.LiquidityMetaFactor = CalculateLiquidityMetaFactor(features);
        metaFactors.RelativeStrengthMetaFactor = CalculateRelativeStrengthMetaFactor(features);
        metaFactors.SentimentMetaFactor = CalculateSentimentMetaFactor(features);
        metaFactors.RiskMetaFactor = CalculateRiskMetaFactor(features);

        // Calculate composite score
        metaFactors.CompositeScore = CalculateCompositeScore(metaFactors);

        _logger.LogDebug("Compressed features for {Symbol}: Composite={Score:F2}",
            features.Symbol, metaFactors.CompositeScore);

        return metaFactors;
    }

    private float CalculateMomentumMetaFactor(QuantFeatureVector f)
    {
        // Weighted average of momentum indicators (normalized to -100 to +100)
        var score = 0f;
        
        // Price momentum (normalized)
        score += Normalize(f.Momentum_20D, -20, 20) * 0.25f;
        score += Normalize(f.Momentum_60D, -30, 30) * 0.15f;
        
        // RSI (convert 0-100 to -100 to +100)
        score += ((f.RSI_14 - 50) * 2) * 0.20f;
        
        // MACD
        score += Normalize(f.MACD_Histogram, -2, 2) * 0.20f;
        
        // Rate of Change
        score += Normalize(f.Momentum_ROC_20, -15, 15) * 0.10f;
        
        // Stochastic
        score += ((f.Stochastic_K - 50) * 2) * 0.10f;

        return Clamp(score, -100, 100);
    }

    private float CalculateTrendMetaFactor(QuantFeatureVector f)
    {
        var score = 0f;
        
        // ADX strength (0-100 scale)
        score += (f.Trend_Strength_ADX - 25) * 2 * 0.30f;  // Strong if > 25
        
        // EMA alignment
        score += (f.Price_To_SMA20_Ratio - 1) * 100 * 0.20f;
        score += (f.Price_To_SMA50_Ratio - 1) * 100 * 0.20f;
        
        // Trend consistency
        score += (f.Trend_Consistency - 50) * 2 * 0.15f;
        
        // Directional movement
        var dmDiff = f.Trend_Direction_DI_Plus - f.Trend_Direction_DI_Minus;
        score += Normalize(dmDiff, -30, 30) * 0.15f;

        return Clamp(score, -100, 100);
    }

    private float CalculateVolatilityMetaFactor(QuantFeatureVector f)
    {
        var score = 0f;
        
        // ATR percent (higher = more volatile)
        score += Normalize(f.ATR_Percent, 0, 5) * 0.30f;
        
        // Historical volatility
        score += Normalize(f.Historical_Volatility_20D, 0, 40) * 0.25f;
        
        // Bollinger bandwidth
        score += Normalize(f.Bollinger_BandWidth_Ratio * 100, 0, 10) * 0.20f;
        
        // Volatility ratio (recent vs longer-term)
        score += (f.Volatility_Ratio_10_30 - 1) * 100 * 0.15f;
        
        // Parkinson volatility
        score += Normalize(f.Parkinson_Volatility, 0, 40) * 0.10f;

        return Clamp(score, 0, 100);  // Volatility is always positive
    }

    private float CalculateLiquidityMetaFactor(QuantFeatureVector f)
    {
        var score = 0f;
        
        // Volume ratios (higher = better liquidity)
        score += Normalize(f.Volume_Ratio_20D - 1, -0.5f, 2) * 0.30f;
        score += Normalize(f.Volume_Ratio_5D - 1, -0.5f, 2) * 0.20f;
        
        // Dollar volume (normalized by typical values)
        var dollarVolumeScore = Math.Log10(Math.Max(f.Dollar_Volume, 1)) - 6;  // Log scale
        score += Normalize((float)dollarVolumeScore, -2, 2) * 0.25f;
        
        // Money Flow Index
        score += (f.Money_Flow_Index - 50) * 2 * 0.15f;
        
        // Volume trend
        score += Normalize(f.Volume_Trend, -50, 50) * 0.10f;

        return Clamp(score, -100, 100);
    }

    private float CalculateRelativeStrengthMetaFactor(QuantFeatureVector f)
    {
        var score = 0f;
        
        // Multi-timeframe relative strength
        score += Normalize(f.RS_Momentum_3M, -20, 20) * 0.35f;
        score += Normalize(f.RS_Momentum_6M, -30, 30) * 0.30f;
        score += Normalize(f.RS_Momentum_12M, -40, 40) * 0.20f;
        
        // RS vs market/sector (when available)
        score += Normalize(f.RS_vs_Market * 100, -20, 20) * 0.10f;
        score += Normalize(f.RS_vs_Sector * 100, -20, 20) * 0.05f;

        return Clamp(score, -100, 100);
    }

    private float CalculateSentimentMetaFactor(QuantFeatureVector f)
    {
        var score = 0f;
        
        // News sentiment
        score += Normalize(f.News_Sentiment_Score, -1, 1) * 100 * 0.40f;
        
        // Market regime alignment
        score += f.Market_Regime * 50 * 0.30f;  // -1 to +1 -> -50 to +50
        
        // Macro sentiment
        score += Normalize(f.Macro_Sentiment_Score, -1, 1) * 100 * 0.20f;
        
        // Social media (if available)
        score += Normalize(f.Social_Media_Sentiment, -1, 1) * 100 * 0.10f;

        return Clamp(score, -100, 100);
    }

    private float CalculateRiskMetaFactor(QuantFeatureVector f)
    {
        // Higher score = higher risk
        var score = 0f;
        
        // Downside deviation
        score += Normalize(f.Downside_Deviation, 0, 0.05f) * 0.25f;
        
        // Value at Risk
        score += Normalize(Math.Abs(f.Value_At_Risk_95), 0, 0.05f) * 0.20f;
        
        // Max drawdown
        score += Normalize(f.Max_Drawdown_30D, 0, 30) * 0.20f;
        
        // Volatility of volatility
        score += Normalize(f.Volatility_Of_Volatility, 0, 2) * 0.15f;
        
        // Tail risk
        score += Normalize(f.Tail_Risk_Indicator, 0, 1) * 100 * 0.10f;
        
        // Liquidity risk
        score += Normalize(f.Liquidity_Risk_Score, 0, 100) * 0.10f;

        return Clamp(score, 0, 100);  // Risk is always positive
    }

    private float CalculateCompositeScore(MetaFactors mf)
    {
        // Regime-adjusted composite score
        var score = 0f;
        
        score += mf.MomentumMetaFactor * 0.25f;
        score += mf.TrendMetaFactor * 0.25f;
        score += mf.LiquidityMetaFactor * 0.15f;
        score += mf.RelativeStrengthMetaFactor * 0.15f;
        score += mf.SentimentMetaFactor * 0.10f;
        score -= mf.RiskMetaFactor * 0.05f;  // Subtract risk
        score -= mf.VolatilityMetaFactor * 0.05f;  // Subtract volatility

        return Clamp(score, -100, 100);
    }

    private float Normalize(float value, float min, float max)
    {
        if (max == min) return 0f;
        var normalized = (value - min) / (max - min) * 200 - 100;  // Map to -100 to +100
        return Clamp(normalized, -100, 100);
    }

    private float Clamp(float value, float min, float max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}