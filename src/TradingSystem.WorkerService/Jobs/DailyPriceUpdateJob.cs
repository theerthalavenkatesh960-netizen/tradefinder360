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
            var now = DateTimeOffset.UtcNow;

            foreach (var (instrumentKey, quote) in allQuotes)
            {
                if (!instrumentMap.TryGetValue(instrumentKey, out var instrumentId))
                {
                    _logger.LogWarning("Instrument not found for key: {InstrumentKey}", instrumentKey);
                    continue;
                }

                // Validate timestamp
                if (quote.Timestamp == default || quote.Timestamp.Year < 2000)
                {
                    _logger.LogWarning(
                        "Invalid timestamp for instrument {InstrumentKey}: {Timestamp}. Skipping this quote.", 
                        instrumentKey, quote.Timestamp);
                    continue;
                }

                // Ensure timestamp is in UTC
                quote.Timestamp = quote.Timestamp.ToUniversalTime();
                quote.InstrumentId = instrumentId;
                quote.Timeframe = "1D";
                quote.CreatedAt = now;
                quote.UpdatedAt = now;

                pricesToUpsert.Add(quote);

                _logger.LogDebug(
                    "Prepared quote for {InstrumentKey}: Timestamp={Timestamp}, Close={Close}", 
                    instrumentKey, quote.Timestamp, quote.Close);
            }

            if (pricesToUpsert.Any())
            {
                try
                {
                    _logger.LogInformation("Upserting {Count} price records to database", pricesToUpsert.Count);
                    
                    var totalSaved = await _priceRepository.BulkUpsertAsync(pricesToUpsert, context.CancellationToken);
                    
                    _logger.LogInformation(
                        "Daily price update job completed. Total records saved: {Count}", 
                        totalSaved);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving prices to database");
                    throw;
                }
            }
            else
            {
                _logger.LogWarning("No valid prices to save after filtering");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in daily price update job");
            throw;
        }
    }
}
