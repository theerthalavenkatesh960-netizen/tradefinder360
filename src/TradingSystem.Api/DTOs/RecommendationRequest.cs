namespace TradingSystem.Api.DTOs;

public class RecommendationRequest
{
    /// <summary>
    /// Target return percentage (e.g., 10 for 10%)
    /// </summary>
    public decimal TargetReturnPercentage { get; set; } = 10;

    /// <summary>
    /// Risk tolerance percentage (e.g., 5 for 5% max loss)
    /// </summary>
    public decimal RiskTolerance { get; set; } = 5;

    /// <summary>
    /// Minimum risk-reward ratio to consider
    /// </summary>
    public decimal MinRiskRewardRatio { get; set; } = 2.0m;

    /// <summary>
    /// Number of top recommendations to return
    /// </summary>
    public int TopCount { get; set; } = 5;

    /// <summary>
    /// Timeframe for analysis in minutes (default: 15 min)
    /// </summary>
    public int TimeframeMinutes { get; set; } = 15;
}