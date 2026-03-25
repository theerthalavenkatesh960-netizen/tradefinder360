using Microsoft.EntityFrameworkCore;
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
    private readonly IDbContextFactory<TradingDbContext> _contextFactory;

    public MarketSentimentService(
        IMarketSentimentRepository sentimentRepository,
        IInstrumentRepository      instrumentRepository,
        IMarketCandleRepository    candleRepository,
        IInstrumentService         instrumentService,
        IDbContextFactory<TradingDbContext> contextFactory,
        ILogger<MarketSentimentService> logger)
    {
        _sentimentRepository = sentimentRepository;
        _instrumentRepository = instrumentRepository;
        _candleRepository    = candleRepository;
        _instrumentService   = instrumentService;
        _contextFactory = contextFactory;
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
            var utcNow   = istToday.AddDays(1);

            // Run independent data fetches concurrently.
            var indicesTask     = AnalyzeIndicesAsync(istToday, cancellationToken);
            var sectorsTask     = AnalyzeSectorsAsync(istToday, cancellationToken);
            var breadthTask     = CalculateMarketBreadthAsync(istToday, cancellationToken);
            var volatilityTask  = GetVolatilityIndexAsync(istToday, cancellationToken);

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
    DateTime istToday, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var indices = await context.Instruments
                .Where(i => i.InstrumentType == InstrumentType.INDEX && i.IsActive)
                .ToListAsync(cancellationToken);

            var fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(-IndexLookbackDays), Ist);

            var toDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(1), Ist);

            var ids = indices.Select(i => i.Id).ToList();

            var candles = await context.MarketCandles
                .Where(c => ids.Contains(c.InstrumentId) &&
                            c.Timestamp >= fromDateUtc &&
                            c.Timestamp <= toDateUtc)
                .OrderBy(c => c.Timestamp)
                .ToListAsync(cancellationToken);

            var map = candles.GroupBy(c => c.InstrumentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var results = new List<dynamic>();
            var locker = new object();

            Parallel.ForEach(indices, index =>
            {
                if (!map.TryGetValue(index.Id, out var c) || c.Count < 2) return;

                var closes = c.Select(x => x.Close).ToArray();
                var latest = closes[^1];
                var prev = closes[^2];

                var rsi = CalculateRsiFromSeries(closes, 14);
                var (macd, _) = CalculateMacdFromSeries(closes);

                var dma20 = CalculateSMA(closes, 20);
                var dma50 = CalculateSMA(closes, 50);

                lock (locker)
                {
                    results.Add(new
                    {
                        Performance = new IndexPerformance
                        {
                            IndexName = index.Name,
                            Symbol = index.Symbol,
                            CurrentValue = latest,
                            ChangePercent = prev != 0 ? (latest - prev) / prev * 100m : 0,
                            DayHigh = c[^1].High,
                            DayLow = c[^1].Low
                        },
                        Rsi = rsi,
                        MacdHist = macd,
                        PriceVs20DMA = dma20 != 0 ? (latest - dma20) / dma20 * 100 : 0,
                        PriceVs50DMA = dma50 != 0 ? (latest - dma50) / dma50 * 100 : 0,
                        GoldenCross = dma20 > dma50
                    });
                }
            });

            if (!results.Any())
                return new IndexAnalysisResult(new(), 50, 0, 0, 0, false);

            return new IndexAnalysisResult(
                results.Select(x => (IndexPerformance)x.Performance).ToList(),
                results.Average(x => (decimal)x.Rsi),
                results.Average(x => (decimal)x.MacdHist),
                results.Average(x => (decimal)x.PriceVs20DMA),
                results.Average(x => (decimal)x.PriceVs50DMA),
                results.All(x => (bool)x.GoldenCross)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing indices");
            return new IndexAnalysisResult(new(), 50, 0, 0, 0, false);
        }
    }
    /// <summary>
    /// Analyzes sector performance with 30-day lookback for 20DMA calculations.
    /// Per-sector stock candles are fetched concurrently.
    /// </summary>
    private async Task<SectorAnalysisResult> AnalyzeSectorsAsync(
    DateTime istToday, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        var sectors = await _instrumentService.GetSectorsAsync();
        var activeSectors = sectors.Where(s => s.IsActive).ToList();

        var stocks = await context.Instruments
            .Where(i => i.InstrumentType == InstrumentType.STOCK && i.IsActive)
            .ToListAsync(cancellationToken);

        var fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(
            istToday.AddDays(-SectorLookbackDays), Ist);

        var toDateUtc = TimeZoneInfo.ConvertTimeToUtc(
            istToday.AddDays(1), Ist);

        var ids = stocks.Select(s => s.Id).ToList();

        var candles = await context.MarketCandles
            .Where(c => ids.Contains(c.InstrumentId) &&
                        c.Timestamp >= fromDateUtc &&
                        c.Timestamp <= toDateUtc)
            .ToListAsync(cancellationToken);

        var map = candles.GroupBy(c => c.InstrumentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<dynamic>();
        var locker = new object();

        Parallel.ForEach(activeSectors, sector =>
        {
            var sectorStocks = stocks.Where(s => s.SectorId == sector.Id).ToList();

            int adv = 0, dec = 0;
            decimal total = 0;
            int count = 0;
            int above20 = 0;

            foreach (var stock in sectorStocks)
            {
                if (!map.TryGetValue(stock.Id, out var c) || c.Count < 2) continue;

                var closes = c.Select(x => x.Close).ToArray();
                var latest = closes[^1];
                var prev = closes[^2];

                var change = prev != 0 ? (latest - prev) / prev * 100 : 0;

                total += change;
                count++;

                if (change > 0) adv++;
                else if (change < 0) dec++;

                if (closes.Length >= 20 && latest > CalculateSMA(closes, 20))
                    above20++;
            }

            if (count == 0) return;

            lock (locker)
            {
                results.Add(new
                {
                    Performance = new SectorPerformance
                    {
                        SectorName = sector.Name,
                        ChangePercent = total / count,
                        StocksAdvancing = adv,
                        StocksDeclining = dec,
                        RelativeStrength = (decimal)adv / Math.Max(1, adv + dec)
                    },
                    Above20 = (decimal)above20 / count * 100
                });
            }
        });

        if (!results.Any())
            return new SectorAnalysisResult(new(), 50, 0);

        var performances = results.Select(x => (SectorPerformance)x.Performance).ToList();

        return new SectorAnalysisResult(
            performances,
            results.Average(x => (decimal)x.Above20),
            performances.Max(s => s.ChangePercent) - performances.Min(s => s.ChangePercent)
        );
    }

    /// <summary>
    /// Calculates rolling market breadth using 365-day lookback (widest window for 52-week H/L).
    /// Computes 20-day rolling A/D ratio, McClellan Oscillator, and 52-week new highs/lows.
    /// </summary>
    private async Task<BreadthAnalysisResult> CalculateMarketBreadthAsync(
        DateTime istToday, CancellationToken cancellationToken)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var activeStocks = await context.Instruments
                .Where(i => i.InstrumentType == InstrumentType.STOCK && i.IsActive)
                .OrderByDescending(s => s.MarketCap ?? 0)
                .Take(BreadthStockLimit)
                .ToListAsync(cancellationToken);

            var ids = activeStocks.Select(s => s.Id).ToList();
            var fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(-BreadthLookbackDays), Ist);

            var toDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(1), Ist);

            // 🔥 SINGLE DB HIT
            var candles = await context.MarketCandles
                .Where(c => ids.Contains(c.InstrumentId) &&
                            c.Timestamp >= fromDateUtc &&
                            c.Timestamp <= toDateUtc)
                .OrderBy(c => c.Timestamp)
                .ToListAsync(cancellationToken);

            var map = candles
                .GroupBy(c => c.InstrumentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            int todayAdvancing = 0, todayDeclining = 0, todayUnchanged = 0;
            int newHighs52W = 0, newLows52W = 0;

            var dailyAdDiffs = new Dictionary<DateTime, int>();
            var lockObj = new object();

            // 🚀 PARALLEL CPU
            Parallel.ForEach(activeStocks, stock =>
            {
                if (!map.TryGetValue(stock.Id, out var c) || c.Count < 2) return;

                var closes = c.Select(x => x.Close).ToArray();
                var todayClose = closes[^1];
                var prevClose = closes[^2];

                var localAdv = 0;
                var localDec = 0;
                var localUnc = 0;
                var localHigh = 0;
                var localLow = 0;

                // ── A/D
                var change = prevClose != 0
                    ? (todayClose - prevClose) / prevClose * 100m
                    : 0m;

                if (change > 0.01m) localAdv++;
                else if (change < -0.01m) localDec++;
                else localUnc++;

                // ── 52W
                decimal high52 = decimal.MinValue;
                decimal low52 = decimal.MaxValue;

                foreach (var x in c)
                {
                    if (x.High > high52) high52 = x.High;
                    if (x.Low < low52) low52 = x.Low;
                }

                if (todayClose >= high52 * 0.98m) localHigh++;
                if (todayClose <= low52 * 1.02m) localLow++;

                // ── McClellan input
                var localDiffs = new Dictionary<DateTime, int>();

                var recent = c.Count > 41 ? c.Skip(c.Count - 41).ToList() : c;

                for (int i = 1; i < recent.Count; i++)
                {
                    var prev = recent[i - 1].Close;
                    var cur = recent[i].Close;
                    if (prev == 0) continue;

                    var d = TimeZoneInfo.ConvertTime(recent[i].Timestamp, Ist).Date;
                    var ch = (cur - prev) / prev * 100m;

                    if (!localDiffs.ContainsKey(d)) localDiffs[d] = 0;

                    if (ch > 0.01m) localDiffs[d]++;
                    else if (ch < -0.01m) localDiffs[d]--;
                }

                // 🔒 MERGE
                lock (lockObj)
                {
                    todayAdvancing += localAdv;
                    todayDeclining += localDec;
                    todayUnchanged += localUnc;
                    newHighs52W += localHigh;
                    newLows52W += localLow;

                    foreach (var kv in localDiffs)
                    {
                        if (!dailyAdDiffs.ContainsKey(kv.Key))
                            dailyAdDiffs[kv.Key] = 0;

                        dailyAdDiffs[kv.Key] += kv.Value;
                    }
                }
            });

            decimal breadthRatio = todayDeclining > 0
                ? (decimal)todayAdvancing / todayDeclining
                : todayAdvancing > 0 ? 10m : 1m;

            var sorted = dailyAdDiffs
                .OrderBy(x => x.Key)
                .Select(x => (decimal)x.Value)
                .ToArray();

            var mclellan = 0m;
            if (sorted.Length >= 39)
            {
                var ema19 = EMA.CalculateSeries(sorted, 19);
                var ema39 = EMA.CalculateSeries(sorted, 39);
                mclellan = ema19[^1] - ema39[^1];
            }

            _logger.LogInformation(
                "Market breadth: A:{A} D:{D} U:{U} Ratio:{R:F2} NH:{NH} NL:{NL} McClellan:{MC:F2}",
                todayAdvancing, todayDeclining, todayUnchanged,
                breadthRatio, newHighs52W, newLows52W, mclellan);

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
        DateTime istToday, CancellationToken cancellationToken)
    {
        var defaultResult = new VolatilityResult(20m, 0m, false);

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var vix = await context.Instruments
                .FirstOrDefaultAsync(i =>
                    i.Symbol.Contains("VIX") &&
                    i.InstrumentType == InstrumentType.INDEX &&
                    i.IsActive,
                    cancellationToken);

            if (vix is null)
            {
                _logger.LogWarning("India VIX instrument not found");
                return defaultResult;
            }

            var fromDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(-VixLookbackDays), Ist);

            var toDateUtc = TimeZoneInfo.ConvertTimeToUtc(
                istToday.AddDays(1), Ist);

            // 🔥 SINGLE DB HIT
            var candles = await context.MarketCandles
                .Where(c => c.InstrumentId == vix.Id &&
                            c.Timestamp >= fromDateUtc &&
                            c.Timestamp <= toDateUtc)
                .OrderBy(c => c.Timestamp)
                .ToListAsync(cancellationToken);

            if (candles.Count == 0)
            {
                _logger.LogWarning("No VIX candles");
                return defaultResult;
            }

            var closes = candles.Select(c => c.Close).ToArray();
            var latest = closes[^1];

            var sma20 = CalculateSMA(closes, 20);
            var diff = latest - sma20;

            var rising = closes.Length >= 2 && closes[^1] > closes[^2];

            _logger.LogInformation(
                "India VIX: {VIX:F2} vs 20DMA:{DMA:F2} diff:{Diff:F2} Rising:{R}",
                latest, sma20, diff, rising);

            return new VolatilityResult(latest, diff, rising);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting VIX");
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