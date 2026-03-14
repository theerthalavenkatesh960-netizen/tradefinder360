using Npgsql;
using Quartz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Automated retention enforcement job for tiered market candle storage.
/// Retention policy:
/// - 1-minute candles: 3 months
/// - 15-minute candles: 9 months
/// - Daily candles: indefinite (no auto-deletion)
/// </summary>
[DisallowConcurrentExecution]
public class PartitionRetentionJob : IJob
{
    private readonly ILogger<PartitionRetentionJob> _logger;
    private readonly IConfiguration _configuration;

    public PartitionRetentionJob(
        ILogger<PartitionRetentionJob> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("=== Partition Retention Cleanup Job Started ===");
        _logger.LogInformation("Scheduled Fire Time: {ScheduledFireTime}", context.ScheduledFireTimeUtc);
        _logger.LogInformation("Retention Policy: 1m=3mo, 15m=9mo, 1d=indefinite");

        var connectionString = _configuration.GetConnectionString("Supabase");
        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogError("Database connection string is missing. Cannot proceed with retention cleanup.");
            throw new InvalidOperationException("Database connection string 'Supabase' not found.");
        }

        try
        {
            await ExecuteRetentionCleanupAsync(connectionString, context.CancellationToken);
            
            _logger.LogInformation("=== Partition Retention Cleanup Job Completed Successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Partition Retention Cleanup Job failed with exception");
            throw new JobExecutionException("Retention cleanup failed", ex, refireImmediately: false);
        }
    }

    /// <summary>
    /// Executes the retention cleanup stored procedure.
    /// Drops expired partitions based on retention policy.
    /// </summary>
    private async Task ExecuteRetentionCleanupAsync(string connectionString, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing retention cleanup procedure...");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT * FROM cleanup_market_candle_retention()",
            connection);

        // Set a reasonable timeout for partition drops (5 minutes)
        command.CommandTimeout = 300;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var droppedPartitions = new List<string>();
        var retainedPartitions = new List<string>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var action = reader.GetString(0);
            var partitionName = reader.GetString(1);
            var timeframeMinutes = reader.GetInt32(2);
            var partitionStart = reader.IsDBNull(3) ? (DateTime?)null : reader.GetDateTime(3);
            var status = reader.GetString(4);

            var timeframeLabel = timeframeMinutes switch
            {
                1 => "1m",
                15 => "15m",
                1440 => "1d",
                _ => $"{timeframeMinutes}m"
            };

            if (action == "DROPPED")
            {
                var logMessage = $"[{timeframeLabel}] {partitionName} (start: {partitionStart:yyyy-MM}) - {status}";
                droppedPartitions.Add(logMessage);
                _logger.LogWarning("🗑 DROPPED: {LogMessage}", logMessage);
            }
            else if (action == "RETAINED")
            {
                var logMessage = $"[{timeframeLabel}] {partitionName} - {status}";
                retainedPartitions.Add(logMessage);
                _logger.LogInformation("✓ RETAINED: {LogMessage}", logMessage);
            }
        }

        _logger.LogInformation(
            "Retention cleanup summary - Dropped: {Dropped}, Retained: {Retained}",
            droppedPartitions.Count,
            retainedPartitions.Count);

        if (droppedPartitions.Any())
        {
            _logger.LogWarning("Partitions dropped ({Count}):", droppedPartitions.Count);
            foreach (var partition in droppedPartitions)
            {
                _logger.LogWarning("  • {Partition}", partition);
            }
        }
        else
        {
            _logger.LogInformation("No partitions were dropped (all within retention period)");
        }
    }
}