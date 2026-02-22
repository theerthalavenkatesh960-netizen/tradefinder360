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

            var instrumentKeys = activeInstruments.Select(i => i.InstrumentKey).ToList();
            var instrumentMap = activeInstruments.ToDictionary(i => i.InstrumentKey, i => i.Id);
            const int batchSize = 500;

            _logger.LogInformation("Fetching current quotes for {Count} instruments in batches of {BatchSize}",
                instrumentKeys.Count, batchSize);

            var allQuotes = await _upstoxPriceService.FetchCurrentQuotesAsync(
                instrumentKeys,
                batchSize,
                context.CancellationToken);

            _logger.LogInformation("Received {Count} quotes from Upstox", allQuotes.Count);

            var pricesToUpsert = new List<Core.Models.InstrumentPrice>();

            foreach (var (instrumentKey, quote) in allQuotes)
            {
                if (!instrumentMap.TryGetValue(instrumentKey, out var instrumentId))
                {
                    _logger.LogWarning("Instrument not found for key: {InstrumentKey}", instrumentKey);
                    continue;
                }

                quote.InstrumentId = instrumentId;
                quote.Timeframe = "1D";
                quote.CreatedAt = DateTime.UtcNow;
                quote.UpdatedAt = DateTime.UtcNow;

                pricesToUpsert.Add(quote);
            }

            if (pricesToUpsert.Any())
            {
                try
                {
                    var totalSaved = await _priceRepository.BulkUpsertAsync(pricesToUpsert, context.CancellationToken);
                    _logger.LogInformation("Daily price update job completed. Total records saved: {Count}", totalSaved);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving prices to database");
                    throw;
                }
            }
            else
            {
                _logger.LogWarning("No prices to save");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in daily price update job");
            throw;
        }
    }
}
