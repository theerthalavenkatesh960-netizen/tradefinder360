using TradingSystem.Core.Models;

namespace TradingSystem.Strategy.Models;

public class EntrySignal
{
    public bool IsValid { get; set; }
    public TradeDirection Direction { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public decimal EntryPrice { get; set; }
    public Dictionary<string, string> ValidationDetails { get; set; } = new();
}
