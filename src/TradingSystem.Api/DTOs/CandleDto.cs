namespace TradingSystem.Api.DTOs;

public class CandleDto
{
    public DateTimeOffset Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}

public class CandleResponseDto
{
    public string InstrumentKey { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public int Timeframe { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Count { get; set; }
    public List<CandleDto> Candles { get; set; } = new();
}

public class LatestCandleResponseDto
{
    public string InstrumentKey { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public int Timeframe { get; set; }
    public CandleDto Candle { get; set; } = null!;
}

public class TimeframeInfoDto
{
    public int Minutes { get; set; }
    public string Description { get; set; } = string.Empty;
    public int CandlesPerDay { get; set; }
}

public class MarketHoursDto
{
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public int TradingMinutesPerDay { get; set; }
}

public class CandleInfoDto
{
    public List<TimeframeInfoDto> AvailableTimeframes { get; set; } = new();
    public MarketHoursDto MarketHours { get; set; } = null!;
}