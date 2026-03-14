using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox.Services;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Job to fetch and store market candles for multiple timeframes (1m, 15m, 1d).
/// Supports tiered partition architecture with automatic routing.
/// </summary>
[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IUpstoxPriceService _upstoxPriceService;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

    // Timeframe configurations (interval name, timeframe minutes)
    private static readonly Dictionary<string, int> TimeframeConfigs = new()
    {
        { "1minute", 1 },
        { "15minute", 15 },
        { "1day", 1440 }
    };

    public MarketCandlesUpdateJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        IUpstoxPriceService upstoxPriceService,
        ILogger<MarketCandlesUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _upstoxPriceService = upstoxPriceService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("=== Starting TIERED Market Candles Update Job ===");
        _logger.LogInformation("Timeframes: 1m, 15m, 1d");

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments to update", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found to update candles");
                return;
            }

            var instrumentKeys = activeInstruments.Select(i => i.InstrumentKey).ToList();
            var instrumentMap = activeInstruments.ToDictionary(i => i.InstrumentKey, i => i.Id);
            
            var toDate = DateTime.UtcNow.Date;
            
            // Process each timeframe
            foreach (var (interval, timeframeMinutes) in TimeframeConfigs)
            {
                var fromDate = CalculateFromDate(timeframeMinutes, toDate);
                
                _logger.LogInformation(
                    "Processing {Timeframe}min candles ({Interval}) from {FromDate} to {ToDate}",
                    timeframeMinutes,
                    interval,
                    fromDate,
                    toDate);

                await ProcessTimeframeAsync(
                    instrumentKeys,
                    instrumentMap,
                    interval,
                    timeframeMinutes,
                    fromDate,
                    toDate,
                    context.CancellationToken);
            }

            _logger.LogInformation("=== Market Candles Update Job Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in market candles update job");
            throw;
        }
    }

    /// <summary>
    /// Calculate the appropriate from_date based on retention policy.
    /// </summary>
    private DateTime CalculateFromDate(int timeframeMinutes, DateTime toDate)
    {
        return timeframeMinutes switch
        {
            1 => toDate.AddDays(-90),      // 1m: 3 months
            15 => toDate.AddDays(-270),    // 15m: 9 months
            1440 => toDate.AddDays(-1460), // 1d: 4 years
            _ => toDate.AddDays(-30)       // Default: 30 days
        };
    }

    /// <summary>
    /// Process a specific timeframe for all instruments.
    /// </summary>
    private async Task ProcessTimeframeAsync(
        List<string> instrumentKeys,
        Dictionary<string, int> instrumentMap,
        string interval,
        int timeframeMinutes,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken)
    {
        const int batchSize = 50;

        _logger.LogInformation(
            "Fetching {Timeframe}min candles with batch size {BatchSize}",
            timeframeMinutes,
            batchSize);

        var bulkCandles = await _upstoxPriceService.FetchBulkHistoricalPricesAsync(
            instrumentKeys,
            interval,
            fromDate,
            toDate,
            batchSize,
            cancellationToken);

        var totalSaved = 0;

        foreach (var (instrumentKey, candles) in bulkCandles)
        {
            if (!instrumentMap.TryGetValue(instrumentKey, out var instrumentId))
            {
                _logger.LogWarning("Instrument not found for key: {InstrumentKey}", instrumentKey);
                continue;
            }

            if (!candles.Any())
            {
                _logger.LogDebug("No candles found for instrument {InstrumentKey}", instrumentKey);
                continue;
            }

            // Convert to MarketCandle with correct timeframe
            var marketCandles = candles.Select(c => new MarketCandle
            {
                InstrumentId = instrumentId,
                TimeframeMinutes = timeframeMinutes, // Explicit timeframe for partition routing
                Timestamp = c.Timestamp.ToUniversalTime(),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                CreatedAt = DateTimeOffset.UtcNow
            }).ToList();

            try
            {
                var saved = await _candleRepository.BulkUpsertAsync(marketCandles, cancellationToken);
                totalSaved += saved;

                _logger.LogDebug(
                    "Saved {Count} {Timeframe}min candles for instrument {InstrumentKey}",
                    saved,
                    timeframeMinutes,
                    instrumentKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error saving {Timeframe}min candles for instrument {InstrumentKey}",
                    timeframeMinutes,
                    instrumentKey);
            }
        }

        _logger.LogInformation(
            "Completed {Timeframe}min candles processing. Total saved: {TotalSaved}",
            timeframeMinutes,
            totalSaved);
    }
}