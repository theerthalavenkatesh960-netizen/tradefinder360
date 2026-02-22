namespace TradingSystem.Core.Models;

public class IndicatorSnapshot
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public int TimeframeMinutes { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public decimal EMAFast { get; set; }
    public decimal EMASlow { get; set; }
    public decimal RSI { get; set; }
    public decimal MacdLine { get; set; }
    public decimal MacdSignal { get; set; }
    public decimal MacdHistogram { get; set; }
    public decimal ADX { get; set; }
    public decimal PlusDI { get; set; }
    public decimal MinusDI { get; set; }
    public decimal ATR { get; set; }
    public decimal BollingerUpper { get; set; }
    public decimal BollingerMiddle { get; set; }
    public decimal BollingerLower { get; set; }
    public decimal VWAP { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public TradingInstrument? Instrument { get; set; }
}
