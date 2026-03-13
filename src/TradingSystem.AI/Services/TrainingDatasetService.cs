using Microsoft.Extensions.Logging;
using TradingSystem.AI.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Service for building ML training datasets from feature store
/// </summary>
public class TrainingDatasetService
{
    private readonly IFeatureStoreRepository _featureStore;
    private readonly ICandleService _candleService;
    private readonly ILogger<TrainingDatasetService> _logger;

    public TrainingDatasetService(
        IFeatureStoreRepository featureStore,
        ICandleService candleService,
        ILogger<TrainingDatasetService> logger)
    {
        _featureStore = featureStore;
        _candleService = candleService;
        _logger = logger;
    }

    /// <summary>
    /// Build training dataset for a single instrument
    /// </summary>
    public async Task<List<TrainingDataPoint>> BuildDatasetAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int forwardReturnDays = 5,
        float successThreshold = 2.0f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building training dataset for instrument {Id} from {Start} to {End}",
            instrumentId, startDate, endDate);

        var dataset = await _featureStore.BuildTrainingDatasetAsync(
            instrumentId,
            startDate,
            endDate,
            forwardReturnDays,
            cancellationToken);

        // Apply success threshold
        foreach (var point in dataset)
        {
            point.TradeSuccessLabel = point.TargetReturn5D > successThreshold;
        }

        _logger.LogInformation("Built dataset with {Count} samples, {Success} successful trades",
            dataset.Count,
            dataset.Count(d => d.TradeSuccessLabel));

        return dataset;
    }

    /// <summary>
    /// Build training dataset for multiple instruments
    /// </summary>
    public async Task<List<TrainingDataPoint>> BuildMultiInstrumentDatasetAsync(
        List<int> instrumentIds,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int forwardReturnDays = 5,
        float successThreshold = 2.0f,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building training dataset for {Count} instruments", instrumentIds.Count);

        var allData = new List<TrainingDataPoint>();

        foreach (var instrumentId in instrumentIds)
        {
            try
            {
                var data = await BuildDatasetAsync(
                    instrumentId,
                    startDate,
                    endDate,
                    forwardReturnDays,
                    successThreshold,
                    cancellationToken);

                allData.AddRange(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error building dataset for instrument {Id}", instrumentId);
            }
        }

        _logger.LogInformation("Built combined dataset with {Count} total samples", allData.Count);

        return allData;
    }

    /// <summary>
    /// Export dataset to CSV for external ML tools
    /// </summary>
    public async Task ExportToCsvAsync(
        List<TrainingDataPoint> dataset,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exporting {Count} samples to {Path}", dataset.Count, outputPath);

        var lines = new List<string>();

        // Header
        if (dataset.Any())
        {
            var featureNames = dataset.First().Features.ToDictionary().Keys;
            var header = string.Join(",", new[] { "symbol", "timestamp" }
                .Concat(featureNames)
                .Concat(new[] { "target_return_5d", "target_return_10d", "trade_success" }));
            lines.Add(header);
        }

        // Data rows
        foreach (var point in dataset)
        {
            var features = point.Features.ToDictionary();
            var row = string.Join(",", new object[]
            {
                point.Symbol,
                point.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")
            }
            .Concat(features.Values.Cast<object>())
            .Concat(new object[]
            {
                point.TargetReturn5D,
                point.TargetReturn10D,
                point.TradeSuccessLabel ? 1 : 0
            }));
            lines.Add(row);
        }

        await File.WriteAllLinesAsync(outputPath, lines, cancellationToken);

        _logger.LogInformation("Exported dataset to {Path}", outputPath);
    }
}