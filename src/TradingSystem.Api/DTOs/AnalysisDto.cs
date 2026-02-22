namespace TradingSystem.Api.DTOs;

public class AnalysisDto
{
    public string InstrumentKey { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;

    public IndicatorSnapshotDto Indicators { get; set; } = new();
    public TrendStateDto TrendState { get; set; } = new();
    public EntryGuidanceDto? EntryGuidance { get; set; }
    public int Confidence { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<string> ReasoningPoints { get; set; } = new();
    public DateTimeOffset AnalysedAt { get; set; }
}

public class IndicatorSnapshotDto
{
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
    public DateTimeOffset Timestamp { get; set; }
}

public class TrendStateDto
{
    public string State { get; set; } = string.Empty;
    public string Bias { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public string QualityLabel { get; set; } = string.Empty;
    public ScoreBreakdownDto ScoreBreakdown { get; set; } = new();
}

public class ScoreBreakdownDto
{
    public int ADX { get; set; }
    public int RSI { get; set; }
    public int EmaVwap { get; set; }
    public int Volume { get; set; }
    public int Bollinger { get; set; }
    public int Structure { get; set; }
    public int Total { get; set; }
}

public class EntryGuidanceDto
{
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public string? OptionType { get; set; }
    public decimal? OptionStrike { get; set; }
}
