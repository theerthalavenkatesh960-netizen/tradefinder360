using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// One-time or on-demand job to backfill historical candle data for all instruments.
/// Fetches multi-year history across all timeframes using Upstox API v3.
/// </summary>
[DisallowConcurrentExecution]
public class HistoricalCandleBackfillJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<HistoricalCandleBackfillJob> _logger;

    // Backfill configuration: (unit, interval, timeframeMinutes, retentionYears, batchDays)
    private static readonly List<BackfillConfig> BackfillConfigs = new()
    {
        new("days", 1, 1440, 5, 365),       // Daily: 5 years, 1-year batches
        new("hours", 1, 60, 3, 90),         // Hourly: 3 years, 90-day batches
        new("minutes", 15, 15, 1, 30),      // 15min: 1 year, 30-day batches
        new("minutes", 1, 1, 0.25, 30)      // 1min: 3 months, 30-day batches
    };

    public HistoricalCandleBackfillJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        UpstoxClient upstoxClient,
        ILogger<HistoricalCandleBackfillJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("=== Historical Candle Backfill Job Started ===");
        _logger.LogInformation("Fetching: Daily (5y), Hourly (3y), 15min (1y), 1min (3mo)");

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments for backfill", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found");
                return;
            }

            // Process instruments in parallel with controlled concurrency
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = 15,
                CancellationToken = context.CancellationToken
            };

            await Parallel.ForEachAsync(activeInstruments, options, async (instrument, ct) =>
            {
                await BackfillInstrumentAsync(instrument, ct);
            });

            _logger.LogInformation("=== Historical Candle Backfill Job Completed ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in backfill job");
            throw;
        }
    }

    private async Task BackfillInstrumentAsync(TradingInstrument instrument, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting backfill for {InstrumentKey} ({Symbol})", 
            instrument.InstrumentKey, 
            instrument.Symbol);

        foreach (var config in BackfillConfigs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await BackfillTimeframeAsync(instrument, config, cancellationToken);
        }

        _logger.LogInformation("Completed backfill for {InstrumentKey}", instrument.InstrumentKey);
    }

    private async Task BackfillTimeframeAsync(
        TradingInstrument instrument,
        BackfillConfig config,
        CancellationToken cancellationToken)
    {
        var toDate = DateTime.UtcNow.Date;
        var fromDate = toDate.AddDays(-(int)(config.RetentionYears * 365));

        _logger.LogInformation(
            "Fetching {Timeframe}min candles for {Symbol}: {From} → {To}",
            config.TimeframeMinutes,
            instrument.Symbol,
            fromDate,
            toDate);

        // Generate date ranges based on API limits
        var ranges = UpstoxClient.GenerateDateRanges(fromDate, toDate, config.BatchDays);

        var totalCandles = 0;

        foreach (var (rangeFrom, rangeTo) in ranges)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var candles = await _upstoxClient.GetHistoricalCandlesV3Async(
                    instrument.InstrumentKey,
                    config.Unit,
                    config.Interval,
                    rangeFrom,
                    rangeTo);

                if (candles.Any())
                {
                    var marketCandles = candles.Select(c => new MarketCandle
                    {
                        InstrumentId = instrument.Id,
                        TimeframeMinutes = config.TimeframeMinutes,
                        Timestamp = c.Timestamp,
                        Open = c.Open,
                        High = c.High,
                        Low = c.Low,
                        Close = c.Close,
                        Volume = c.Volume,
                        CreatedAt = DateTimeOffset.UtcNow
                    }).ToList();

                    await _candleRepository.BulkUpsertAsync(marketCandles, cancellationToken);
                    totalCandles += candles.Count;

                    _logger.LogDebug(
                        "Saved {Count} {Timeframe}min candles for {Symbol} ({From} → {To})",
                        candles.Count,
                        config.TimeframeMinutes,
                        instrument.Symbol,
                        rangeFrom,
                        rangeTo);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error fetching {Timeframe}min candles for {Symbol} ({From} → {To})",
                    config.TimeframeMinutes,
                    instrument.Symbol,
                    rangeFrom,
                    rangeTo);
            }

            // Small delay between batches to avoid API throttling
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation(
            "Completed {Timeframe}min backfill for {Symbol}: {Total} candles",
            config.TimeframeMinutes,
            instrument.Symbol,
            totalCandles);
    }

    private record BackfillConfig(
        string Unit,
        int Interval,
        int TimeframeMinutes,
        double RetentionYears,
        int BatchDays);
}