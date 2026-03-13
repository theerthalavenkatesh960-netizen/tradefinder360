namespace TradingSystem.Core.Models;

/// <summary>
/// Trading strategy types supported by the system
/// </summary>
public enum StrategyType
{
    MOMENTUM,
    BREAKOUT,
    MEAN_REVERSION,
    SWING_TRADING
}

/// <summary>
/// Strategy-specific parameters and configuration
/// </summary>
public class StrategyConfig
{
    public StrategyType StrategyType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, decimal> Parameters { get; set; } = new();
    public int MinConfidence { get; set; } = 60;
    public decimal MinRiskReward { get; set; } = 2.0m;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Strategy evaluation result with scoring details
/// </summary>
public class StrategySignal
{
    public StrategyType Strategy { get; set; }
    public bool IsValid { get; set; }
    public int Score { get; set; } // 0-100
    public string Direction { get; set; } = string.Empty; // BUY/SELL
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal Confidence { get; set; }
    public List<string> Signals { get; set; } = new();
    public Dictionary<string, decimal> Metrics { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>
/// Strategy recommendation with ranked alternatives
/// </summary>
public class StrategyRecommendation
{
    public TradingInstrument Instrument { get; set; } = null!;
    public StrategySignal PrimarySignal { get; set; } = null!;
    public List<StrategySignal> AlternativeSignals { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}