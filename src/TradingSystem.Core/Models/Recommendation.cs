namespace TradingSystem.Core.Models;

public class Recommendation
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
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
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public TradingInstrument? Instrument { get; set; }
}
