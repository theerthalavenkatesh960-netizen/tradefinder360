namespace TradingSystem.Core.Models;

public class Candle
{
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
    public int TimeframeMinutes { get; set; }

    public decimal TypicalPrice => (High + Low + Close) / 3;
    public decimal Range => High - Low;
    public bool IsBullish => Close > Open;
    public bool IsBearish => Close < Open;
    public decimal BodySize => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
    //test
}
