using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

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

    // ── Revised sentiment score weights (must sum to 100) ────────────────────
    private const decimal IndexTrendWeight   = 25m;  // Price vs 20DMA/50DMA, golden/death cross
    private const decimal MomentumWeight     = 20m;  // RSI zone + MACD histogram direction
    private const decimal SectorBreadthWeight = 20m; // % sectors above 20DMA, top vs bottom sector spread
    private const decimal MarketBreadthWeight = 20m; // McClellan oscillator + new highs/lows ratio
    private const decimal VixTrendWeight     = 15m;  // VIX vs its 20DMA, direction of change

    // ── Sentiment score thresholds ───────────────────────────────────────────
    private const decimal StronglyBullishThreshold = 60m;
    private const decimal BullishThreshold         = 30m;
    private const decimal BearishThreshold         = -30m;
    private const decimal StronglyBearishThreshold = -60m;

    // ── Cache staleness ──────────────────────────────────────────────────────
    private const int StalenessThresholdMinutes = 30;

    // ── Stock universe cap (breadth) ─────────────────────────────────────────
    private const int BreadthStockLimit = 200;

    // ── Widest lookback windows (IST days) ───────────────────────────────────
    private const int IndexLookbackDays   = 75;   // 50DMA needs ~75 days
    private const int SectorLookbackDays  = 30;   // 20DMA for sector breadth
    private const int BreadthLookbackDays = 365;  // 52-week high/low
    private const int VixLookbackDays     = 30;   // VIX 20-day SMA

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

            var indexResult        = await indicesTask;
            var sectorResult       = await sectorsTask;
            var breadthResult      = await breadthTask;
            var volatilityResult   = await volatilityTask;

            var sentimentScore = CalculateSentimentScore(
                indexResult, sectorResult, breadthResult, volatilityResult);
            var sentiment  = DetermineSentiment(sentimentScore);
            var keyFactors = IdentifyKeyFactors(
                indexResult, sectorResult, breadthResult, volatilityResult);

            var marketSentiment = new MarketSentiment
            {
                Timestamp           = DateTimeOffset.UtcNow,
                Sentiment           = sentiment,
                SentimentScore      = sentimentScore,
                VolatilityIndex     = volatilityResult.VixClose,
                MarketBreadth       = breadthResult.BreadthRatio,
                RSI                 = indexResult.AvgRsi,
                MacdHistogram       = indexResult.AvgMacdHistogram,
                PriceVs20DMA        = indexResult.AvgPriceVs20DMA,
                PriceVs50DMA        = indexResult.AvgPriceVs50DMA,
                NewHighs52W         = breadthResult.NewHighs52W,
                NewLows52W          = breadthResult.NewLows52W,
                MclellanOscillator  = breadthResult.MclellanOscillator,
                VixVs20DMA          = volatilityResult.VixVs20DMA,
                IndexPerformance    = indexResult.Performances,
                SectorPerformance   = sectorResult.Performances,
                KeyFactors          = keyFactors,
                CreatedAt           = DateTimeOffset.UtcNow
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
    // Internal result types for richer data flow between helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private sealed record IndexAnalysisResult(
        List<IndexPerformance> Performances,
        decimal AvgRsi,
        decimal AvgMacdHistogram,
        decimal AvgPriceVs20DMA,
        decimal AvgPriceVs50DMA,
        bool GoldenCross);

    private sealed record SectorAnalysisResult(
        List<SectorPerformance> Performances,
        decimal PctSectorsAbove20DMA,
        decimal TopBottomSpread);

    private sealed record BreadthAnalysisResult(
        decimal BreadthRatio,
        int NewHighs52W,
        int NewLows52W,
        decimal MclellanOscillator);

    private sealed record VolatilityResult(
        decimal VixClose,
        decimal VixVs20DMA,
        bool VixRising);

    // ═══════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fetches all active INDEX instruments then retrieves their daily candles
    /// (75 days for 50DMA) in parallel. Computes RSI-14, MACD, 20DMA, 50DMA
    /// per index and returns aggregated averages.
    /// </summary>
    private async Task<IndexAnalysisResult> AnalyzeIndicesAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            var indices = await _instrumentRepository.GetListAsync(
                i => i.InstrumentType == InstrumentType.INDEX && i.IsActive,
                cancellationToken);

            _logger.LogInformation("Found {Count} active indices to analyze", indices.Count);

            var fromDate = istToday.AddDays(-IndexLookbackDays);

            // Fetch candles for every index concurrently — widest window once.
            var tasks = indices.Select(async instrument =>
            {
                try
                {
                    var candles = (await _candleRepository.GetByInstrumentIdAsync(
                        instrument.Id, 1440, fromDate, utcNow, cancellationToken))
                        .OrderBy(c => c.Timestamp)
                        .ToList();

                    if (candles.Count < 2)
                    {
                        _logger.LogDebug("Insufficient candles for index {Symbol}", instrument.Symbol);
                        return null;
                    }

                    var closes = candles.Select(c => c.Close).ToArray();
                    var latestClose = closes[^1];

                    // Daily change: today's close vs previous day's close
                    var previousDayClose = closes[^2];
                    var changePercent = previousDayClose != 0
                        ? (latestClose - previousDayClose) / previousDayClose * 100m
                        : 0m;

                    // Single-pass min/max for today's candle (last in daily series)
                    var lastCandle = candles[^1];
                    var dayHigh = lastCandle.High;
                    var dayLow  = lastCandle.Low;

                    // RSI-14 using existing Indicators.RSI (Wilder's smoothing)
                    var rsiValue = CalculateRsiFromSeries(closes, 14);

                    // MACD using existing Indicators.MACD
                    var (macdHist, _) = CalculateMacdFromSeries(closes);

                    // 20DMA and 50DMA
                    var dma20 = CalculateSMA(closes, 20);
                    var dma50 = CalculateSMA(closes, 50);

                    var priceVs20 = dma20 != 0 ? (latestClose - dma20) / dma20 * 100m : 0m;
                    var priceVs50 = dma50 != 0 ? (latestClose - dma50) / dma50 * 100m : 0m;
                    var isGoldenCross = dma20 > dma50;

                    _logger.LogDebug(
                        "Analyzed index {Symbol}: {Change:F2}% RSI:{RSI:F1} MACD-H:{MACD:F2} vs20:{V20:F2}% vs50:{V50:F2}%",
                        instrument.Symbol, changePercent, rsiValue, macdHist, priceVs20, priceVs50);

                    return new
                    {
                        Performance = new IndexPerformance
                        {
                            IndexName     = instrument.Name,
                            Symbol        = instrument.Symbol,
                            CurrentValue  = latestClose,
                            ChangePercent = changePercent,
                            DayHigh       = dayHigh,
                            DayLow        = dayLow
                        },
                        Rsi           = rsiValue,
                        MacdHist      = macdHist,
                        PriceVs20DMA  = priceVs20,
                        PriceVs50DMA  = priceVs50,
                        GoldenCross   = isGoldenCross
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing index {Symbol}", instrument.Symbol);
                    return null;
                }
            });

            var results = (await Task.WhenAll(tasks)).Where(r => r is not null).ToList();

            if (results.Count == 0)
            {
                return new IndexAnalysisResult(
                    new List<IndexPerformance>(), 50m, 0m, 0m, 0m, false);
            }

            return new IndexAnalysisResult(
                Performances:    results.Select(r => r!.Performance).ToList(),
                AvgRsi:          results.Average(r => r!.Rsi),
                AvgMacdHistogram: results.Average(r => r!.MacdHist),
                AvgPriceVs20DMA: results.Average(r => r!.PriceVs20DMA),
                AvgPriceVs50DMA: results.Average(r => r!.PriceVs50DMA),
                GoldenCross:     results.All(r => r!.GoldenCross));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting indices from database");
            return new IndexAnalysisResult(
                new List<IndexPerformance>(), 50m, 0m, 0m, 0m, false);
        }
    }

    /// <summary>
    /// Analyzes sector performance with 30-day lookback for 20DMA calculations.
    /// Per-sector stock candles are fetched concurrently.
    /// </summary>
    private async Task<SectorAnalysisResult> AnalyzeSectorsAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        try
        {
            var sectors = await _instrumentService.GetSectorsAsync();
            var activeSectors = sectors.Where(s => s.IsActive).ToList();

            _logger.LogInformation("Found {Count} active sectors to analyze", activeSectors.Count);

            var fromDate = istToday.AddDays(-SectorLookbackDays);

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
                            return (await _candleRepository.GetByInstrumentIdAsync(
                                stock.Id, 1440, fromDate, utcNow, cancellationToken))
                                .OrderBy(c => c.Timestamp)
                                .ToList();
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

                    var advancing      = 0;
                    var declining      = 0;
                    var unchanged      = 0;
                    var totalChange    = 0m;
                    var processedCount = 0;
                    var stocksAbove20DMA = 0;
                    var totalStocksWithDMA = 0;

                    foreach (var candles in allCandleSets)
                    {
                        if (candles is null || candles.Count < 2) continue;

                        var closes = candles.Select(c => c.Close).ToArray();
                        var todayClose      = closes[^1];
                        var previousDayClose = closes[^2];

                        var change = previousDayClose != 0
                            ? (todayClose - previousDayClose) / previousDayClose * 100m
                            : 0m;

                        totalChange += change;
                        processedCount++;

                        if      (change >  0.1m) advancing++;
                        else if (change < -0.1m) declining++;
                        else                     unchanged++;

                        // Check if price is above 20DMA
                        if (closes.Length >= 20)
                        {
                            var dma20 = CalculateSMA(closes, 20);
                            totalStocksWithDMA++;
                            if (todayClose > dma20)
                                stocksAbove20DMA++;
                        }
                    }

                    if (processedCount == 0) return null;

                    var avgChange = totalChange / processedCount;

                    // Relative strength: fraction of movers that are advancing.
                    var totalMovers      = advancing + declining;
                    var relativeStrength = totalMovers > 0
                        ? (decimal)advancing / totalMovers
                        : 0.5m;

                    var pctAbove20DMA = totalStocksWithDMA > 0
                        ? (decimal)stocksAbove20DMA / totalStocksWithDMA * 100m
                        : 50m;

                    _logger.LogDebug(
                        "Analyzed sector {SectorName}: {Change:F2}% (A:{A} D:{D} U:{U}) Above20DMA:{Pct:F1}%",
                        sector.Name, avgChange, advancing, declining, unchanged, pctAbove20DMA);

                    return new
                    {
                        Performance = new SectorPerformance
                        {
                            SectorName       = sector.Name,
                            ChangePercent    = avgChange,
                            StocksAdvancing  = advancing,
                            StocksDeclining  = declining,
                            RelativeStrength = relativeStrength
                        },
                        PctAbove20DMA = pctAbove20DMA
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error analyzing sector {SectorName}", sector.Name);
                    return null;
                }
            });

            var results = (await Task.WhenAll(sectorTasks)).Where(r => r is not null).ToList();

            if (results.Count == 0)
            {
                return new SectorAnalysisResult(
                    new List<SectorPerformance>(), 50m, 0m);
            }

            var performances = results.Select(r => r!.Performance).ToList();
            var pctSectorsAbove20DMA = results.Average(r => r!.PctAbove20DMA);

            // Top vs bottom sector spread
            var topChange    = performances.Max(s => s.ChangePercent);
            var bottomChange = performances.Min(s => s.ChangePercent);
            var topBottomSpread = topChange - bottomChange;

            return new SectorAnalysisResult(performances, pctSectorsAbove20DMA, topBottomSpread);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sectors from database");
            return new SectorAnalysisResult(
                new List<SectorPerformance>(), 50m, 0m);
        }
    }

    /// <summary>
    /// Calculates rolling market breadth using 365-day lookback (widest window for 52-week H/L).
    /// Computes 20-day rolling A/D ratio, McClellan Oscillator, and 52-week new highs/lows.
    /// </summary>
    private async Task<BreadthAnalysisResult> CalculateMarketBreadthAsync(
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

            var fromDate = istToday.AddDays(-BreadthLookbackDays);

            // Fetch all candles concurrently — widest window once per stock.
            var candleTasks = stocksToAnalyze.Select(async stock =>
            {
                try
                {
                    return (Stock: stock, Candles: (await _candleRepository.GetByInstrumentIdAsync(
                        stock.Id, 1440, fromDate, utcNow, cancellationToken))
                        .OrderBy(c => c.Timestamp)
                        .ToList());
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Error fetching candles for breadth stock {Symbol}", stock.Symbol);
                    return (Stock: stock, Candles: new List<MarketCandle>());
                }
            });

            var allStockData = await Task.WhenAll(candleTasks);

            // ── Today's A/D count ────────────────────────────────────────────
            var todayAdvancing = 0;
            var todayDeclining = 0;
            var todayUnchanged = 0;

            // ── 52-week new highs/lows ───────────────────────────────────────
            var newHighs52W = 0;
            var newLows52W  = 0;

            // ── Daily A/D diffs for McClellan (need ~39+ days) ───────────────
            // We need to compute daily A/D difference for last ~39 trading days.
            // Collect per-day A/D for the last 40 trading days across all stocks.
            // Strategy: for each stock, determine daily change for last 40 days,
            // then aggregate across stocks per day.
            var dailyAdDiffs = new Dictionary<DateTime, int>(); // date -> (adv - dec) net

            foreach (var (stock, candles) in allStockData)
            {
                if (candles is null || candles.Count < 2) continue;

                var closes = candles.Select(c => c.Close).ToArray();
                var todayClose       = closes[^1];
                var previousDayClose = closes[^2];

                // Today's A/D
                var change = previousDayClose != 0
                    ? (todayClose - previousDayClose) / previousDayClose * 100m
                    : 0m;

                if      (change >  0.01m) todayAdvancing++;
                else if (change < -0.01m) todayDeclining++;
                else                      todayUnchanged++;

                // 52-week high/low — single-pass
                decimal high52 = decimal.MinValue;
                decimal low52  = decimal.MaxValue;
                foreach (var c in candles)
                {
                    if (c.High > high52) high52 = c.High;
                    if (c.Low  < low52)  low52  = c.Low;
                }

                if (todayClose >= high52 * 0.98m) newHighs52W++;
                if (todayClose <= low52  * 1.02m) newLows52W++;

                // Daily A/D diffs for McClellan — last 40 trading days
                var recentCandles = candles.Count > 41
                    ? candles.Skip(candles.Count - 41).ToList()
                    : candles;

                for (int i = 1; i < recentCandles.Count; i++)
                {
                    var prevClose = recentCandles[i - 1].Close;
                    var curClose  = recentCandles[i].Close;
                    if (prevClose == 0) continue;

                    var dayChange = (curClose - prevClose) / prevClose * 100m;
                    var date = TimeZoneInfo.ConvertTime(recentCandles[i].Timestamp, Ist).Date;

                    if (!dailyAdDiffs.ContainsKey(date))
                        dailyAdDiffs[date] = 0;

                    if      (dayChange >  0.01m) dailyAdDiffs[date]++;
                    else if (dayChange < -0.01m) dailyAdDiffs[date]--;
                }
            }

            // ── 20-day rolling A/D ratio ─────────────────────────────────────
            decimal breadthRatio = todayDeclining > 0
                ? (decimal)todayAdvancing / todayDeclining
                : todayAdvancing > 0 ? 10m : 1.0m;

            // ── McClellan Oscillator ─────────────────────────────────────────
            // EMA(19) of daily A/D diff - EMA(39) of daily A/D diff
            var sortedDays = dailyAdDiffs.OrderBy(kv => kv.Key).Select(kv => (decimal)kv.Value).ToArray();
            var mclellan = 0m;
            if (sortedDays.Length >= 39)
            {
                var ema19Series = EMA.CalculateSeries(sortedDays, 19);
                var ema39Series = EMA.CalculateSeries(sortedDays, 39);
                mclellan = ema19Series[^1] - ema39Series[^1];
            }

            _logger.LogInformation(
                "Market breadth: A:{A} D:{D} U:{U} Ratio:{R:F2} NewHighs:{NH} NewLows:{NL} McClellan:{MC:F2}",
                todayAdvancing, todayDeclining, todayUnchanged, breadthRatio,
                newHighs52W, newLows52W, mclellan);

            return new BreadthAnalysisResult(breadthRatio, newHighs52W, newLows52W, mclellan);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating market breadth");
            return new BreadthAnalysisResult(1.0m, 0, 0, 0m);
        }
    }

    /// <summary>
    /// Fetches India VIX with 30-day lookback for 20-day SMA comparison.
    /// Returns VIX close, VIX vs 20DMA, and whether VIX is rising.
    /// </summary>
    private async Task<VolatilityResult> GetVolatilityIndexAsync(
        DateTime istToday, DateTime utcNow, CancellationToken cancellationToken)
    {
        var defaultResult = new VolatilityResult(20m, 0m, false);

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
                return defaultResult;
            }

            var fromDate = istToday.AddDays(-VixLookbackDays);
            var candles = (await _candleRepository.GetByInstrumentIdAsync(
                vix.Id, 1440, fromDate, utcNow, cancellationToken))
                .OrderBy(c => c.Timestamp)
                .ToList();

            if (candles.Count == 0)
            {
                _logger.LogWarning("No VIX candles available");
                return defaultResult;
            }

            var closes = candles.Select(c => c.Close).ToArray();
            var latestClose = closes[^1];

            // VIX 20-day SMA
            var vix20DMA  = CalculateSMA(closes, 20);
            var vixVs20DMA = latestClose - vix20DMA;

            // VIX rising = latest > previous day
            var vixRising = closes.Length >= 2 && closes[^1] > closes[^2];

            _logger.LogInformation(
                "India VIX: {VIX:F2} vs 20DMA:{DMA:F2} (diff:{Diff:F2}) Rising:{Rising}",
                latestClose, vix20DMA, vixVs20DMA, vixRising);

            return new VolatilityResult(latestClose, vixVs20DMA, vixRising);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting volatility index");
            return defaultResult;
        }
    }

    // ── Technical Indicator Helpers (reuse existing Indicators library) ──────

    /// <summary>
    /// Computes RSI-14 with Wilder's smoothing using the existing RSI indicator class.
    /// Returns the final RSI value for the series.
    /// </summary>
    private static decimal CalculateRsiFromSeries(decimal[] closes, int period)
    {
        if (closes.Length < period + 1) return 50m;

        var rsiSeries = RSI.CalculateSeries(closes, period);
        // Return the last valid (non-zero) value
        for (int i = rsiSeries.Length - 1; i >= 0; i--)
        {
            if (rsiSeries[i] != 0m) return rsiSeries[i];
        }
        return 50m;
    }

    /// <summary>
    /// Computes MACD histogram and signal using the existing MACD indicator class.
    /// Returns (histogram, macdLine) for the latest bar.
    /// </summary>
    private static (decimal Histogram, decimal MacdLine) CalculateMacdFromSeries(decimal[] closes)
    {
        if (closes.Length < 35) return (0m, 0m); // Need 26 + 9 bars minimum

        var (macdLines, signalLines, histograms) = MACD.CalculateSeries(closes);
        return (histograms[^1], macdLines[^1]);
    }

    /// <summary>
    /// Computes a simple moving average of the last N values.
    /// Returns 0 if insufficient data.
    /// </summary>
    private static decimal CalculateSMA(decimal[] values, int period)
    {
        if (values.Length < period) return 0m;
        var sum = 0m;
        for (int i = values.Length - period; i < values.Length; i++)
            sum += values[i];
        return sum / period;
    }

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Produces a sentiment score in [-100, 100] using the revised 5-component model.
    ///
    ///   Component                    Weight   Signal
    ///   ─────────────────────────    ──────   ──────────────────────────────
    ///   Index trend (MA + price)      25 pts  Price vs 20DMA/50DMA, golden/death cross
    ///   Index momentum (RSI+MACD)     20 pts  RSI zone + MACD histogram direction
    ///   Sector rotation breadth       20 pts  % sectors above 20DMA, top vs bottom spread
    ///   Market breadth (A/D+52wH/L)   20 pts  McClellan oscillator + new highs/lows ratio
    ///   VIX trend                     15 pts  VIX vs its 20DMA, direction of change
    /// </summary>
    private static decimal CalculateSentimentScore(
        IndexAnalysisResult    indexResult,
        SectorAnalysisResult   sectorResult,
        BreadthAnalysisResult  breadthResult,
        VolatilityResult       volatilityResult)
    {
        // ── 1. Index trend (25 pts) ─────────────────────────────────────────
        // Price vs 20DMA: maps ±5% to ±10 pts
        // Price vs 50DMA: maps ±5% to ±10 pts
        // Golden/Death cross: ±5 pts
        var trendScore = 0m;
        trendScore += Math.Clamp(indexResult.AvgPriceVs20DMA / 5m * 10m, -10m, 10m);
        trendScore += Math.Clamp(indexResult.AvgPriceVs50DMA / 5m * 10m, -10m, 10m);
        trendScore += indexResult.GoldenCross ? 5m : -5m;
        trendScore = Math.Clamp(trendScore, -IndexTrendWeight, IndexTrendWeight);

        // ── 2. Index momentum (20 pts) ──────────────────────────────────────
        // RSI: 30-70 neutral band, outside → bullish/bearish signal (±10 pts)
        // MACD histogram direction: maps value to ±10 pts
        var momentumScore = 0m;
        var rsi = indexResult.AvgRsi;
        if (rsi > 70m)
            momentumScore += -Math.Clamp((rsi - 70m) / 30m * 10m, 0m, 10m); // overbought = caution
        else if (rsi < 30m)
            momentumScore += -Math.Clamp((30m - rsi) / 30m * 10m, 0m, 10m); // oversold = caution
        else
            momentumScore += Math.Clamp((rsi - 50m) / 20m * 10m, -10m, 10m); // above 50 = bullish

        // MACD histogram: positive = bullish, negative = bearish
        momentumScore += Math.Clamp(indexResult.AvgMacdHistogram * 50m, -10m, 10m);
        momentumScore = Math.Clamp(momentumScore, -MomentumWeight, MomentumWeight);

        // ── 3. Sector rotation breadth (20 pts) ─────────────────────────────
        // % sectors above 20DMA: 50% = neutral, 80%+ = bullish, 20%- = bearish (±15 pts)
        // Top vs bottom sector spread: wide spread = rotation, narrow = conviction (±5 pts)
        var sectorScore = 0m;
        sectorScore += Math.Clamp((sectorResult.PctSectorsAbove20DMA - 50m) / 30m * 15m, -15m, 15m);
        // Large spread (>5%) suggests divergence; narrow spread with high breadth is stronger
        sectorScore += sectorResult.TopBottomSpread > 5m ? -2.5m : 2.5m;
        if (sectorResult.Performances.Count > 0)
        {
            var avgSectorChange = sectorResult.Performances.Average(s => s.ChangePercent);
            sectorScore += Math.Clamp(avgSectorChange / 2m * 2.5m, -2.5m, 2.5m);
        }
        sectorScore = Math.Clamp(sectorScore, -SectorBreadthWeight, SectorBreadthWeight);

        // ── 4. Market breadth (20 pts) ──────────────────────────────────────
        // McClellan oscillator: maps to ±10 pts (positive = bullish breadth)
        // New highs vs new lows ratio: maps to ±10 pts
        var breadthScore = 0m;
        breadthScore += Math.Clamp(breadthResult.MclellanOscillator / 50m * 10m, -10m, 10m);

        var totalHiLo = breadthResult.NewHighs52W + breadthResult.NewLows52W;
        if (totalHiLo > 0)
        {
            var hiLoRatio = (decimal)breadthResult.NewHighs52W / totalHiLo; // 0.0 to 1.0
            breadthScore += Math.Clamp((hiLoRatio - 0.5m) * 20m, -10m, 10m);
        }
        breadthScore = Math.Clamp(breadthScore, -MarketBreadthWeight, MarketBreadthWeight);

        // ── 5. VIX trend (15 pts) ───────────────────────────────────────────
        // VIX vs 20DMA: positive diff = fear increasing = bearish
        // VIX rising = bearish signal
        var volScore = 0m;
        // VIX above 20DMA by 3+ pts = full bearish; below by 3+ pts = full bullish
        volScore += Math.Clamp(-volatilityResult.VixVs20DMA / 3m * 10m, -10m, 10m);
        volScore += volatilityResult.VixRising ? -5m : 5m;
        volScore = Math.Clamp(volScore, -VixTrendWeight, VixTrendWeight);

        var total = trendScore + momentumScore + sectorScore + breadthScore + volScore;
        return Math.Clamp(total, -100m, 100m);
    }

    private static SentimentType DetermineSentiment(decimal score) => score switch
    {
        > StronglyBullishThreshold => SentimentType.STRONGLY_BULLISH,
        > BullishThreshold         => SentimentType.BULLISH,
        < StronglyBearishThreshold => SentimentType.STRONGLY_BEARISH,
        < BearishThreshold         => SentimentType.BEARISH,
        _                          => SentimentType.NEUTRAL
    };

    private static List<string> IdentifyKeyFactors(
        IndexAnalysisResult    indexResult,
        SectorAnalysisResult   sectorResult,
        BreadthAnalysisResult  breadthResult,
        VolatilityResult       volatilityResult)
    {
        var factors = new List<string>();
        var indices = indexResult.Performances;
        var sectors = sectorResult.Performances;

        // Index performance factors
        var strongIndices = indices.Where(i => i.ChangePercent >  1m).ToList();
        var weakIndices   = indices.Where(i => i.ChangePercent < -1m).ToList();

        if (strongIndices.Count > 0)
            factors.Add($"Strong performance: {string.Join(", ", strongIndices.Select(i => i.IndexName))}");
        if (weakIndices.Count > 0)
            factors.Add($"Weak performance: {string.Join(", ", weakIndices.Select(i => i.IndexName))}");

        // MA trend factors
        if (indexResult.GoldenCross)
            factors.Add("Golden cross: 20DMA above 50DMA across indices");
        else
            factors.Add("Death cross: 20DMA below 50DMA across indices");

        if (Math.Abs(indexResult.AvgPriceVs20DMA) > 2m)
            factors.Add($"Indices {(indexResult.AvgPriceVs20DMA > 0 ? "above" : "below")} 20DMA by {indexResult.AvgPriceVs20DMA:F1}%");

        // RSI factors
        if (indexResult.AvgRsi > 70m)
            factors.Add($"Market overbought (RSI: {indexResult.AvgRsi:F1})");
        else if (indexResult.AvgRsi < 30m)
            factors.Add($"Market oversold (RSI: {indexResult.AvgRsi:F1})");

        // MACD factors
        if (indexResult.AvgMacdHistogram > 0)
            factors.Add($"Bullish MACD momentum (histogram: {indexResult.AvgMacdHistogram:F2})");
        else if (indexResult.AvgMacdHistogram < 0)
            factors.Add($"Bearish MACD momentum (histogram: {indexResult.AvgMacdHistogram:F2})");

        // Sector factors
        var topSector    = sectors.MaxBy(s => s.ChangePercent);
        var bottomSector = sectors.MinBy(s => s.ChangePercent);

        if (topSector is not null && topSector.ChangePercent > 0.5m)
            factors.Add($"{topSector.SectorName} sector leading ({topSector.ChangePercent:F2}%)");
        if (bottomSector is not null && bottomSector.ChangePercent < -0.5m)
            factors.Add($"{bottomSector.SectorName} sector lagging ({bottomSector.ChangePercent:F2}%)");

        factors.Add($"Sectors above 20DMA: {sectorResult.PctSectorsAbove20DMA:F0}%");

        // Breadth factors
        if (breadthResult.BreadthRatio > 1.5m)
            factors.Add($"Strong market breadth (A/D: {breadthResult.BreadthRatio:F2})");
        else if (breadthResult.BreadthRatio < 0.67m)
            factors.Add($"Weak market breadth (A/D: {breadthResult.BreadthRatio:F2})");

        if (breadthResult.NewHighs52W > breadthResult.NewLows52W)
            factors.Add($"52-week highs ({breadthResult.NewHighs52W}) > lows ({breadthResult.NewLows52W})");
        else if (breadthResult.NewLows52W > breadthResult.NewHighs52W)
            factors.Add($"52-week lows ({breadthResult.NewLows52W}) > highs ({breadthResult.NewHighs52W})");

        if (Math.Abs(breadthResult.MclellanOscillator) > 20m)
            factors.Add($"McClellan Oscillator: {breadthResult.MclellanOscillator:F1} ({(breadthResult.MclellanOscillator > 0 ? "bullish" : "bearish")} breadth)");

        // VIX factors
        if (volatilityResult.VixClose > 25m)
            factors.Add($"High volatility (VIX: {volatilityResult.VixClose:F2})");
        else if (volatilityResult.VixClose < 12m)
            factors.Add($"Low volatility (VIX: {volatilityResult.VixClose:F2})");

        if (Math.Abs(volatilityResult.VixVs20DMA) > 2m)
            factors.Add($"VIX {(volatilityResult.VixVs20DMA > 0 ? "above" : "below")} 20DMA by {Math.Abs(volatilityResult.VixVs20DMA):F1} pts ({(volatilityResult.VixRising ? "rising" : "falling")})");

        return factors;
    }

    // ── Mapping ──────────────────────────────────────────────────────────────

    private MarketContext MapToMarketContext(MarketSentiment sentiment)
    {
        return new MarketContext
        {
            Timestamp          = sentiment.Timestamp,
            Sentiment          = sentiment.Sentiment,
            SentimentScore     = sentiment.SentimentScore,
            VolatilityIndex    = sentiment.VolatilityIndex,
            MarketBreadth      = sentiment.MarketBreadth,
            RSI                = sentiment.RSI,
            MacdHistogram      = sentiment.MacdHistogram,
            PriceVs20DMA       = sentiment.PriceVs20DMA,
            PriceVs50DMA       = sentiment.PriceVs50DMA,
            NewHighs52W        = sentiment.NewHighs52W,
            NewLows52W         = sentiment.NewLows52W,
            MclellanOscillator = sentiment.MclellanOscillator,
            VixVs20DMA         = sentiment.VixVs20DMA,
            MajorIndices       = sentiment.IndexPerformance  ?? new List<IndexPerformance>(),
            Sectors            = sentiment.SectorPerformance ?? new List<SectorPerformance>(),
            KeyFactors         = sentiment.KeyFactors        ?? new List<string>(),
            Summary            = GenerateSummary(
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