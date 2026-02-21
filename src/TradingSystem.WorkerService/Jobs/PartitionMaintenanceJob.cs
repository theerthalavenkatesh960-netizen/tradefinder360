using Npgsql;
using Quartz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Scheduled job that maintains database partitions for the instrument_prices table.
/// Runs monthly to ensure partitions exist for upcoming months.
/// </summary>
[DisallowConcurrentExecution]
public class PartitionMaintenanceJob : IJob
{
    private readonly ILogger<PartitionMaintenanceJob> _logger;
    private readonly IConfiguration _configuration;

    public PartitionMaintenanceJob(
        ILogger<PartitionMaintenanceJob> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("=== Partition Maintenance Job Started ===");
        _logger.LogInformation("Scheduled Fire Time: {ScheduledFireTime}", context.ScheduledFireTimeUtc);

        var connectionString = _configuration.GetConnectionString("Supabase");
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogError("Database connection string is missing. Cannot proceed with partition maintenance.");
            throw new InvalidOperationException("Database connection string 'Supabase' not found.");
        }

        try
        {
            await CreateFuturePartitions(connectionString);
            await LogExistingPartitions(connectionString);

            _logger.LogInformation("=== Partition Maintenance Job Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Partition Maintenance Job failed with exception");
            throw new JobExecutionException("Partition maintenance failed", ex, refireImmediately: false);
        }
    }

    /// <summary>
    /// Creates partitions for the next N months to ensure data can be inserted without delays.
    /// </summary>
    private async Task CreateFuturePartitions(string connectionString)
    {
        const int monthsAhead = 2; // Create partitions 2 months in advance

        _logger.LogInformation("Creating partitions for the next {MonthsAhead} months...", monthsAhead);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT create_next_n_months_partitions(@monthsAhead)",
            connection);

        command.Parameters.AddWithValue("monthsAhead", monthsAhead);

        await using var reader = await command.ExecuteReaderAsync();

        var partitionsCreated = 0;
        var partitionsSkipped = 0;

        while (await reader.ReadAsync())
        {
            var result = reader.GetString(0);

            if (result.Contains("Created partition"))
            {
                partitionsCreated++;
                _logger.LogInformation("✓ {Result}", result);
            }
            else if (result.Contains("already exists"))
            {
                partitionsSkipped++;
                _logger.LogDebug("○ {Result}", result);
            }
            else
            {
                _logger.LogWarning("? Unexpected result: {Result}", result);
            }
        }

        _logger.LogInformation(
            "Partition creation summary - Created: {Created}, Skipped (already exist): {Skipped}",
            partitionsCreated,
            partitionsSkipped);
    }

    /// <summary>
    /// Logs all existing partitions for the instrument_prices table for visibility.
    /// </summary>
    private async Task LogExistingPartitions(string connectionString)
    {
        _logger.LogInformation("Fetching list of existing partitions...");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var query = @"
            SELECT
                c.relname AS partition_name,
                pg_get_expr(c.relpartbound, c.oid) AS partition_range
            FROM pg_class c
            JOIN pg_inherits i ON i.inhrelid = c.oid
            JOIN pg_class parent ON parent.oid = i.inhparent
            WHERE parent.relname = 'instrument_prices'
            AND c.relkind = 'r'
            ORDER BY c.relname;
        ";

        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var partitionCount = 0;
        var partitions = new List<string>();

        while (await reader.ReadAsync())
        {
            var partitionName = reader.GetString(0);
            var partitionRange = reader.GetString(1);
            partitions.Add($"{partitionName} → {partitionRange}");
            partitionCount++;
        }

        if (partitionCount > 0)
        {
            _logger.LogInformation("Existing partitions ({Count}):", partitionCount);
            foreach (var partition in partitions)
            {
                _logger.LogInformation("  • {Partition}", partition);
            }
        }
        else
        {
            _logger.LogWarning("No partitions found for instrument_prices table!");
        }
    }
}
