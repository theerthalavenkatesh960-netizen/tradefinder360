using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Core.Utilities;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;

[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private const int MaxParallelism    = 8;
    private const int MaxApiRetries     = 3;
    private const int MinTradingDaysGap = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);

    private readonly IInstrumentRepository   _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly UpstoxClient            _upstoxClient;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

    private static readonly IReadOnlyList<TimeframeConfig> TimeframeConfigs =
    [
        new(Unit: "days",    Interval: 1,  TimeframeMinutes: 1440, RetentionDays: 1825, BatchDays: 1825),
        new(Unit: "minutes", Interval: 15, TimeframeMinutes: 15,   RetentionDays: 274,  BatchDays: 31),
        new(Unit: "minutes", Interval: 1,  TimeframeMinutes: 1,    RetentionDays: 91,   BatchDays: 31),
    ];

    public MarketCandlesUpdateJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        UpstoxClient upstoxClient,
        ILogger<MarketCandlesUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository     = candleRepository;
        _upstoxClient         = upstoxClient;
        _logger               = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;

        _logger.LogInformation("═══ Market candles update job STARTED ═══");

        try
        {
            TradingCalendar.ValidateHolidayData(_logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TradingCalendar validation failed — continuing anyway");
        }

        List<TradingInstrument> instruments;
        try
        {
            instruments = (await _instrumentRepository.GetActiveInstrumentsAsync(ct)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FATAL — failed to load instruments, aborting job");
            return;
        }

        if (instruments.Count == 0)
        {
            _logger.LogWarning("No active instruments found — aborting");
            return;
        }

        var toDate    = DateTime.UtcNow.Date.AddDays(-1);
        var fromDates = TimeframeConfigs
            .ToDictionary(c => c, c => toDate.AddDays(-c.RetentionDays));

        _logger.LogInformation(
            "Instruments: {Count} | Date window: {From:d} → {To:d} | Parallelism: {P}",
            instruments.Count,
            fromDates.Values.Min(),
            toDate,
            MaxParallelism);

        // Log each timeframe window so you can verify the ranges are correct
        foreach (var (cfg, from) in fromDates)
            _logger.LogInformation(
                "  Timeframe {TF,5}m → {From:d} to {To:d} ({Days} days)",
                cfg.TimeframeMinutes, from, toDate, cfg.RetentionDays);

        int totalApiCalls    = 0;
        int totalCandlesSaved = 0;
        int processed        = 0;
        int failed           = 0;

        await Parallel.ForEachAsync(instruments,
            new ParallelOptions { MaxDegreeOfParallelism = MaxParallelism, CancellationToken = ct },
            async (instrument, innerCt) =>
            {
                // Each instrument is wrapped individually — one failure never
                // silently kills the others or the whole job
                try
                {
                    var (calls, candles) = await ProcessInstrumentAsync(
                        instrument, toDate, fromDates, innerCt);

                    Interlocked.Add(ref totalApiCalls,     calls);
                    Interlocked.Add(ref totalCandlesSaved, candles);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("{Symbol} — cancelled", instrument.Symbol);
                    throw; // propagate so ForEachAsync stops cleanly
                }
                catch (Exception ex)
                {
                    // Log full exception with symbol — previously this was swallowed
                    _logger.LogError(ex,
                        "INSTRUMENT FAILED — {Symbol} (Id={Id})",
                        instrument.Symbol, instrument.Id);
                    Interlocked.Increment(ref failed);
                    // Don't rethrow — continue processing other instruments
                }

                var done = Interlocked.Increment(ref processed);
                if (done % 50 == 0 || done == instruments.Count)
                    _logger.LogInformation(
                        "Progress {Done}/{Total} | failed={Failed} | API={Calls} | Candles={Candles}",
                        done, instruments.Count, failed, totalApiCalls, totalCandlesSaved);
            });

        _logger.LogInformation(
            "═══ Job COMPLETED — processed={I} failed={F} | API calls={A} | Candles={C} ═══",
            processed, failed, totalApiCalls, totalCandlesSaved);
    }

    // -------------------------------------------------------------------------
    // Per-instrument: run all 3 timeframes and catch each independently
    // Previously Task.WhenAll would swallow exceptions from failed tasks
    // -------------------------------------------------------------------------
    private async Task<(int ApiCalls, int CandlesSaved)> ProcessInstrumentAsync(
        TradingInstrument instrument,
        DateTime toDate,
        IReadOnlyDictionary<TimeframeConfig, DateTime> fromDates,
        CancellationToken ct)
    {
        int totalCalls   = 0;
        int totalCandles = 0;

        // Run timeframes sequentially per instrument intentionally —
        // Task.WhenAll across 8 parallel instruments × 3 timeframes = 24 concurrent
        // DB + API calls which was overwhelming connections and hiding exceptions.
        // Sequential per-instrument keeps total concurrency at MaxParallelism (8).
        foreach (var config in TimeframeConfigs)
        {
            try
            {
                _logger.LogDebug("→ {Symbol} {TF}m starting",
                    instrument.Symbol, config.TimeframeMinutes);

                var (calls, candles) = await ProcessTimeframeAsync(
                    instrument, config, fromDates[config], toDate, ct);

                totalCalls   += calls;
                totalCandles += candles;

                _logger.LogDebug("✓ {Symbol} {TF}m done — {Calls} API call(s), {Candles} candle(s)",
                    instrument.Symbol, config.TimeframeMinutes, calls, candles);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // Log which exact timeframe failed — previously this was invisible
                _logger.LogError(ex,
                    "TIMEFRAME FAILED — {Symbol} {TF}m",
                    instrument.Symbol, config.TimeframeMinutes);
                // Continue to next timeframe rather than aborting the instrument
            }
        }

        return (totalCalls, totalCandles);
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessTimeframeAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        return config.Unit == "days"
            ? await ProcessDailyAsync(instrument, config, fromDate, toDate, ct)
            : await ProcessIntradayAsync(instrument, config, fromDate, toDate, ct);
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessDailyAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        DateTime fetchFrom;

        try
        {
            var latestDate = await _candleRepository.GetLatestCandleDateAsync(
                instrument.Id, config.TimeframeMinutes, ct);

            if (latestDate.HasValue && latestDate.Value.Date >= toDate.Date)
            {
                _logger.LogDebug("{Symbol} 1d — up to date (latest={Date:d})",
                    instrument.Symbol, latestDate.Value);
                return (0, 0);
            }

            fetchFrom = latestDate.HasValue
                ? latestDate.Value.Date.AddDays(1)
                : fromDate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "{Symbol} 1d — failed to get latest candle date, falling back to full window",
                instrument.Symbol);
            fetchFrom = fromDate;
        }

        if (fetchFrom > toDate)
        {
            _logger.LogDebug("{Symbol} 1d — fetchFrom {F:d} > toDate {T:d}, nothing to fetch",
                instrument.Symbol, fetchFrom, toDate);
            return (0, 0);
        }

        _logger.LogDebug("{Symbol} 1d — fetching {From:d} → {To:d}",
            instrument.Symbol, fetchFrom, toDate);

        var candles = await FetchWithRetryAsync(instrument, config, fetchFrom, toDate, ct);

        if (candles is null || candles.Count == 0)
        {
            _logger.LogDebug("{Symbol} 1d — empty response ({From:d}→{To:d})",
                instrument.Symbol, fetchFrom, toDate);
            return (0, 0);
        }

        _logger.LogDebug("{Symbol} 1d — got {Count} candles from API, upserting...",
            instrument.Symbol, candles.Count);

        var marketCandles = MapToMarketCandles(candles, instrument.Id, config.TimeframeMinutes);
        await _candleRepository.BulkUpsertAsync(marketCandles, ct);

        _logger.LogInformation("{Symbol} 1d — saved {Count} candles ({From:d}→{To:d})",
            instrument.Symbol, marketCandles.Count, fetchFrom, toDate);

        return (1, marketCandles.Count);
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessIntradayAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        List<(DateTime From, DateTime To)> genuineRanges;

        try
        {
            genuineRanges = await GetGenuineIntradayMissingRangesAsync(
                instrument, config, fromDate, toDate, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "{Symbol} {TF}m — failed to compute missing ranges, skipping",
                instrument.Symbol, config.TimeframeMinutes);
            return (0, 0);
        }

        if (genuineRanges.Count == 0)
            return (0, 0);

        _logger.LogDebug("{Symbol} {TF}m — {Count} range(s) to fetch: {Ranges}",
            instrument.Symbol,
            config.TimeframeMinutes,
            genuineRanges.Count,
            // Log exact ranges so you can see what's being queued
            string.Join(", ", genuineRanges.Select(r => $"{r.From:d}→{r.To:d}")));

        int apiCalls    = 0;
        int candlesSaved = 0;

        foreach (var (rangeFrom, rangeTo) in genuineRanges)
        {
            var batches = GenerateMonthlyBatches(rangeFrom, rangeTo, config.BatchDays);

            _logger.LogDebug("{Symbol} {TF}m — range {From:d}→{To:d} split into {N} batch(es)",
                instrument.Symbol, config.TimeframeMinutes, rangeFrom, rangeTo, batches.Count);

            foreach (var (batchFrom, batchTo) in batches)
            {
                ct.ThrowIfCancellationRequested();

                _logger.LogDebug("{Symbol} {TF}m — batch {From:d}→{To:d}",
                    instrument.Symbol, config.TimeframeMinutes, batchFrom, batchTo);

                var candles = await FetchWithRetryAsync(instrument, config, batchFrom, batchTo, ct);

                if (candles is null || candles.Count == 0)
                {
                    _logger.LogDebug("{Symbol} {TF}m — empty response {From:d}→{To:d}",
                        instrument.Symbol, config.TimeframeMinutes, batchFrom, batchTo);
                    continue;
                }

                _logger.LogDebug("{Symbol} {TF}m — got {Count} candles, upserting...",
                    instrument.Symbol, config.TimeframeMinutes, candles.Count);

                apiCalls++;
                var marketCandles = MapToMarketCandles(candles, instrument.Id, config.TimeframeMinutes);

                await _candleRepository.BulkUpsertAsync(marketCandles, ct);
                candlesSaved += marketCandles.Count;
            }
        }

        if (candlesSaved > 0)
            _logger.LogInformation("{Symbol} {TF}m — saved {Count} candles",
                instrument.Symbol, config.TimeframeMinutes, candlesSaved);

        return (apiCalls, candlesSaved);
    }

    private async Task<List<(DateTime From, DateTime To)>> GetGenuineIntradayMissingRangesAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        var rawRanges = (await _candleRepository.GetMissingDataRangesAsync(
            instrument.Id, fromDate, toDate, config.TimeframeMinutes, ct)).ToList();

        _logger.LogDebug("{Symbol} {TF}m — DB returned {Count} raw gap(s)",
            instrument.Symbol, config.TimeframeMinutes, rawRanges.Count);

        if (rawRanges.Count == 0)
        {
            var hasAnyData = await _candleRepository.HasAnyDataAsync(
                instrument.Id, config.TimeframeMinutes, ct);

            if (!hasAnyData)
            {
                _logger.LogInformation(
                    "{Symbol} {TF}m — no existing data at all, queuing full window {From:d}→{To:d}",
                    instrument.Symbol, config.TimeframeMinutes, fromDate, toDate);
                return [(fromDate, toDate)];
            }

            _logger.LogDebug("{Symbol} {TF}m — has data, no gaps found",
                instrument.Symbol, config.TimeframeMinutes);
            return [];
        }

        var genuine = new List<(DateTime From, DateTime To)>();

        foreach (var r in rawRanges)
        {
            var clampedTo = r.ToDate > toDate ? toDate : r.ToDate;

            if (clampedTo < r.FromDate)
            {
                _logger.LogDebug("{Symbol} {TF}m — dropping inverted range {From:d}→{To:d}",
                    instrument.Symbol, config.TimeframeMinutes, r.FromDate, clampedTo);
                continue;
            }

            var tradingDays = TradingCalendar.CountTradingDays(r.FromDate, clampedTo);

            _logger.LogDebug(
                "{Symbol} {TF}m — gap {From:d}→{To:d} = {TD} trading day(s)",
                instrument.Symbol, config.TimeframeMinutes, r.FromDate, clampedTo, tradingDays);

            if (tradingDays < MinTradingDaysGap)
                continue;

            genuine.Add((r.FromDate, clampedTo));
        }

        _logger.LogDebug(
            "{Symbol} {TF}m — {Genuine}/{Total} gap(s) pass threshold ({Min} trading days)",
            instrument.Symbol, config.TimeframeMinutes,
            genuine.Count, rawRanges.Count, MinTradingDaysGap);

        return genuine;
    }

    private static List<(DateTime From, DateTime To)> GenerateMonthlyBatches(
        DateTime from, DateTime to, int maxDays)
    {
        var batches = new List<(DateTime, DateTime)>();
        var cursor  = from;
        while (cursor <= to)
        {
            var batchEnd = cursor.AddDays(maxDays - 1);
            if (batchEnd > to) batchEnd = to;
            batches.Add((cursor, batchEnd));
            cursor = batchEnd.AddDays(1);
        }
        return batches;
    }

    private static List<MarketCandle> MapToMarketCandles(
        List<Candle> candles, int instrumentId, int timeframeMinutes)
    {
        return candles.Select(c => new MarketCandle
        {
            InstrumentId     = instrumentId,
            TimeframeMinutes = timeframeMinutes,
            Timestamp        = c.Timestamp,
            Open             = c.Open,
            High             = c.High,
            Low              = c.Low,
            Close            = c.Close,
            Volume           = c.Volume,
            CreatedAt        = DateTimeOffset.UtcNow
        }).ToList();
    }

    private async Task<List<Candle>?> FetchWithRetryAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        for (int attempt = 1; attempt <= MaxApiRetries; attempt++)
        {
            try
            {
                _logger.LogDebug(
                    "{Symbol} {TF}m — API call attempt {A}/{Max}: {From:d}→{To:d}",
                    instrument.Symbol, config.TimeframeMinutes, attempt, MaxApiRetries, from, to);

                var result = await _upstoxClient.GetHistoricalCandlesV3Async(
                    instrument.InstrumentKey,
                    config.Unit,
                    config.Interval,
                    from,
                    to);

                var list = result?.ToList();

                _logger.LogDebug(
                    "{Symbol} {TF}m — API returned {Count} candles",
                    instrument.Symbol, config.TimeframeMinutes, list?.Count ?? 0);

                return list;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (attempt < MaxApiRetries)
            {
                var delay = RetryBaseDelay * Math.Pow(2, attempt - 1);
                _logger.LogWarning(ex,
                    "{Symbol} {TF}m — attempt {A}/{Max} failed ({Msg}), retrying in {D}s",
                    instrument.Symbol, config.TimeframeMinutes,
                    attempt, MaxApiRetries, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{Symbol} {TF}m — all {Max} attempts failed for {From:d}→{To:d} | {Type}: {Msg}",
                    instrument.Symbol, config.TimeframeMinutes,
                    MaxApiRetries, from, to,
                    ex.GetType().Name, ex.Message);
                return null;
            }
        }
        return null;
    }

    private record TimeframeConfig(
        string Unit,
        int    Interval,
        int    TimeframeMinutes,
        int    RetentionDays,
        int    BatchDays);
}