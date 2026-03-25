using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Data.Services;

public class MarketSentimentService : IMarketSentimentService
{
    // ── Timezone ────────────────────────────────────────────────────────────────
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    /// <summary>Consistent IST "today" for the lifetime of a single call-chain.</summary>
    private static DateTime IstNow =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);

    private static DateTime IstToday => IstNow.Date;

    // ── Sentiment score weights (must sum to 100) ────────────────────────────
    private const decimal IndexWeight    = 40m;
    private const decimal SectorWeight   = 30m;
    private const decimal BreadthWeight  = 20m;
    private const decimal VolWeight      = 10m;

    // ── Sentiment score thresholds ───────────────────────────────────────────
    private const decimal BullishThreshold = 30m;
    private const decimal BearishThreshold = -30m;

    // ── Cache staleness ──────────────────────────────────────────────────────
    private const int StalenessThresholdMinutes = 30;

    // ── Stock universe cap (breadth) ─────────────────────────────────────────
    private const int BreadthStockLimit = 200;

    private readonly IMarketSentimentRepository _sentimentRepository;
    private readonly IInstrumentRepository      _instrumentRepository;
    private readonly IMarketCandleRepository    _candleRepository;
    private readonly IInstrumentService         _instrumentService;
    private readonly ILogger<MarketSentimentService> _logger;

    public MarketSentimentService(
        IMarketSentimentRepository sentimentRepository,
        IInstrumentRepository      instrumentRepository,
        IMarketCandleRepository    candleRepository,
        IInstrumentService         instrumentService,
        ILogger<MarketSentimentService> logger)
    {
        _sentimentRepository = sentimentRepository;
        _instrumentRepository = instrumentRepository;
        _candleRepository    = candleRepository;
        _instrumentService   = instrumentService;
        _logger              = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<MarketContext> GetCurrentMarketContextAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var latestSentiment = await _sentimentRepository.GetLatestAsync(cancellationToken);

            bool isStale = latestSentiment is null ||
                           (DateTimeOffset.UtcNow - latestSentiment.Timestamp).TotalMinutes
                           > StalenessThresholdMinutes;

            return isStale
                ? await AnalyzeAndUpdateMarketSentimentAsync(cancellationToken)
                : MapToMarketContext(latestSentiment!);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current market context");
            throw;
        }
    }

    public async Task<MarketContext> AnalyzeAndUpdateMarketSentimentAsync(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting market sentiment analysis at {Time}", DateTime.UtcNow);

        try
        {
            // Snapshot a single IST "now" so every sub-task shares the same boundary.
            var istToday = IstToday;
            var utcNow   = DateTime.UtcNow;

            // Run independent data fetches concurrently.
            var indicesTask     = AnalyzeIndicesAsync(istToday, utcNow, cancellationToken);
            var sectorsTask     = AnalyzeSectorsAsync(istToday, utcNow, cancellationToken);
            var breadthTask     = CalculateMarketBreadthAsync(istToday, utcNow, cancellationToken);
            var volatilityTask  = GetVolatilityIndexAsync(istToday, utcNow, cancellationToken);

            await Task.WhenAll(indicesTask, sectorsTask, breadthTask, volatilityTask);

            var indexPerformances  = await indicesTask;
            var sectorPerformances = await sectorsTask;
            var breadth            = await breadthTask;
            var volatilityIndex    = await volatilityTask;

            var sentimentScore = CalculateSentimentScore(
                indexPerformances, sectorPerformances, breadth, volatilityIndex);
            var sentiment  = DetermineSentiment(sentimentScore);
            var keyFactors = IdentifyKeyFactors(
                indexPerformances, sectorPerformances, breadth, volatilityIndex);

            var marketSentiment = new MarketSentiment
            {
                Timestamp          = DateTimeOffset.UtcNow,
                Sentiment          = sentiment,
                SentimentScore     = sentimentScore,
                VolatilityIndex    = volatilityIndex,
                MarketBreadth      = breadth,
                IndexPerformance   = indexPerformances,
                SectorPerformance  = sectorPerformances,
                KeyFactors         = keyFactors,
                CreatedAt          = DateTimeOffset.UtcNow
            };

            await _sentimentRepository.AddAsync(marketSentiment, cancellationToken);

            _logger.LogInformation(
                "Market sentiment analysis completed: {Sentiment} ({Score:F1})",
                sentiment, sentimentScore);

            return MapToMarketContext(marketSentiment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing market sentiment");
            throw;
        }
    }

    public async Task<List<MarketSentiment>> GetHistoryAsync(
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return await _sentimentRepository.GetHistoryAsync(fromDate, toDate, cancellationToken);
    }

    /// <summary>
    /// Adjusts base confidence (0–100) by ±10 % depending on sentiment.
    /// Strongly bullish/bearish variants are handled explicitly.
    /// </summary>
    public decimal AdjustConfidenceForMarketSentiment(
        decimal baseConfidence, SentimentType sentiment)
    {
        var multiplier = sentiment switch
        {
            SentimentType.STRONGLY_BULLISH => 1.15m,
            SentimentType.BULLISH          => 1.10m,
            SentimentType.NEUTRAL          => 1.00m,
            SentimentType.BEARISH          => 0.90m,
            SentimentType.STRONGLY_BEARISH => 0.85m,
            _                              => 1.00m
        };

        return Math.Clamp(baseConfidence * multiplier, 0m, 100m);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches all active INDEX instruments then retrieves their candles in
    /// parallel — one Task per index — to eliminate sequential N+1 latency.
    /// </summary>
    private async Task<List<IndexPerformance>> AnalyzeIndicesAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            var indices = await _instrumentRepository.GetListAsync(
                i => i.InstrumentType == InstrumentType.INDEX && i.IsActive,
                cancellationToken);

            _logger.LogInformation("Found {Count} active indices to analyze", indices.Count);

            // Fetch candles for every index concurrently.
            var tasks = indices.Select(async instrument =>
            {
                try
                {
                    var candles = (await _candleRepository.GetByInstrumentIdAsync(
                        instrument.Id, 1, istToday, utcNow, cancellationToken))
                        .OrderBy(c => c.Timestamp)
                        .ToList();

                    if (candles.Count == 0)
                    {
                        _logger.LogDebug("No candles found for index {Symbol}", instrument.Symbol);
                        return null;
                    }

                    var firstCandle  = candles[0];
                    var latestCandle = candles[^1];

                    var changePercent = firstCandle.Open != 0
                        ? (latestCandle.Close - firstCandle.Open) / firstCandle.Open * 100m
                        : 0m;

                    // Single-pass min/max — avoids multiple enumerations.
                    var dayHigh = candles[0].High;
                    var dayLow  = candles[0].Low;
                    foreach (var c in candles)
                    {
                        if (c.High > dayHigh) dayHigh = c.High;
                        if (c.Low  < dayLow)  dayLow  = c.Low;
                    }

                    _logger.LogDebug("Analyzed index {Symbol}: {Change:F2}%",
                        instrument.Symbol, changePercent);

                    return (IndexPerformance?)new IndexPerformance
                    {
                        IndexName     = instrument.Name,
                        Symbol        = instrument.Symbol,
                        CurrentValue  = latestCandle.Close,
                        ChangePercent = changePercent,
                        DayHigh       = dayHigh,
                        DayLow        = dayLow
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing index {Symbol}", instrument.Symbol);
                    return null;
                }
            });

            var results = await Task.WhenAll(tasks);
            return results.Where(r => r is not null).Select(r => r!).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting indices from database");
            return new List<IndexPerformance>();
        }
    }

    /// <summary>
    /// Analyzes sector performance.
    /// Per-sector stock candles are fetched concurrently; within each sector the
    /// stocks are also processed concurrently to minimize wall-clock time.
    /// </summary>
    private async Task<List<SectorPerformance>> AnalyzeSectorsAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            var sectors = await _instrumentService.GetSectorsAsync();
            var activeSectors = sectors.Where(s => s.IsActive).ToList();

            _logger.LogInformation("Found {Count} active sectors to analyze", activeSectors.Count);

            var sectorTasks = activeSectors.Select(async sector =>
            {
                try
                {
                    var sectorStocks = await _instrumentRepository.GetListAsync(
                        i => i.SectorId == sector.Id
                             && i.InstrumentType == InstrumentType.STOCK
                             && i.IsActive,
                        cancellationToken);

                    if (sectorStocks.Count == 0)
                    {
                        _logger.LogDebug("No stocks found for sector {SectorName}", sector.Name);
                        return null;
                    }

                    // Fetch candles for every stock in this sector concurrently.
                    var stockTasks = sectorStocks.Select(async stock =>
                    {
                        try
                        {
                            return await _candleRepository.GetByInstrumentIdAsync(
                                stock.Id, 1, istToday, utcNow, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex,
                                "Error fetching candles for stock {Symbol} in sector {SectorName}",
                                stock.Symbol, sector.Name);
                            return new List<MarketCandle>();
                        }
                    });

                    var allCandleSets = await Task.WhenAll(stockTasks);

                    var advancing     = 0;
                    var declining     = 0;
                    var unchanged     = 0;
                    var totalChange   = 0m;
                    var processedCount = 0;

                    foreach (var candles in allCandleSets)
                    {
                        if (candles is null || candles.Count == 0) continue;

                        var ordered      = candles.OrderBy(c => c.Timestamp).ToList();
                        var firstCandle  = ordered[0];
                        var latestCandle = ordered[^1];

                        var change = firstCandle.Open != 0
                            ? (latestCandle.Close - firstCandle.Open) / firstCandle.Open * 100m
                            : 0m;

                        totalChange += change;
                        processedCount++;

                        if      (change >  0.1m) advancing++;
                        else if (change < -0.1m) declining++;
                        else                     unchanged++;
                    }

                    if (processedCount == 0) return null;

                    var avgChange = totalChange / processedCount;

                    // Relative strength: fraction of movers that are advancing.
                    var totalMovers   = advancing + declining;
                    var relativeStrength = totalMovers > 0
                        ? (decimal)advancing / totalMovers
                        : 0.5m;

                    _logger.LogDebug(
                        "Analyzed sector {SectorName}: {Change:F2}% (A:{A} D:{D} U:{U})",
                        sector.Name, avgChange, advancing, declining, unchanged);

                    return (SectorPerformance?)new SectorPerformance
                    {
                        SectorName       = sector.Name,
                        ChangePercent    = avgChange,
                        StocksAdvancing  = advancing,
                        StocksDeclining  = declining,
                        RelativeStrength = relativeStrength
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing sector {SectorName}", sector.Name);
                    return null;
                }
            });

            var results = await Task.WhenAll(sectorTasks);
            return results.Where(r => r is not null).Select(r => r!).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sectors from database");
            return new List<SectorPerformance>();
        }
    }

    /// <summary>
    /// Calculates the advance/decline ratio for up to <see cref="BreadthStockLimit"/> stocks,
    /// ordered by market cap descending. Candles are fetched concurrently.
    /// </summary>
    private async Task<decimal> CalculateMarketBreadthAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            var activeStocks = await _instrumentRepository.GetListAsync(
                i => i.InstrumentType == InstrumentType.STOCK && i.IsActive,
                cancellationToken);

            var stocksToAnalyze = activeStocks
                .OrderByDescending(s => s.MarketCap ?? 0)
                .Take(BreadthStockLimit)
                .ToList();

            _logger.LogInformation(
                "Calculating market breadth for {Count} stocks", stocksToAnalyze.Count);

            // Fetch all candles concurrently.
            var candleTasks = stocksToAnalyze.Select(async stock =>
            {
                try
                {
                    return await _candleRepository.GetByInstrumentIdAsync(
                        stock.Id, 1, istToday, utcNow, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Error fetching candles for breadth stock {Symbol}", stock.Symbol);
                    return new List<MarketCandle>();
                }
            });

            var allCandleSets = await Task.WhenAll(candleTasks);

            var advancing = 0;
            var declining = 0;
            var unchanged = 0;

            foreach (var candles in allCandleSets)
            {
                if (candles is null || candles.Count == 0) continue;

                var firstCandle  = candles.MinBy(c => c.Timestamp)!;
                var latestCandle = candles.MaxBy(c => c.Timestamp)!;

                var change = firstCandle.Open != 0
                    ? (latestCandle.Close - firstCandle.Open) / firstCandle.Open * 100m
                    : 0m;

                if      (change >  0.01m) advancing++;
                else if (change < -0.01m) declining++;
                else                     unchanged++;
            }

            // Pure A/D ratio with sensible edge-case handling.
            decimal breadthRatio = declining > 0
                ? (decimal)advancing / declining
                : advancing > 0 ? 10m : 1.0m;

            _logger.LogInformation(
                "Market breadth: A:{A} D:{D} U:{U} Ratio:{R:F2}",
                advancing, declining, unchanged, breadthRatio);

            return breadthRatio;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating market breadth");
            return 1.0m; // Neutral fallback
        }
    }

    /// <summary>
    /// Fetches India VIX from the database using the IST date boundary.
    /// </summary>
    private async Task<decimal> GetVolatilityIndexAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        const decimal defaultVix = 20m;

        try
        {
            var vixInstruments = await _instrumentRepository.GetListAsync(
                i => i.Symbol.Contains("VIX")
                     && i.InstrumentType == InstrumentType.INDEX
                     && i.IsActive,
                cancellationToken);

            var vix = vixInstruments.FirstOrDefault();
            if (vix is null)
            {
                _logger.LogWarning("India VIX instrument not found in database");
                return defaultVix;
            }

            var candles = await _candleRepository.GetByInstrumentIdAsync(
                vix.Id, 1, istToday, utcNow, cancellationToken);

            if (candles.Count == 0)
            {
                _logger.LogWarning("No VIX candles available for today");
                return defaultVix;
            }

            var latestClose = candles.MaxBy(c => c.Timestamp)!.Close;
            _logger.LogInformation("India VIX: {VIX:F2}", latestClose);
            return latestClose;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting volatility index");
            return defaultVix;
        }
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a sentiment score in [-100, 100].
    ///
    /// Each sub-score is individually clamped to its weight band before
    /// summing, so no single factor can dominate the final result.
    ///
    ///   Component          Weight   Raw driver
    ///   ─────────────────  ──────   ──────────────────────────────────────
    ///   Index performance   40 pts  average daily % change, ±2 % → full band
    ///   Sector performance  30 pts  average daily % change, ±2 % → full band
    ///   Market breadth      20 pts  A/D ratio mapped via smooth curve
    ///   Volatility (VIX)    10 pts  linear interpolation 12–25, inverted
    /// </summary>
    private static decimal CalculateSentimentScore(
        List<IndexPerformance>  indices,
        List<SectorPerformance> sectors,
        decimal breadth,
        decimal volatility)
    {
        // ── Index sub-score ─────────────────────────────────────────────────
        // ±2 % average change maps to the full ±40 band; clamped beyond that.
        decimal indexScore = 0m;
        if (indices.Count > 0)
        {
            var avgChange = indices.Average(i => i.ChangePercent);
            indexScore = Math.Clamp(avgChange / 2m * IndexWeight, -IndexWeight, IndexWeight);
        }

        // ── Sector sub-score ────────────────────────────────────────────────
        // Same mapping: ±2 % → ±30.
        decimal sectorScore = 0m;
        if (sectors.Count > 0)
        {
            var avgChange = sectors.Average(s => s.ChangePercent);
            sectorScore = Math.Clamp(avgChange / 2m * SectorWeight, -SectorWeight, SectorWeight);
        }

        // ── Breadth sub-score ───────────────────────────────────────────────
        // A/D ratio of 1.0 → 0 pts (neutral).
        // A/D ratio of 2.0 → +20 pts (all advancing).
        // A/D ratio of 0.5 → -20 pts (all declining).
        // Uses log-ratio so it's symmetric and bounded.
        decimal breadthScore;
        if (breadth <= 0m)
        {
            breadthScore = -BreadthWeight;
        }
        else
        {
            // log2(ratio): 0 at 1.0, +1 at 2.0, -1 at 0.5
            var logRatio = (decimal)Math.Log2((double)breadth);
            breadthScore = Math.Clamp(logRatio * BreadthWeight, -BreadthWeight, BreadthWeight);
        }

        // ── Volatility sub-score ────────────────────────────────────────────
        // VIX ≤ 12  → +10 pts (calm market, confidence boost)
        // VIX ≥ 25  → -10 pts (fearful market, confidence hit)
        // Linear interpolation between 12 and 25.
        decimal volScore;
        if (volatility <= 12m)
            volScore = VolWeight;
        else if (volatility >= 25m)
            volScore = -VolWeight;
        else
            volScore = VolWeight - (volatility - 12m) / (25m - 12m) * (2m * VolWeight);

        var total = indexScore + sectorScore + breadthScore + volScore;
        return Math.Clamp(total, -100m, 100m);
    }

    private static SentimentType DetermineSentiment(decimal score) => score switch
    {
        > BullishThreshold => SentimentType.BULLISH,
        < BearishThreshold => SentimentType.BEARISH,
        _                  => SentimentType.NEUTRAL
    };

    private static List<string> IdentifyKeyFactors(
        List<IndexPerformance>  indices,
        List<SectorPerformance> sectors,
        decimal breadth,
        decimal volatility)
    {
        var factors = new List<string>();

        var strongIndices = indices.Where(i => i.ChangePercent >  1m).ToList();
        var weakIndices   = indices.Where(i => i.ChangePercent < -1m).ToList();

        if (strongIndices.Count > 0)
            factors.Add($"Strong performance: {string.Join(", ", strongIndices.Select(i => i.IndexName))}");
        if (weakIndices.Count > 0)
            factors.Add($"Weak performance: {string.Join(", ", weakIndices.Select(i => i.IndexName))}");

        var topSector    = sectors.MaxBy(s => s.ChangePercent);
        var bottomSector = sectors.MinBy(s => s.ChangePercent);

        if (topSector is not null && topSector.ChangePercent > 0.5m)
            factors.Add($"{topSector.SectorName} sector leading ({topSector.ChangePercent:F2}%)");
        if (bottomSector is not null && bottomSector.ChangePercent < -0.5m)
            factors.Add($"{bottomSector.SectorName} sector lagging ({bottomSector.ChangePercent:F2}%)");

        if      (breadth > 1.5m)  factors.Add($"Strong market breadth (A/D: {breadth:F2})");
        else if (breadth < 0.67m) factors.Add($"Weak market breadth (A/D: {breadth:F2})");

        if      (volatility > 25m) factors.Add($"High volatility (VIX: {volatility:F2})");
        else if (volatility < 12m) factors.Add($"Low volatility (VIX: {volatility:F2})");

        return factors;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private MarketContext MapToMarketContext(MarketSentiment sentiment)
    {
        return new MarketContext
        {
            Timestamp       = sentiment.Timestamp,
            Sentiment       = sentiment.Sentiment,
            SentimentScore  = sentiment.SentimentScore,
            VolatilityIndex = sentiment.VolatilityIndex,
            MarketBreadth   = sentiment.MarketBreadth,
            MajorIndices    = sentiment.IndexPerformance  ?? new List<IndexPerformance>(),
            Sectors         = sentiment.SectorPerformance ?? new List<SectorPerformance>(),
            KeyFactors      = sentiment.KeyFactors        ?? new List<string>(),
            Summary         = GenerateSummary(
                                  sentiment.Sentiment,
                                  sentiment.SentimentScore,
                                  sentiment.VolatilityIndex)
        };
    }

    private static string GenerateSummary(
        SentimentType sentiment, decimal score, decimal volatility)
    {
        var sentimentText = sentiment switch
        {
            SentimentType.STRONGLY_BULLISH => "strongly bullish",
            SentimentType.BULLISH          => "bullish",
            SentimentType.BEARISH          => "bearish",
            SentimentType.STRONGLY_BEARISH => "strongly bearish",
            _                              => "neutral"
        };

        var volText = volatility switch
        {
            < 12m  => "low volatility",
            >= 25m => "high volatility",
            _      => "moderate volatility"
        };

        return $"Market sentiment is {sentimentText} (score: {score:F1}) with {volText} " +
               $"(VIX: {volatility:F1}). Trading recommendations are adjusted accordingly.";
    }
}