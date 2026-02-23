using TradingSystem.Core.Models;

namespace TradingSystem.Scanner.Models;

public class ScanResult
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public ScanMarketState MarketState { get; set; }
    public int SetupScore { get; set; }
    public ScanBias Bias { get; set; }
    public decimal LastClose { get; set; }
    public decimal ATR { get; set; }
    public DateTime ScannedAt { get; set; }

    public ScoreBreakdown ScoreBreakdown { get; set; } = new();
    public List<string> Reasons { get; set; } = new();

    public string QualityLabel => SetupScore >= 70 ? "HIGH" : SetupScore >= 50 ? "WATCHLIST" : "IGNORE";
}

public class ScoreBreakdown
{
    public int AdxScore { get; set; }
    public int RsiScore { get; set; }
    public int EmaVwapScore { get; set; }
    public int VolumeScore { get; set; }
    public int BollingerScore { get; set; }
    public int StructureScore { get; set; }
    public int Total => AdxScore + RsiScore + EmaVwapScore + VolumeScore + BollingerScore + StructureScore;
}
