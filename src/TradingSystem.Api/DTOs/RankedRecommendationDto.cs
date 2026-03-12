namespace TradingSystem.Api.DTOs;

public class RankedRecommendationDto : RecommendationDto
{
    /// <summary>
    /// Ranking position (1 = best)
    /// </summary>
    public int Rank { get; set; }

    /// <summary>
    /// Overall score based on user criteria
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Expected return percentage
    /// </summary>
    public decimal ExpectedReturnPercentage { get; set; }

    /// <summary>
    /// Risk percentage
    /// </summary>
    public decimal RiskPercentage { get; set; }

    /// <summary>
    /// Indicator signals that triggered this recommendation
    /// </summary>
    public List<string> IndicatorSignals { get; set; } = new();
}