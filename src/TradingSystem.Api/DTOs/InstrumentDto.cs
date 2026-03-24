namespace TradingSystem.Api.DTOs;

public class InstrumentDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string InstrumentKey { get; set; } = string.Empty;

    // metadata
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public decimal? MarketCap { get; set; }
    public string? InstrumentType { get; set; }
    public bool IsDerivativesEnabled { get; set; }

    // market data
    public decimal? Price { get; set; }
    public long? Volume { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }

    // derived/analysis
    public string? Trend { get; set; }

    // recommendation information
    public decimal? EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? ExpectedProfit { get; set; }
    public int? Confidence { get; set; }
}

/// <summary>
/// Detailed instrument view including candle data for charting.
/// </summary>
public class InstrumentDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string InstrumentKey { get; set; } = string.Empty;

    // metadata
    public string? Sector { get; set; }
    public string? Industry { get; set; }
    public decimal? MarketCap { get; set; }
    public string? InstrumentType { get; set; }
    public string? TradingMode { get; set; }
    public int LotSize { get; set; }
    public decimal TickSize { get; set; }
    public bool IsDerivativesEnabled { get; set; }

    // latest price
    public decimal? Price { get; set; }
    public long? Volume { get; set; }
    public decimal? Change { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal? DayHigh { get; set; }
    public decimal? DayLow { get; set; }
    public decimal? DayOpen { get; set; }

    // trend/analysis
    public string? Trend { get; set; }
    public int? SetupScore { get; set; }
    public string? MarketState { get; set; }
    public IndicatorSnapshotDto? LatestIndicators { get; set; }

    // recommendation
    public decimal? EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? RiskRewardRatio { get; set; }
    public decimal? ExpectedProfit { get; set; }
    public int? Confidence { get; set; }
    public string? RecommendationDirection { get; set; }
    public DateTimeOffset? RecommendationExpiresAt { get; set; }

    // candles for charting
    public List<CandleDto> Candles { get; set; } = [];
}

/// <summary>
/// Search/filter request body for POST /api/instrument/search.
/// Defaults return all active STOCK instruments sorted by symbol.
/// Send an empty {} body to get the default stock listing.
/// </summary>
public class InstrumentSearchRequest
{
    /// <summary>Search by symbol or name (partial match)</summary>
    public string? Search { get; set; }

    /// <summary>Exchange: NSE, BSE</summary>
    public string? Exchange { get; set; }

    /// <summary>Sector name or code</summary>
    public string? Sector { get; set; }

    /// <summary>Industry name</summary>
    public string? Industry { get; set; }

    /// <summary>Instrument type: STOCK, INDEX. Default: STOCK</summary>
    public string InstrumentType { get; set; } = "STOCK";

    /// <summary>Only instruments with derivatives (F&O)</summary>
    public bool? DerivativesEnabled { get; set; }

    /// <summary>Trend direction: bullish, bearish, none</summary>
    public string? Trend { get; set; }

    /// <summary>Minimum setup score (0-100)</summary>
    public int? MinSetupScore { get; set; }

    /// <summary>Minimum ADX value</summary>
    public decimal? MinAdx { get; set; }

    /// <summary>RSI below this value (oversold screen)</summary>
    public decimal? RsiBelow { get; set; }

    /// <summary>RSI above this value (overbought screen)</summary>
    public decimal? RsiAbove { get; set; }

    /// <summary>Minimum market cap</summary>
    public decimal? MinMarketCap { get; set; }

    /// <summary>Maximum market cap</summary>
    public decimal? MaxMarketCap { get; set; }

    /// <summary>Minimum daily change %</summary>
    public decimal? MinChangePercent { get; set; }

    /// <summary>Maximum daily change %</summary>
    public decimal? MaxChangePercent { get; set; }

    /// <summary>Only show instruments with active recommendations</summary>
    public bool? HasRecommendation { get; set; }

    /// <summary>Price timeframe for market data. Default: 1D</summary>
    public string PriceTimeframe { get; set; } = "1D";

    /// <summary>Scan timeframe in minutes. Default: 15</summary>
    public int ScanTimeframe { get; set; } = 15;

    /// <summary>Sort field: symbol, price, change, marketCap, confidence, volume. Default: symbol</summary>
    public string SortBy { get; set; } = "symbol";

    /// <summary>Sort direction: asc, desc. Default: asc</summary>
    public string SortDirection { get; set; } = "asc";

    /// <summary>Page number (1-based). Default: 1</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size (1-200). Default: 50</summary>
    public int PageSize { get; set; } = 50;
}

public class PaginatedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}