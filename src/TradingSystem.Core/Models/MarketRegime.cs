namespace TradingSystem.Core.Models;

/// <summary>
/// Market regime classification
/// </summary>
public enum MarketRegimeType
{
    BULL_MARKET,
    BEAR_MARKET,
    SIDEWAYS_MARKET,
    HIGH_VOLATILITY_MARKET,
    LOW_LIQUIDITY_MARKET,
    TRANSITIONAL_MARKET
}

/// <summary>
/// Detected market regime with confidence
/// </summary>
public class MarketRegimeDetection
{
    public MarketRegimeType Regime { get; set; }
    public float Confidence { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    
    // Regime characteristics
    public float TrendStrength { get; set; }
    public float VolatilityLevel { get; set; }
    public float LiquidityLevel { get; set; }
    public float MarketBreadth { get; set; }
    public float SectorCorrelation { get; set; }
    
    // Sub-regime scores
    public Dictionary<MarketRegimeType, float> RegimeScores { get; set; } = new();
    
    // Contributing factors
    public List<string> KeyIndicators { get; set; } = new();
    
    /// <summary>
    /// Regime-specific recommendations
    /// </summary>
    public RegimeStrategyGuidance Guidance { get; set; } = new();
}

/// <summary>
/// Strategy adjustments based on market regime
/// </summary>
public class RegimeStrategyGuidance
{
    public string PreferredStrategies { get; set; } = string.Empty;
    public float RecommendedExposure { get; set; }  // 0.0 to 1.0
    public float RecommendedLeverage { get; set; }  // 0.0 to 2.0
    public float RiskMultiplier { get; set; } = 1.0f;
    public List<string> AvoidStrategies { get; set; } = new();
    public List<string> TradingTips { get; set; } = new();
}