using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox.Services;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class InstrumentSyncJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IUpstoxInstrumentService _upstoxInstrumentService;
    private readonly ILogger<InstrumentSyncJob> _logger;

    public InstrumentSyncJob(
        IInstrumentRepository instrumentRepository,
        IUpstoxInstrumentService upstoxInstrumentService,
        ILogger<InstrumentSyncJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _upstoxInstrumentService = upstoxInstrumentService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starting instrument sync job at {Time}", DateTime.UtcNow);

        try
        {
            var instruments = await _upstoxInstrumentService.FetchAllEquityInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Fetched {Count} instruments from Upstox", instruments.Count);

            if (!instruments.Any())
            {
                _logger.LogWarning("No instruments fetched from Upstox");
                return;
            }

            var saved = await _instrumentRepository.BulkUpsertAsync(instruments, context.CancellationToken);
            _logger.LogInformation("Instrument sync completed. Saved/Updated {Count} records", saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in instrument sync job");
            throw;
        }
    }
}
