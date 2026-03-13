namespace TradingSystem.Api.DTOs;

public class StrategyDto
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class StrategySignalDto
{
    public string Strategy { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public int Score { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal Confidence { get; set; }
    public List<string> Signals { get; set; } = new();
    public Dictionary<string, decimal> Metrics { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

public class StrategyRecommendationDto
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public StrategySignalDto Signal { get; set; } = null!;
    public List<StrategySignalDto>? AlternativeSignals { get; set; }
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}