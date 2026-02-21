using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox.Services;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class DailyPriceUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IInstrumentPriceRepository _priceRepository;
    private readonly IUpstoxPriceService _upstoxPriceService;
    private readonly ILogger<DailyPriceUpdateJob> _logger;

    public DailyPriceUpdateJob(
        IInstrumentRepository instrumentRepository,
        IInstrumentPriceRepository priceRepository,
        IUpstoxPriceService upstoxPriceService,
        ILogger<DailyPriceUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _priceRepository = priceRepository;
        _upstoxPriceService = upstoxPriceService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starting daily price update job at {Time}", DateTime.UtcNow);

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Found {Count} active instruments to update", activeInstruments.Count);

            if (!activeInstruments.Any())
            {
                _logger.LogWarning("No active instruments found to update prices");
                return;
            }

            var toDate = DateTime.UtcNow.Date;
            var fromDate = toDate.AddDays(-30);

            var instrumentKeys = activeInstruments.Select(i => i.InstrumentKey).ToList();
            var batchSize = 50;

            _logger.LogInformation("Fetching prices from {FromDate} to {ToDate}", fromDate, toDate);

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

                foreach (var price in prices)
                {
                    price.InstrumentId = instrumentId;
                }

                try
                {
                    var saved = await _priceRepository.BulkUpsertAsync(prices, context.CancellationToken);
                    totalSaved += saved;
                    _logger.LogDebug("Saved {Count} prices for instrument {InstrumentKey}", saved, instrumentKey);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving prices for instrument: {InstrumentKey}", instrumentKey);
                }
            }

            _logger.LogInformation("Daily price update job completed. Total records saved: {Count}", totalSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in daily price update job");
            throw;
        }
    }
}
