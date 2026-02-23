namespace TradingSystem.Core.Models;

public enum ScanMarketState
{
    SIDEWAYS,
    TRENDING_BULLISH,
    TRENDING_BEARISH,
    PULLBACK_READY,
    OVEREXTENDED
}

public enum ScanBias
{
    BULLISH,
    BEARISH,
    NONE
}

public class ScanSnapshot
{
    public long Id { get; set; }
    public int InstrumentId { get; set; }
    public DateTime Timestamp { get; set; }
    public string MarketState { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public string Bias { get; set; } = string.Empty;
    public int AdxScore { get; set; }
    public int RsiScore { get; set; }
    public int EmaVwapScore { get; set; }
    public int VolumeScore { get; set; }
    public int BollingerScore { get; set; }
    public int StructureScore { get; set; }
    public decimal LastClose { get; set; }
    public decimal ATR { get; set; }
    public DateTime CreatedAt { get; set; }
    public TradingInstrument? Instrument { get; set; }

    public string QualityLabel => SetupScore >= 70 ? "HIGH" : SetupScore >= 50 ? "WATCHLIST" : "IGNORE";
}
