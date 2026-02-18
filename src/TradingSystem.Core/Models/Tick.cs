namespace TradingSystem.Core.Models;

public class Tick
{
    public DateTime Timestamp { get; set; }
    public decimal Price { get; set; }
    public long Volume { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
}
