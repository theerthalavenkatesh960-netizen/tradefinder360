namespace TradingSystem.Api.DTOs;

/// <summary>
/// Market sentiment response DTO
/// </summary>
public class MarketSentimentDto
{
    public DateTimeOffset Timestamp { get; set; }
    public string Sentiment { get; set; } = string.Empty; // "BULLISH", "NEUTRAL", "BEARISH"
    public decimal SentimentScore { get; set; } // -100 to +100
    public string SentimentDescription { get; set; } = string.Empty;
    public MarketVolatilityDto Volatility { get; set; } = new();
    public MarketBreadthDto Breadth { get; set; } = new();
    public List<IndexPerformanceDto> MajorIndices { get; set; } = new();
    public List<SectorPerformanceDto> Sectors { get; set; } = new();
    public List<string> KeyFactors { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}

public class MarketVolatilityDto
{
    public decimal Index { get; set; } // VIX equivalent
    public string Level { get; set; } = string.Empty; // "LOW", "MODERATE", "HIGH", "EXTREME"
    public string Impact { get; set; } = string.Empty;
}

public class MarketBreadthDto
{
    public decimal AdvanceDeclineRatio { get; set; }
    public int StocksAdvancing { get; set; }
    public int StocksDeclining { get; set; }
    public int StocksUnchanged { get; set; }
    public string Interpretation { get; set; } = string.Empty;
}

public class IndexPerformanceDto
{
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal DayHigh { get; set; }
    public decimal DayLow { get; set; }
    public string Trend { get; set; } = string.Empty;
}

public class SectorPerformanceDto
{
    public string Name { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public int StocksAdvancing { get; set; }
    public int StocksDeclining { get; set; }
    public decimal RelativeStrength { get; set; }
    public string Performance { get; set; } = string.Empty; // "OUTPERFORMING", "INLINE", "UNDERPERFORMING"
}