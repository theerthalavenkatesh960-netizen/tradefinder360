using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class IndicatorSnapshotsUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IIndicatorService _indicatorService;
    private readonly ILogger<IndicatorSnapshotsUpdateJob> _logger;

    public IndicatorSnapshotsUpdateJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        IIndicatorService indicatorService,
        ILogger<IndicatorSnapshotsUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _indicatorService = indicatorService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starting indicator snapshots update job at {Time}", DateTime.UtcNow);

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments to process", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found");
                return;
            }

            var timeframeMinutes = 15; // You can make this configurable
            var totalProcessed = 0;

            foreach (var instrument in activeInstruments)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Job cancelled");
                    break;
                }

                try
                {
                    // Get the latest indicator snapshot to avoid recalculating
                    var latestSnapshot = await _indicatorService.GetLatestAsync(
                        instrument.Id, 
                        timeframeMinutes);

                    var fromDate = latestSnapshot?.Timestamp.DateTime.AddMinutes(timeframeMinutes) 
                        ?? DateTime.UtcNow.AddDays(-7);
                    var toDate = DateTime.UtcNow;

                    // Fetch candles for this instrument
                    var marketCandles = await _candleRepository.GetByInstrumentIdAsync(
                        instrument.Id,
                        timeframeMinutes,
                        fromDate,
                        toDate,
                        context.CancellationToken);

                    if (!marketCandles.Any())
                    {
                        _logger.LogDebug("No new candles found for {InstrumentKey}", instrument.InstrumentKey);
                        continue;
                    }

                    // Convert to Candle objects
                    var candles = marketCandles.Select(mc => mc.ToCandle()).OrderBy(c => c.Timestamp).ToList();

                    // Need minimum candles for indicator calculation
                    if (candles.Count < 50)
                    {
                        _logger.LogDebug("Insufficient candles ({Count}) for {InstrumentKey}", 
                            candles.Count, instrument.InstrumentKey);
                        continue;
                    }

                    // Initialize indicator engine with standard parameters
                    var indicatorEngine = new IndicatorEngine(
                        emaFastPeriod: 20,
                        emaSlowPeriod: 50,
                        rsiPeriod: 14,
                        macdFast: 12,
                        macdSlow: 26,
                        macdSignal: 9,
                        adxPeriod: 14,
                        atrPeriod: 14,
                        bollingerPeriod: 20,
                        bollingerStdDev: 2.0m
                    );

                    var savedCount = 0;

                    // Calculate indicators for each candle
                    foreach (var candle in candles)
                    {
                        var indicators = indicatorEngine.Calculate(candle);

                        // Save indicator snapshot
                        await _indicatorService.SaveAsync(
                            instrument.Id,
                            timeframeMinutes,
                            indicators);

                        savedCount++;
                    }

                    totalProcessed += savedCount;
                    _logger.LogDebug("Processed {Count} indicators for {InstrumentKey}", 
                        savedCount, instrument.InstrumentKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing indicators for instrument: {InstrumentKey}", 
                        instrument.InstrumentKey);
                }
            }

            _logger.LogInformation("Indicator snapshots update job completed. Total processed: {Count}", totalProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in indicator snapshots update job");
            throw;
        }
    }
}