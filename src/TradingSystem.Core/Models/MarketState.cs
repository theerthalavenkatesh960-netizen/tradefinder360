namespace TradingSystem.Core.Models;

public enum MarketState
{
    SIDEWAYS,
    TRENDING_BULLISH,
    TRENDING_BEARISH
}

public class MarketStateInfo
{
    public MarketState State { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Dictionary<string, decimal> Indicators { get; set; } = new(); 
}
