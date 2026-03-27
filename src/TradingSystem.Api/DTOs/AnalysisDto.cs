namespace TradingSystem.Api.DTOs;

public class AnalysisDto
{
    public string InstrumentKey { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;

    public IndicatorSnapshotDto Indicators { get; set; } = new();
    public TrendStateDto TrendState { get; set; } = new();
    public EntryGuidanceDto? EntryGuidance { get; set; }

    // New context sections
    public NoTradeContextDto? NoTradeContext { get; set; }
    public VolumeContextDto? VolumeContext { get; set; }
    public StructureLevelsDto? StructureLevels { get; set; }
    public SignalTimingDto? SignalTiming { get; set; }
    public MarketRegimeDto? MarketRegime { get; set; }

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

    // New confidence breakdown
    public ConfidenceBreakdownDto? ConfidenceBreakdown { get; set; }
    public int? ExpectedHoldingMinutes { get; set; }
    public decimal? MaxAdverseExcursionPct { get; set; }
    public decimal? MaxFavorableExcursionPct { get; set; }
}

// ?? New DTOs ?????????????????????????????????????????????????

public class NoTradeContextDto
{
    public string WhyNoTradeCode { get; set; } = string.Empty;
    public string WhyNoTradeMessage { get; set; } = string.Empty;
    public decimal? NextTriggerPrice { get; set; }
    public string? NextTriggerCondition { get; set; }
    public int? EstimatedRecheckMinutes { get; set; }
    public DateTimeOffset? InvalidatesAt { get; set; }
}

public class VolumeContextDto
{
    public long CurrentVolume { get; set; }
    public long Volume20Avg { get; set; }
    public decimal RelativeVolume { get; set; }
    public decimal? DeliveryVolumeRatio { get; set; }
    public bool IsAboveAverage { get; set; }
}

public class StructureLevelsDto
{
    public decimal? SessionHigh { get; set; }
    public decimal? SessionLow { get; set; }
    public decimal? PreviousDayHigh { get; set; }
    public decimal? PreviousDayLow { get; set; }
    public decimal? NearestSupport { get; set; }
    public decimal? NearestResistance { get; set; }
    public decimal? Pivot { get; set; }
    public decimal? R1 { get; set; }
    public decimal? S1 { get; set; }
}

public class SignalTimingDto
{
    public int? SignalAgeBars { get; set; }
    public int? BarsSinceMacdCross { get; set; }
    public int? BarsSinceRsiZoneExit { get; set; }
    public int SignalFreshnessScore { get; set; }
}

public class MarketRegimeDto
{
    public string VolatilityRegime { get; set; } = string.Empty;
    public int TrendStrengthScore { get; set; }
    public int RangeCompressionScore { get; set; }
    public int MomentumQualityScore { get; set; }
}

public class ConfidenceBreakdownDto
{
    public int Trend { get; set; }
    public int Momentum { get; set; }
    public int Volume { get; set; }
    public int Structure { get; set; }
    public int Volatility { get; set; }
    public int Total { get; set; }
}
