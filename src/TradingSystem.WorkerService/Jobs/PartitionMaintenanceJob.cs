using Npgsql;
using Quartz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Scheduled job that maintains tiered database partitions for the market_candles table.
/// Creates resolution-based partitions (1m, 15m, 1d) for upcoming months.
/// Runs monthly to ensure partitions exist for all timeframes.
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
        _logger.LogInformation("=== TIERED Partition Maintenance Job Started ===");
        _logger.LogInformation("Scheduled Fire Time: {ScheduledFireTime}", context.ScheduledFireTimeUtc);
        _logger.LogInformation("Target: Create future partitions for 1m, 15m, and 1d timeframes");

        var connectionString = _configuration.GetConnectionString("Supabase");
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogError("Database connection string is missing. Cannot proceed with partition maintenance.");
            throw new InvalidOperationException("Database connection string 'Supabase' not found.");
        }

        try
        {
            await CreateFuturePartitionsAsync(connectionString, context.CancellationToken);
            await LogExistingPartitionsAsync(connectionString, context.CancellationToken);

            _logger.LogInformation("=== Tiered Partition Maintenance Job Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tiered Partition Maintenance Job failed with exception");
            throw new JobExecutionException("Partition maintenance failed", ex, refireImmediately: false);
        }
    }

    /// <summary>
    /// Creates tiered partitions (1m, 15m, 1d) for the next N months.
    /// This ensures data can be inserted for all timeframes without delays.
    /// </summary>
    private async Task CreateFuturePartitionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        const int monthsAhead = 3; // Create partitions 3 months in advance

        _logger.LogInformation("Creating TIERED partitions (1m, 15m, 1d) for the next {MonthsAhead} months...", monthsAhead);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT * FROM create_future_market_candle_partitions(@monthsAhead)",
            connection);

        command.Parameters.AddWithValue("monthsAhead", monthsAhead);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var partitionsCreated = 0;
        var partitionsSkipped = 0;

        while (await reader.ReadAsync(cancellationToken))
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
    /// Logs all existing tiered partitions for visibility and monitoring.
    /// Groups by timeframe (1m, 15m, 1d) for clarity.
    /// </summary>
    private async Task LogExistingPartitionsAsync(string connectionString, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching list of existing tiered partitions...");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var query = @"
            SELECT
                c.relname AS partition_name,
                pg_get_expr(c.relpartbound, c.oid) AS partition_range,
                pg_size_pretty(pg_total_relation_size(c.oid)) AS partition_size
            FROM pg_class c
            JOIN pg_inherits i ON i.inhrelid = c.oid
            JOIN pg_class parent ON parent.oid = i.inhparent
            WHERE parent.relname = 'market_candles'
            AND c.relkind = 'r'
            ORDER BY c.relname;
        ";

        await using var command = new NpgsqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var partitions1m = new List<string>();
        var partitions15m = new List<string>();
        var partitions1d = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var partitionName = reader.GetString(0);
            var partitionRange = reader.GetString(1);
            var partitionSize = reader.GetString(2);
            var partitionInfo = $"{partitionName} → {partitionRange} ({partitionSize})";

            if (partitionName.Contains("_1m_"))
                partitions1m.Add(partitionInfo);
            else if (partitionName.Contains("_15m_"))
                partitions15m.Add(partitionInfo);
            else if (partitionName.Contains("_1d_"))
                partitions1d.Add(partitionInfo);
        }

        var totalPartitions = partitions1m.Count + partitions15m.Count + partitions1d.Count;

        if (totalPartitions > 0)
        {
            _logger.LogInformation("=== EXISTING TIERED PARTITIONS ({Total} total) ===", totalPartitions);

            if (partitions1m.Any())
            {
                _logger.LogInformation("--- 1-Minute Candles ({Count} partitions) ---", partitions1m.Count);
                foreach (var partition in partitions1m)
                {
                    _logger.LogInformation("  • {Partition}", partition);
                }
            }

            if (partitions15m.Any())
            {
                _logger.LogInformation("--- 15-Minute Candles ({Count} partitions) ---", partitions15m.Count);
                foreach (var partition in partitions15m)
                {
                    _logger.LogInformation("  • {Partition}", partition);
                }
            }

            if (partitions1d.Any())
            {
                _logger.LogInformation("--- Daily Candles ({Count} partitions) ---", partitions1d.Count);
                foreach (var partition in partitions1d)
                {
                    _logger.LogInformation("  • {Partition}", partition);
                }
            }
        }
        else
        {
            _logger.LogWarning("⚠ No partitions found for market_candles table!");
        }
    }
}
