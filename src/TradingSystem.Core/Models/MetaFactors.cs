namespace TradingSystem.Core.Models;

/// <summary>
/// Compressed meta-factors from 120+ quantitative factors
/// </summary>
public class MetaFactors
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    // 7 Meta-Factors
    public float MomentumMetaFactor { get; set; }
    public float TrendMetaFactor { get; set; }
    public float VolatilityMetaFactor { get; set; }
    public float LiquidityMetaFactor { get; set; }
    public float RelativeStrengthMetaFactor { get; set; }
    public float SentimentMetaFactor { get; set; }
    public float RiskMetaFactor { get; set; }

    /// <summary>
    /// Composite score from all meta-factors
    /// </summary>
    public float CompositeScore { get; set; }

    public Dictionary<string, float> ToDictionary()
    {
        return new Dictionary<string, float>
        {
            [nameof(MomentumMetaFactor)] = MomentumMetaFactor,
            [nameof(TrendMetaFactor)] = TrendMetaFactor,
            [nameof(VolatilityMetaFactor)] = VolatilityMetaFactor,
            [nameof(LiquidityMetaFactor)] = LiquidityMetaFactor,
            [nameof(RelativeStrengthMetaFactor)] = RelativeStrengthMetaFactor,
            [nameof(SentimentMetaFactor)] = SentimentMetaFactor,
            [nameof(RiskMetaFactor)] = RiskMetaFactor,
            [nameof(CompositeScore)] = CompositeScore
        };
    }
}