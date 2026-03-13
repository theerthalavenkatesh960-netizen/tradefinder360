namespace TradingSystem.Core.Models;

/// <summary>
/// Tracks performance of individual meta-factors for reinforcement learning
/// </summary>
public class FactorPerformanceTracking
{
    public long Id { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    
    // Factor weights (current)
    public float MomentumWeight { get; set; }
    public float TrendWeight { get; set; }
    public float VolatilityWeight { get; set; }
    public float LiquidityWeight { get; set; }
    public float RelativeStrengthWeight { get; set; }
    public float SentimentWeight { get; set; }
    public float RiskWeight { get; set; }
    
    // Performance by factor
    public float MomentumWinRate { get; set; }
    public float MomentumAvgReturn { get; set; }
    public int MomentumTradeCount { get; set; }
    
    public float TrendWinRate { get; set; }
    public float TrendAvgReturn { get; set; }
    public int TrendTradeCount { get; set; }
    
    public float SentimentWinRate { get; set; }
    public float SentimentAvgReturn { get; set; }
    public int SentimentTradeCount { get; set; }
    
    // Overall metrics
    public float TotalTrades { get; set; }
    public float OverallWinRate { get; set; }
    public float OverallSharpeRatio { get; set; }
    
    // Recommended weight adjustments
    public string RecommendedAdjustmentsJson { get; set; } = string.Empty;
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}