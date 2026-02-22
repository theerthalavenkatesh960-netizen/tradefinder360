namespace TradingSystem.Api.DTOs;

public class RecommendationDto
{
    public Guid Id { get; set; }
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public int Confidence { get; set; }
    public string? OptionType { get; set; }
    public decimal? OptionStrike { get; set; }
    public string ExplanationText { get; set; } = string.Empty;
    public List<string> ReasoningPoints { get; set; } = new();
    public DateTime Timestamp { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
