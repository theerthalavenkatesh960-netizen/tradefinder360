using TradingSystem.Core.Models;

namespace TradingSystem.Risk.Models;

public class RiskParameters
{
    public decimal StopLossPrice { get; set; }
    public decimal TargetPrice { get; set; }
    public decimal StopLossDistance { get; set; }
    public decimal TargetDistance { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public int PositionSize { get; set; }
    public decimal MaxLossAmount { get; set; }
}

public class ExitSignal
{
    public bool ShouldExit { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal CurrentPrice { get; set; }
    public Dictionary<string, string> Details { get; set; } = new();
}
