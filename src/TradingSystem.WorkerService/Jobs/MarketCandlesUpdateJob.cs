using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;

/// <summary>
/// Scheduled job to fetch and store market candles for multiple timeframes using Upstox API v3.
/// Efficiently checks database for missing data before making API calls.
/// 
/// Timeframes:
/// - 1d (1440m): 5 years retention
/// - 15m: 1 year retention
/// - 1m: 3 months retention
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
            var totalApiCalls = 0;
            var totalCandlesSaved = 0;

            await Parallel.ForEachAsync(activeInstruments, options, async (instrument, ct) =>
            {
                var (calls, candles) = await ProcessInstrumentAsync(instrument, ct);
                
                Interlocked.Increment(ref totalProcessed);
                Interlocked.Add(ref totalApiCalls, calls);
                Interlocked.Add(ref totalCandlesSaved, candles);
                
                if (totalProcessed % 100 == 0)
                {
                    _logger.LogInformation(
                        "Progress: {Processed}/{Total} instruments | API calls: {ApiCalls} | Candles saved: {Candles}",
                        totalProcessed,
                        activeInstruments.Count,
                        totalApiCalls,
                        totalCandlesSaved);
                }
            });

            _logger.LogInformation(
                "=== Market Candles Update Job Completed ===\n" +
                "Instruments: {Instruments} | API Calls: {ApiCalls} | Candles Saved: {Candles}",
                totalProcessed,
                totalApiCalls,
                totalCandlesSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in market candles update job");
            throw;
        }
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessInstrumentAsync(
        TradingInstrument instrument,
        CancellationToken cancellationToken)
    {
        var totalApiCalls = 0;
        var totalCandles = 0;

        foreach (var config in TimeframeConfigs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var (calls, candles) = await ProcessTimeframeAsync(instrument, config, cancellationToken);
            totalApiCalls += calls;
            totalCandles += candles;
        }

        return (totalApiCalls, totalCandles);
    }

    private async Task<(int ApiCalls, int CandlesSaved)> ProcessTimeframeAsync(
        TradingInstrument instrument,
        TimeframeConfig config,
        CancellationToken cancellationToken)
    {
        var toDate = DateTime.UtcNow.Date;
        var fromDate = toDate.AddDays(-(int)(config.RetentionYears * 365));

        // Step 1: Check database for missing data ranges
        var missingRanges = await _candleRepository.GetMissingDataRangesAsync(
            instrument.Id,
            fromDate,
            toDate,
            config.TimeframeMinutes,
            cancellationToken);

        if (!missingRanges.Any())
        {
            _logger.LogDebug(
                "{Symbol} - {Timeframe}min: All data present, skipping API calls",
                instrument.Symbol,
                config.TimeframeMinutes);
            return (0, 0);
        }

        _logger.LogInformation(
            "{Symbol} - {Timeframe}min: {MissingCount} missing range(s) detected",
            instrument.Symbol,
            config.TimeframeMinutes,
            missingRanges.Count);

        var totalApiCalls = 0;
        var totalCandlesSaved = 0;

        // Step 2: Fetch only missing data ranges
        foreach (var missingRange in missingRanges)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Step 3: Split large missing ranges into API-compatible batches
            var batches = UpstoxClient.GenerateDateRanges(
                missingRange.FromDate,
                missingRange.ToDate,
                config.BatchDays);

            _logger.LogDebug(
                "{Symbol} - {Timeframe}min: Fetching missing range {From:yyyy-MM-dd} → {To:yyyy-MM-dd} ({BatchCount} batch(es))",
                instrument.Symbol,
                config.TimeframeMinutes,
                missingRange.FromDate,
                missingRange.ToDate,
                batches.Count);

            foreach (var (batchFrom, batchTo) in batches)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Step 4: Make API call only for missing batch
                    var candles = await _upstoxClient.GetHistoricalCandlesV3Async(
                        instrument.InstrumentKey,
                        config.Unit,
                        config.Interval,
                        batchFrom,
                        batchTo);

                    totalApiCalls++;

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

                        // Step 5: Bulk upsert with ON CONFLICT DO NOTHING
                        await _candleRepository.BulkUpsertAsync(marketCandles, cancellationToken);
                        totalCandlesSaved += candles.Count;

                        _logger.LogDebug(
                            "{Symbol} - {Timeframe}min: Saved {Count} candles ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                            instrument.Symbol,
                            config.TimeframeMinutes,
                            candles.Count,
                            batchFrom,
                            batchTo);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "{Symbol} - {Timeframe}min: No data returned from API ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                            instrument.Symbol,
                            config.TimeframeMinutes,
                            batchFrom,
                            batchTo);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{Symbol} - {Timeframe}min: Error fetching batch ({From:yyyy-MM-dd} → {To:yyyy-MM-dd})",
                        instrument.Symbol,
                        config.TimeframeMinutes,
                        batchFrom,
                        batchTo);
                }

                // Small delay between API calls to respect rate limits
                if (batches.Count > 1)
                {
                    await Task.Delay(150, cancellationToken);
                }
            }
        }

        if (totalCandlesSaved > 0)
        {
            _logger.LogInformation(
                "{Symbol} - {Timeframe}min: {ApiCalls} API call(s), {Candles} candles saved",
                instrument.Symbol,
                config.TimeframeMinutes,
                totalApiCalls,
                totalCandlesSaved);
        }

        return (totalApiCalls, totalCandlesSaved);
    }

    private record TimeframeConfig(
        string Unit,
        int Interval,
        int TimeframeMinutes,
        double RetentionYears,
        int BatchDays);
}