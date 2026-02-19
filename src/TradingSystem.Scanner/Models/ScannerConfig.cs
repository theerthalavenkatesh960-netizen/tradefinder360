namespace TradingSystem.Scanner.Models;

public class ScannerConfig
{
    public int ScanIntervalMinutes { get; set; } = 15;
    public int MinimumScoreThreshold { get; set; } = 50;
    public bool EnableScanForAllInstruments { get; set; } = true;
    public List<string> ScanInstruments { get; set; } = new();

    public ScoreWeights Weights { get; set; } = new();
}

public class ScoreWeights
{
    public int AdxWeight { get; set; } = 20;
    public int RsiWeight { get; set; } = 20;
    public int EmaVwapWeight { get; set; } = 15;
    public int VolumeWeight { get; set; } = 15;
    public int BollingerWeight { get; set; } = 10;
    public int StructureWeight { get; set; } = 20;
}
