using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using TradingSystem.WorkerService.DataSeeders;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class CsvDataSeederJob : IJob
{
    private readonly CsvSeedService _csvSeedService;
    private readonly SeederConfig _config;
    private readonly ILogger<CsvDataSeederJob> _logger;

    public CsvDataSeederJob(
        CsvSeedService csvSeedService,
        IOptions<SeederConfig> config,
        ILogger<CsvDataSeederJob> logger)
    {
        _csvSeedService = csvSeedService;
        _config = config.Value;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        if (!_config.EnableCsvSeeding)
        {
            _logger.LogInformation("CSV seeding is disabled in configuration");
            return;
        }

        _logger.LogInformation("Starting CSV data seeding job at {Time}", DateTime.UtcNow);

        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");

            var sectorsPath = Path.Combine(dataPath, "sectors.csv");
            var stocksPath = Path.Combine(dataPath, "stocks.csv");

            var sectorCount = await _csvSeedService.SeedSectorsFromCsvAsync(sectorsPath, context.CancellationToken);
            _logger.LogInformation("Seeded {Count} sectors", sectorCount);

            var instrumentCount = await _csvSeedService.SeedInstrumentsFromCsvAsync(stocksPath, context.CancellationToken);
            _logger.LogInformation("Seeded {Count} instruments", instrumentCount);

            _logger.LogInformation("CSV data seeding completed successfully. Total: {Sectors} sectors, {Instruments} instruments",
                sectorCount, instrumentCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in CSV data seeding job");
            throw;
        }
    }
}

public class SeederConfig
{
    public bool EnableCsvSeeding { get; set; }
}
