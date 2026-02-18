namespace TradingSystem.Core.Models;

public class Option
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Strike { get; set; }
    public TradeDirection Type { get; set; }
    public DateTime Expiry { get; set; }
    public decimal LastPrice { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public long Volume { get; set; }
    public decimal ImpliedVolatility { get; set; }
    public bool IsATM { get; set; }
}
