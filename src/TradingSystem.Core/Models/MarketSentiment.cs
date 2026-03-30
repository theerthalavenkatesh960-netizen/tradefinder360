namespace TradingSystem.Core.Models;

/// <summary>
/// Represents overall market sentiment
/// </summary>
public enum SentimentType
{
    STRONGLY_BULLISH,
    BEARISH,
    NEUTRAL,
    BULLISH,
    STRONGLY_BEARISH
}

/// <summary>
/// Market sentiment entity stored in database
/// </summary>
public class MarketSentiment
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public SentimentType Sentiment { get; set; }
    public decimal SentimentScore { get; set; } // -100 to +100
    public decimal VolatilityIndex { get; set; } // VIX equivalent
    public decimal MarketBreadth { get; set; } // Advancing vs Declining ratio

    // ?? Multi-period indicator fields ????????????????????????????????????????
    public decimal RSI { get; set; }                  // market-wide average RSI
    public decimal MacdHistogram { get; set; }        // average MACD histogram across major indices
    public decimal PriceVs20DMA { get; set; }         // % above/below 20DMA (index average)
    public decimal PriceVs50DMA { get; set; }         // % above/below 50DMA
    public int NewHighs52W { get; set; }              // count of stocks at 52-week highs today
    public int NewLows52W { get; set; }               // count of stocks at 52-week lows today
    public decimal MclellanOscillator { get; set; }   // rolling breadth oscillator
    public decimal VixVs20DMA { get; set; }           // VIX minus its 20-day SMA

    // JSONB fields
    public List<IndexPerformance> IndexPerformance { get; set; } = new();
    public List<SectorPerformance> SectorPerformance { get; set; } = new();
    // PostgreSQL array
    public List<string> KeyFactors { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Market index performance data
/// </summary>
public class IndexPerformance
{
    public string IndexName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public decimal CurrentValue { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal DayHigh { get; set; }
    public decimal DayLow { get; set; }
}

/// <summary>
/// Sector performance data
/// </summary>
public class SectorPerformance
{
    public string SectorName { get; set; } = string.Empty;
    public decimal ChangePercent { get; set; }
    public int StocksAdvancing { get; set; }
    public int StocksDeclining { get; set; }
    public decimal RelativeStrength { get; set; }
}

/// <summary>
/// Complete market context snapshot
/// </summary>
public class MarketContext
{
    public DateTimeOffset Timestamp { get; set; }
    public SentimentType Sentiment { get; set; }
    public decimal SentimentScore { get; set; }
    public decimal VolatilityIndex { get; set; }
    public decimal MarketBreadth { get; set; }

    // ?? Multi-period indicator fields ????????????????????????????????????????
    public decimal RSI { get; set; }
    public decimal MacdHistogram { get; set; }
    public decimal PriceVs20DMA { get; set; }
    public decimal PriceVs50DMA { get; set; }
    public int NewHighs52W { get; set; }
    public int NewLows52W { get; set; }
    public decimal MclellanOscillator { get; set; }
    public decimal VixVs20DMA { get; set; }

    public List<IndexPerformance> MajorIndices { get; set; } = new();
    public List<SectorPerformance> Sectors { get; set; } = new();
    public List<string> KeyFactors { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}