namespace TradingSystem.Api.DTOs;

public class RadarItemDto
{
    public int instrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string MarketState { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public string QualityLabel { get; set; } = string.Empty;
    public string Bias { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal ATR { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class RadarResponseDto
{
    public List<RadarItemDto> Items { get; set; } = new();
    public int TotalScanned { get; set; }
    public int HighQuality { get; set; }
    public int Watchlist { get; set; }
    public DateTime ScannedAt { get; set; }
}

// ---- Intraday section DTOs ----

public class MoverItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal ATR { get; set; }
    public string Bias { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public DateTime ScannedAt { get; set; }
    
    // ---- Trend context (for mini-candlestick charts) ----
    public List<CandleDto> TrendCandles { get; set; } = new(); // Last 5 days of daily candles for UI trend chart
    
    // ---- AI Analysis placeholder (for future AI insights) ----
    public string AIAnalysis { get; set; } = "Analyzing.../Ready"; // e.g., "This stock shows strong breakout potential"
}

public class SectorLeaderItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal ChangePercent { get; set; }
    public int SetupScore { get; set; }
    public string Bias { get; set; } = string.Empty;
    public DateTime ScannedAt { get; set; }
}

public class BreakoutItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal OpenRangeHigh { get; set; }
    public decimal OpenRangeLow { get; set; }
    public decimal BreakoutPercent { get; set; }
    public string Direction { get; set; } = string.Empty; // LONG or SHORT
    public int SetupScore { get; set; }
    public DateTime ScannedAt { get; set; }
}

public class SRProximityItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public decimal Level { get; set; }
    public decimal DistancePercent { get; set; }
    public string LevelType { get; set; } = string.Empty; // SUPPORT or RESISTANCE
    public string Bias { get; set; } = string.Empty;
    public int SetupScore { get; set; }
    public DateTime ScannedAt { get; set; }
}

public class PatternItemDto
{
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public decimal LastClose { get; set; }
    public string PatternName { get; set; } = string.Empty;
    public string PatternDirection { get; set; } = string.Empty; // BULLISH or BEARISH
    public int Confidence { get; set; }
    public int SetupScore { get; set; }
    public DateTime ScannedAt { get; set; }
}

public class RadarSectionsDto
{
    public List<MoverItemDto> TopGainers { get; set; } = new();
    public List<MoverItemDto> TopLosers { get; set; } = new();
    public List<SectorLeaderItemDto> SectorLeaders { get; set; } = new();
    public List<BreakoutItemDto> Breakouts30Min { get; set; } = new();
    public List<SRProximityItemDto> NearSupport { get; set; } = new();
    public List<SRProximityItemDto> NearResistance { get; set; } = new();
    public List<PatternItemDto> Patterns { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
