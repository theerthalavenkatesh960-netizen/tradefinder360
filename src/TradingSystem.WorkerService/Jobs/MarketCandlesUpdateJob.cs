using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox.Services;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class MarketCandlesUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IUpstoxPriceService _upstoxPriceService;
    private readonly ILogger<MarketCandlesUpdateJob> _logger;

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
        _logger.LogInformation("Starting market candles update job at {Time}", DateTime.UtcNow);

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments to update", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found to update candles");
                return;
            }

            var toDate = DateTime.UtcNow.Date;
            var fromDate = toDate.AddDays(-30);

            var instrumentKeys = activeInstruments.Select(i => i.InstrumentKey).ToList();
            var batchSize = 50;
            var timeframeMinutes = 1;

            _logger.LogInformation("Fetching candles from {FromDate} to {ToDate} with {Timeframe}min interval", 
                fromDate, toDate, timeframeMinutes);

            var bulkPrices = await _upstoxPriceService.FetchBulkHistoricalPricesAsync(
                instrumentKeys,
                "1minute",
                fromDate,
                toDate,
                batchSize,
                context.CancellationToken);

            var instrumentMap = activeInstruments.ToDictionary(i => i.InstrumentKey, i => i.Id);
            var totalSaved = 0;

            foreach (var (instrumentKey, prices) in bulkPrices)
            {
                if (!instrumentMap.TryGetValue(instrumentKey, out var instrumentId))
                {
                    _logger.LogWarning("Instrument not found for key: {InstrumentKey}", instrumentKey);
                    continue;
                }

                if (!prices.Any())
                {
                    _logger.LogDebug("No prices found for instrument {InstrumentKey}", instrumentKey);
                    continue;
                }

                // Convert InstrumentPrice to MarketCandle
                var candles = prices.Select(p => new MarketCandle
                {
                    InstrumentId = instrumentId,
                    TimeframeMinutes = timeframeMinutes,
                    Timestamp = p.Timestamp,
                    Open = p.Open,
                    High = p.High,
                    Low = p.Low,
                    Close = p.Close,
                    Volume = p.Volume
                }).ToList();

                try
                {
                    var saved = await _candleRepository.BulkUpsertAsync(candles, context.CancellationToken);
                    totalSaved += saved;
                    _logger.LogDebug("Saved {Count} candles for instrument {InstrumentKey}", saved, instrumentKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving candles for instrument: {InstrumentKey}", instrumentKey);
                }
            }

            _logger.LogInformation("Market candles update job completed. Total records saved: {Count}", totalSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in market candles update job");
            throw;
        }
    }
}