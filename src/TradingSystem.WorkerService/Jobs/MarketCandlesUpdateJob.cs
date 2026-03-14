using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Scheduled job to fetch and store market candles for multiple timeframes using Upstox API v3.
/// Supports tiered partition architecture with automatic routing.
/// 
/// Timeframes:
/// - 1d (1440m): 5 years retention, single API call
/// - 15m: 1 year retention, 30-day batches
/// - 1m: 3 months retention, 30-day batches
/// </summary>
[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

    // Timeframe configurations using Upstox API v3 format
    private static readonly List<TimeframeConfig> TimeframeConfigs = new()
    {
        new("days", 1, 1440, 5, 1825),      // Daily: 5 years, single API call
        new("minutes", 15, 15, 1, 30),      // 15min: 1 year, 30-day batches
        new("minutes", 1, 1, 0.25, 30)      // 1min: 3 months, 30-day batches
    };

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
        _logger.LogInformation("=== Market Candles Update Job Started (API v3) ===");
        _logger.LogInformation("Timeframes: 1d (5y), 15m (1y), 1m (3mo)");

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments to update", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found to update candles");
                return;
            }

            // Process instruments in parallel with controlled concurrency
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = 15,
                CancellationToken = context.CancellationToken
            };

            var totalProcessed = 0;

            await Parallel.ForEachAsync(activeInstruments, options, async (instrument, ct) =>
            {
                await ProcessInstrumentAsync(instrument, ct);
                Interlocked.Increment(ref totalProcessed);

                if (totalProcessed % 100 == 0)
                {
                    _logger.LogInformation("Progress: {Processed}/{Total} instruments", totalProcessed, activeInstruments.Count);
                }
            });

            _logger.LogInformation("=== Market Candles Update Job Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in market candles update job");
            throw;
        }
    }

    private async Task ProcessInstrumentAsync(TradingInstrument instrument, CancellationToken cancellationToken)
    {
        foreach (var config in TimeframeConfigs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessTimeframeAsync(instrument, config, cancellationToken);
        }
    }

    private async Task ProcessTimeframeAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        CancellationToken cancellationToken)
    {
        var toDate = DateTime.UtcNow.Date;
        var fromDate = toDate.AddDays(-(int)(config.RetentionYears * 365));

        // Generate date ranges based on API limits
        var ranges = UpstoxClient.GenerateDateRanges(fromDate, toDate, config.BatchDays);

        _logger.LogDebug(
            "{Symbol} - {Timeframe}min: Fetching {RangeCount} batch(es) from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            instrument.Symbol,
            config.TimeframeMinutes,
            ranges.Count,
            fromDate,
            toDate);

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
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error fetching {Timeframe}min candles for {Symbol} ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                    config.TimeframeMinutes,
                    instrument.Symbol,
                    rangeFrom,
                    rangeTo);
            }

            // Small delay between batches
            if (ranges.Count > 1)
            {
                await Task.Delay(150, cancellationToken);
            }
        }

        if (totalCandles > 0)
        {
            _logger.LogDebug(
                "{Symbol} - {Timeframe}min: {Total} candles saved",
                instrument.Symbol,
                config.TimeframeMinutes,
                totalCandles);
        }
    }

    private record TimeframeConfig(
        string Unit,
        int Interval,
        int TimeframeMinutes,
        double RetentionYears,
        int BatchDays);
}