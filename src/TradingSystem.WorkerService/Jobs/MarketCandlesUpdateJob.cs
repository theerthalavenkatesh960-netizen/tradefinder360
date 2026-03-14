using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Core.Utilities;
using TradingSystem.Data;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;
using System.Collections.Concurrent;

[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private const int MaxInstrumentParallelism = 12;
    private const int MaxApiRetries            = 3;
    private const int MinTradingDaysGap        = 3;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(1);

    // Single source of truth for IST timezone — used everywhere in this job.
    // All dates in DB are IST, Upstox API expects IST dates, so we never use
    // DateTime.UtcNow or DateTime.Now directly anywhere in this job.
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    // IST "today" — computed once per job execution via IstNow property
    private static DateTime IstToday =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist).Date;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UpstoxClient         _upstoxClient;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

    private static readonly IReadOnlyList<TimeframeConfig> TimeframeConfigs =
    [
        new(Unit: "days",    Interval: 1,  TimeframeMinutes: 1440, RetentionDays: 1825, BatchDays: 1825),
        new(Unit: "minutes", Interval: 15, TimeframeMinutes: 15,   RetentionDays: 274,  BatchDays: 31),
        new(Unit: "minutes", Interval: 1,  TimeframeMinutes: 1,    RetentionDays: 91,   BatchDays: 31),
    ];

    private readonly ConcurrentBag<JobError> _errors = new();

    public MarketCandlesUpdateJob(
        IServiceScopeFactory scopeFactory,
        UpstoxClient upstoxClient,
        ILogger<MarketCandlesUpdateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _upstoxClient = upstoxClient;
        _logger       = logger;
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
            if (_errors.Count > 0)
            {
                _logger.LogError(
                    "═══ JOB COMPLETED WITH {ErrorCount} ERROR(S) in {Elapsed:mm\\:ss} ═══",
                    _errors.Count, sw.Elapsed);

                foreach (var group in _errors.GroupBy(e => e.ErrorType).OrderByDescending(g => g.Count()))
                    _logger.LogError("  {Count}× {Type}: {Samples}",
                        group.Count(),
                        group.Key,
                        string.Join(", ", group.Take(5).Select(e => e.Context)));
            }
            else
            {
                _logger.LogInformation(
                    "═══ Job COMPLETED SUCCESSFULLY in {Elapsed:mm\\:ss} ═══", sw.Elapsed);
            }
        }
    }

    private async Task ExecuteInternal(CancellationToken ct)
    {
        TradingCalendar.ValidateHolidayData(_logger);

        List<TradingInstrument> instruments;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            instruments = (await repo.GetActiveInstrumentsAsync(ct)).ToList();
        }

        if (instruments.Count == 0)
        {
            _logger.LogWarning("No active instruments found");
            return;
        }

        // FIX: Use IST today as the reference point for ALL date calculations.
        // Yesterday in IST = safe ceiling (today's session may be incomplete).
        // Never use DateTime.UtcNow.Date here — that can be 1 day behind IST
        // between midnight IST (00:00) and UTC offset time (05:30 IST = 00:00 UTC).
        var toDate    = IstToday.AddDays(-1);
        var fromDates = TimeframeConfigs.ToDictionary(
            c => c,
            c => toDate.AddDays(-c.RetentionDays));

        _logger.LogInformation(
            "Instruments: {Count} | IST toDate: {To:yyyy-MM-dd} | Parallelism: {P}",
            instruments.Count, toDate, MaxInstrumentParallelism);

        foreach (var (cfg, from) in fromDates)
            _logger.LogInformation(
                "  {TF,5}m: {From:yyyy-MM-dd} → {To:yyyy-MM-dd} ({Days}d)",
                cfg.TimeframeMinutes, from, toDate, cfg.RetentionDays);

        var metrics  = new JobMetrics();
        var progress = 0;

        await Parallel.ForEachAsync(
            instruments,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxInstrumentParallelism,
                CancellationToken      = ct
            },
            async (instrument, innerCt) =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var candleRepo = scope.ServiceProvider
                    .GetRequiredService<IMarketCandleRepository>();

                try
                {
                    var result = await ProcessInstrumentAsync(
                        instrument, toDate, fromDates, candleRepo, innerCt);

                    metrics.Add(result);

                    var done = Interlocked.Increment(ref progress);
                    if (done % 100 == 0 || done == instruments.Count)
                        _logger.LogInformation(
                            "Progress: {Done}/{Total} | Errors: {Errors} | API: {API} | Saved: {Saved}",
                            done, instruments.Count, _errors.Count,
                            metrics.TotalApiCalls, metrics.TotalCandlesSaved);
                }
                catch (Exception ex)
                {
                    _errors.Add(new JobError
                    {
                        ErrorType  = ex.GetType().Name,
                        Context    = $"{instrument.Symbol} (all timeframes)",
                        Message    = ex.Message,
                        StackTrace = ex.StackTrace ?? ""
                    });
                    _logger.LogError(ex, "{Symbol} FAILED: {Msg}", instrument.Symbol, ex.Message);
                }
            });

        _logger.LogInformation(
            "FINAL: Processed={P} | Errors={E} | API={A} | Candles={C}",
            instruments.Count, _errors.Count, metrics.TotalApiCalls, metrics.TotalCandlesSaved);
    }

    private async Task<InstrumentResult> ProcessInstrumentAsync(
        TradingInstrument instrument,
        DateTime toDate,                                        // IST date, no time component
        IReadOnlyDictionary<TimeframeConfig, DateTime> fromDates,
        IMarketCandleRepository candleRepository,
        CancellationToken ct)
    {
        var result = new InstrumentResult { Symbol = instrument.Symbol };

        foreach (var config in TimeframeConfigs)
        {
            try
            {
                var (calls, candles) = await ProcessTimeframeAsync(
                    instrument, config, fromDates[config], toDate, candleRepository, ct);

                result.ApiCalls     += calls;
                result.CandlesSaved += candles;
            }
            catch (Exception ex)
            {
                _errors.Add(new JobError
                {
                    ErrorType  = ex.GetType().Name,
                    Context    = $"{instrument.Symbol} {config.TimeframeMinutes}m",
                    Message    = ex.Message,
                    StackTrace = ex.StackTrace ?? ""
                });
                _logger.LogError(ex, "{Symbol} {TF}m FAILED: {Msg}",
                    instrument.Symbol, config.TimeframeMinutes, ex.Message);
            }
        }

        return result;
    }

    private Task<(int ApiCalls, int CandlesSaved)> ProcessTimeframeAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,
        DateTime toDate,
        IMarketCandleRepository candleRepository,
        CancellationToken ct)
    {
        return config.Unit == "days"
            ? ProcessDailyAsync(instrument, config, fromDate, toDate, candleRepository, ct)
            : ProcessIntradayAsync(instrument, config, fromDate, toDate, candleRepository, ct);
    }

    // -------------------------------------------------------------------------
    // DAILY — single API call from (latestDate+1) to toDate.
    // latestDate comes back as an IST date from the repository.
    // toDate is already an IST date set at job start.
    // All comparisons are IST vs IST — no conversion needed.
    // -------------------------------------------------------------------------
    private async Task<(int ApiCalls, int CandlesSaved)> ProcessDailyAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,                  // IST date (toDate - RetentionDays)
        DateTime toDate,                    // IST date (IstToday - 1)
        IMarketCandleRepository candleRepository,
        CancellationToken ct)
    {
        // FIX: repository returns IST date (see GetLatestCandleDateAsync below).
        // Both latestDate and toDate are IST dates — comparison is safe.
        var latestDate = await candleRepository.GetLatestCandleDateAsync(
            instrument.Id, config.TimeframeMinutes, ct);

        DateTime fetchFrom;

        if (latestDate.HasValue)
        {
            // latestDate.Value is already an IST date with no time component
            if (latestDate.Value >= toDate)
                return (0, 0);

            fetchFrom = latestDate.Value.AddDays(1);
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
        await candleRepository.BulkUpsertAsync(marketCandles, ct);

        _logger.LogInformation("{Symbol} 1d — saved {Count} candles ({From:yyyy-MM-dd}→{To:yyyy-MM-dd})",
            instrument.Symbol, marketCandles.Count, fetchFrom, toDate);

        return (1, marketCandles.Count);
    }

    // -------------------------------------------------------------------------
    // INTRADAY — gap-based, monthly batches.
    // fromDate and toDate are IST dates.
    // GetMissingDataRangesAsync returns IST-based date ranges from the DB.
    // TradingCalendar works on DateOnly — no timezone involved.
    // -------------------------------------------------------------------------
    private async Task<(int ApiCalls, int CandlesSaved)> ProcessIntradayAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        DateTime fromDate,                  // IST date
        DateTime toDate,                    // IST date
        IMarketCandleRepository candleRepository,
        CancellationToken ct)
    {
        var genuineRanges = await GetGenuineIntradayMissingRangesAsync(
            instrument, config, fromDate, toDate, candleRepository, ct);

        if (genuineRanges.Count == 0)
            return (0, 0);

        int apiCalls    = 0;
        int candlesSaved = 0;

        foreach (var (rangeFrom, rangeTo) in genuineRanges)
        {
            foreach (var (batchFrom, batchTo) in GenerateMonthlyBatches(rangeFrom, rangeTo, config.BatchDays))
            {
                ct.ThrowIfCancellationRequested();

                var candles = await FetchWithRetryAsync(instrument, config, batchFrom, batchTo, ct);

                if (candles is null || candles.Count == 0)
                    continue;

                apiCalls++;
                var marketCandles = MapToMarketCandles(candles, instrument.Id, config.TimeframeMinutes);
                await candleRepository.BulkUpsertAsync(marketCandles, ct);
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
        DateTime fromDate,                  // IST date
        DateTime toDate,                    // IST date
        IMarketCandleRepository candleRepository,
        CancellationToken ct)
    {
        var rawRanges = (await candleRepository.GetMissingDataRangesAsync(
            instrument.Id, fromDate, toDate, config.TimeframeMinutes, ct)).ToList();

        if (rawRanges.Count == 0)
        {
            var hasAnyData = await candleRepository.HasAnyDataAsync(
                instrument.Id, config.TimeframeMinutes, ct);

            if (!hasAnyData)
            {
                _logger.LogDebug(
                    "{Symbol} {TF}m — empty table, queuing full window {From:yyyy-MM-dd}→{To:yyyy-MM-dd}",
                    instrument.Symbol, config.TimeframeMinutes, fromDate, toDate);
                return [(fromDate, toDate)];
            }

            return [];
        }

        var genuine = new List<(DateTime From, DateTime To)>();

        foreach (var r in rawRanges)
        {
            // Clamp — DB gap may extend past our safe toDate
            var clampedTo = r.ToDate > toDate ? toDate : r.ToDate;

            if (clampedTo < r.FromDate)
                continue;

            // TradingCalendar operates on dates only — no timezone concern here
            var tradingDays = TradingCalendar.CountTradingDays(r.FromDate, clampedTo);

            if (tradingDays < MinTradingDaysGap)
                continue;

            genuine.Add((r.FromDate, clampedTo));
        }

        return genuine;
    }

    // -------------------------------------------------------------------------
    // Splits a date range into maxDays-sized batches.
    // All inputs and outputs are IST dates (no time component).
    // -------------------------------------------------------------------------
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
        DateTime from,                      // IST date
        DateTime to,                        // IST date
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
                    var delay = RetryBaseDelay * Math.Pow(2, attempt - 1); // 1s → 2s → 4s
                    _logger.LogWarning(ex,
                        "{Symbol} {TF}m attempt {A}/{Max} failed, retrying in {D}s",
                        instrument.Symbol, config.TimeframeMinutes,
                        attempt, MaxApiRetries, delay.TotalSeconds);
                    await Task.Delay(delay, ct);
                }
            }
        }

        throw new InvalidOperationException(
            $"API failed after {MaxApiRetries} attempts: {from:yyyy-MM-dd}→{to:yyyy-MM-dd}",
            lastException);
    }

    // -------------------------------------------------------------------------
    // Records and helpers
    // -------------------------------------------------------------------------

    private record TimeframeConfig(
        string Unit,
        int    Interval,
        int    TimeframeMinutes,
        int    RetentionDays,
        int    BatchDays);

    private record JobError
    {
        public required string ErrorType  { get; init; }
        public required string Context    { get; init; }
        public required string Message    { get; init; }
        public required string StackTrace { get; init; }
    }

    private record InstrumentResult
    {
        public required string Symbol      { get; init; }
        public int             ApiCalls    { get; set; }
        public int             CandlesSaved { get; set; }
    }

    private sealed class JobMetrics
    {
        private int _apiCalls;
        private int _candlesSaved;

        public int TotalApiCalls     => _apiCalls;
        public int TotalCandlesSaved => _candlesSaved;

        public void Add(InstrumentResult result)
        {
            Interlocked.Add(ref _apiCalls,     result.ApiCalls);
            Interlocked.Add(ref _candlesSaved, result.CandlesSaved);
        }
    }
}


