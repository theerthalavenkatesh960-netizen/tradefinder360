using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Core.Utilities;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;
using System.Collections.Concurrent;

[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private const int MaxParallelism = 16;  // Increased for 6K stocks
    private const int MaxApiRetries = 3;
    private const int MinTradingDaysGap = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);

    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

    private static readonly IReadOnlyList<TimeframeConfig> TimeframeConfigs =
    [
        new(Unit: "days",    Interval: 1,  TimeframeMinutes: 1440, RetentionDays: 1825, BatchDays: 1825),
        new(Unit: "minutes", Interval: 15, TimeframeMinutes: 15,   RetentionDays: 274,  BatchDays: 31),
        new(Unit: "minutes", Interval: 1,  TimeframeMinutes: 1,    RetentionDays: 91,   BatchDays: 31),
    ];

    // Centralized error tracking
    private readonly ConcurrentBag<JobError> _errors = new();

    public MarketCandlesUpdateJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        UpstoxClient upstoxClient,
        ILogger<MarketCandlesUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("═══ Market candles update job STARTED ═══");

        try
        {
            await ExecuteInternal(ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job CANCELLED after {Elapsed:mm\\:ss}", sw.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL JOB FAILURE after {Elapsed:mm\\:ss}", sw.Elapsed);
            throw;
        }
        finally
        {
            // Always report errors collected during execution
            if (_errors.Count > 0)
            {
                _logger.LogError(
                    "═══ JOB COMPLETED WITH {ErrorCount} ERROR(S) in {Elapsed:mm\\:ss} ═══",
                    _errors.Count, sw.Elapsed);

                // Group errors by type for easier debugging
                var errorGroups = _errors
                    .GroupBy(e => e.ErrorType)
                    .OrderByDescending(g => g.Count());

                foreach (var group in errorGroups)
                {
                    _logger.LogError("  {Count}× {Type}: {Samples}",
                        group.Count(),
                        group.Key,
                        string.Join(", ", group.Take(5).Select(e => e.Context)));
                }
            }
            else
            {
                _logger.LogInformation("═══ Job COMPLETED SUCCESSFULLY in {Elapsed:mm\\:ss} ═══", sw.Elapsed);
            }
        }
    }

    private async Task ExecuteInternal(CancellationToken ct)
    {
        // Validate calendar data
        TradingCalendar.ValidateHolidayData(_logger);

        // Load instruments
        var instruments = (await _instrumentRepository.GetActiveInstrumentsAsync(ct)).ToList();
        if (instruments.Count == 0)
        {
            _logger.LogWarning("No active instruments found");
            return;
        }

        // Calculate date ranges - FIX: Use UTC midnight to avoid timezone issues
        var toDate = DateTime.UtcNow.Date;  // Today UTC midnight
        var fromDates = TimeframeConfigs.ToDictionary(
            c => c,
            c => toDate.AddDays(-c.RetentionDays));

        _logger.LogInformation(
            "Instruments: {Count} | Date: {To:yyyy-MM-dd} | Parallelism: {P}",
            instruments.Count, toDate, MaxParallelism);

        foreach (var (cfg, from) in fromDates)
            _logger.LogInformation(
                "  {TF,5}m: {From:yyyy-MM-dd} → {To:yyyy-MM-dd} ({Days}d)",
                cfg.TimeframeMinutes, from, toDate, cfg.RetentionDays);

        // Metrics
        var metrics = new JobMetrics();
        var progress = 0;

        // Process all instruments in parallel with all timeframes
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(MaxParallelism);

        foreach (var instrument in instruments)
        {
            await semaphore.WaitAsync(ct);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var result = await ProcessInstrumentAsync(instrument, toDate, fromDates, ct);
                    metrics.Add(result);

                    var done = Interlocked.Increment(ref progress);
                    if (done % 100 == 0 || done == instruments.Count)
                        _logger.LogInformation(
                            "Progress: {Done}/{Total} | Errors: {Errors} | API: {API} | Saved: {Saved}",
                            done, instruments.Count, _errors.Count,
                            metrics.TotalApiCalls, metrics.TotalCandlesSaved);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "FINAL: Processed={P} | Errors={E} | API={A} | Candles={C}",
            instruments.Count, _errors.Count, metrics.TotalApiCalls, metrics.TotalCandlesSaved);
    }

    private async Task<InstrumentResult> ProcessInstrumentAsync(
        TradingInstrument instrument,
        DateTime toDate,
        IReadOnlyDictionary<TimeframeConfig, DateTime> fromDates,
        CancellationToken ct)
    {
        var result = new InstrumentResult { Symbol = instrument.Symbol };

        // Process all timeframes in parallel per instrument
        var timeframeTasks = TimeframeConfigs.Select(async config =>
        {
            try
            {
                var (calls, candles) = await ProcessTimeframeAsync(
                    instrument, config, fromDates[config], toDate, ct);

                return new TimeframeResult
                {
                    Success = true,
                    ApiCalls = calls,
                    CandlesSaved = candles
                };
            }
            catch (Exception ex)
            {
                _errors.Add(new JobError
                {
                    ErrorType = ex.GetType().Name,
                    Context = $"{instrument.Symbol} {config.TimeframeMinutes}m",
                    Message = ex.Message,
                    StackTrace = ex.StackTrace ?? ""
                });

                _logger.LogError(ex, "{Symbol} {TF}m FAILED: {Msg}",
                    instrument.Symbol, config.TimeframeMinutes, ex.Message);

                return new TimeframeResult { Success = false };
            }
        }).ToList();

        var timeframeResults = await Task.WhenAll(timeframeTasks);

        foreach (var tfResult in timeframeResults)
        {
            if (tfResult.Success)
            {
                result.ApiCalls += tfResult.ApiCalls;
                result.CandlesSaved += tfResult.CandlesSaved;
            }
        }

        return result;
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
        // FIX: Get latest date and ensure UTC comparison
        var latestDate = await _candleRepository.GetLatestCandleDateAsync(
            instrument.Id, config.TimeframeMinutes, ct);

        DateTime fetchFrom;

        if (latestDate.HasValue)
        {
            // FIX: Ensure we're comparing dates in UTC
            var latestUtc = DateTime.SpecifyKind(latestDate.Value, DateTimeKind.Utc).Date;

            if (latestUtc >= toDate.Date)
            {
                _logger.LogDebug("{Symbol} 1d — up to date (latest={Date:yyyy-MM-dd})",
                    instrument.Symbol, latestUtc);
                return (0, 0);
            }

            fetchFrom = latestUtc.AddDays(1);
        }
        else
        {
            fetchFrom = fromDate;
        }

        if (fetchFrom > toDate)
            return (0, 0);

        _logger.LogDebug("{Symbol} 1d — fetching {From:yyyy-MM-dd} → {To:yyyy-MM-dd}",
            instrument.Symbol, fetchFrom, toDate);

        var candles = await FetchWithRetryAsync(instrument, config, fetchFrom, toDate, ct);

        if (candles is null || candles.Count == 0)
            return (1, 0);

        var marketCandles = MapToMarketCandles(candles, instrument.Id, config.TimeframeMinutes);
        await _candleRepository.BulkUpsertAsync(marketCandles, ct);

        _logger.LogInformation("{Symbol} 1d — saved {Count} candles",
            instrument.Symbol, marketCandles.Count);

        return (1, marketCandles.Count);
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessIntradayAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken ct)
    {
        var genuineRanges = await GetGenuineIntradayMissingRangesAsync(
            instrument, config, fromDate, toDate, ct);

        if (genuineRanges.Count == 0)
            return (0, 0);

        _logger.LogDebug("{Symbol} {TF}m — {Count} gap(s): {Ranges}",
            instrument.Symbol, config.TimeframeMinutes, genuineRanges.Count,
            string.Join(", ", genuineRanges.Select(r => $"{r.From:MM/dd}→{r.To:MM/dd}")));

        int apiCalls = 0;
        int candlesSaved = 0;

        foreach (var (rangeFrom, rangeTo) in genuineRanges)
        {
            var batches = GenerateMonthlyBatches(rangeFrom, rangeTo, config.BatchDays);

            foreach (var (batchFrom, batchTo) in batches)
            {
                ct.ThrowIfCancellationRequested();

                var candles = await FetchWithRetryAsync(instrument, config, batchFrom, batchTo, ct);

                if (candles is null || candles.Count == 0)
                    continue;

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

        if (rawRanges.Count == 0)
        {
            var hasAnyData = await _candleRepository.HasAnyDataAsync(
                instrument.Id, config.TimeframeMinutes, ct);

            if (!hasAnyData)
                return [(fromDate, toDate)];

            return [];
        }

        var genuine = new List<(DateTime From, DateTime To)>();

        foreach (var r in rawRanges)
        {
            var clampedTo = r.ToDate > toDate ? toDate : r.ToDate;

            if (clampedTo < r.FromDate)
                continue;

            var tradingDays = TradingCalendar.CountTradingDays(r.FromDate, clampedTo);

            if (tradingDays < MinTradingDaysGap)
                continue;

            genuine.Add((r.FromDate, clampedTo));
        }

        return genuine;
    }

    private static List<(DateTime From, DateTime To)> GenerateMonthlyBatches(
        DateTime from, DateTime to, int maxDays)
    {
        var batches = new List<(DateTime, DateTime)>();
        var cursor = from;
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
            InstrumentId = instrumentId,
            TimeframeMinutes = timeframeMinutes,
            Timestamp = c.Timestamp,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToList();
    }

    private async Task<List<Candle>?> FetchWithRetryAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxApiRetries; attempt++)
        {
            try
            {
                var result = await _upstoxClient.GetHistoricalCandlesV3Async(
                    instrument.InstrumentKey,
                    config.Unit,
                    config.Interval,
                    from,
                    to);

                return result?.ToList();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt < MaxApiRetries)
                {
                    var delay = RetryBaseDelay * Math.Pow(2, attempt - 1);
                    _logger.LogDebug("{Symbol} {TF}m — retry {A}/{Max} after {D}s: {Msg}",
                        instrument.Symbol, config.TimeframeMinutes,
                        attempt, MaxApiRetries, delay.TotalSeconds, ex.Message);
                    await Task.Delay(delay, ct);
                }
            }
        }

        // All retries exhausted
        if (lastException != null)
            throw new InvalidOperationException(
                $"API failed after {MaxApiRetries} attempts: {from:yyyy-MM-dd}→{to:yyyy-MM-dd}",
                lastException);

        return null;
    }

    private record TimeframeConfig(
        string Unit,
        int Interval,
        int TimeframeMinutes,
        int RetentionDays,
        int BatchDays);

    private record JobError
    {
        public required string ErrorType { get; init; }
        public required string Context { get; init; }
        public required string Message { get; init; }
        public required string StackTrace { get; init; }
    }

    private record TimeframeResult
    {
        public bool Success { get; init; }
        public int ApiCalls { get; init; }
        public int CandlesSaved { get; init; }
    }

    private record InstrumentResult
    {
        public required string Symbol { get; init; }
        public int ApiCalls { get; set; }
        public int CandlesSaved { get; set; }
    }

    private class JobMetrics
    {
        private int _apiCalls;
        private int _candlesSaved;

        public int TotalApiCalls => _apiCalls;
        public int TotalCandlesSaved => _candlesSaved;

        public void Add(InstrumentResult result)
        {
            Interlocked.Add(ref _apiCalls, result.ApiCalls);
            Interlocked.Add(ref _candlesSaved, result.CandlesSaved);
        }
    }
}