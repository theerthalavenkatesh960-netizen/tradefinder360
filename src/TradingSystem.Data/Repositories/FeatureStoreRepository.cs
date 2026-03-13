using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.AI.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class FeatureStoreRepository : IFeatureStoreRepository
{
    private readonly TradingDbContext _context;
    private readonly ILogger<FeatureStoreRepository> _logger;
    private const string FEATURE_VERSION = "1.0";

    public FeatureStoreRepository(TradingDbContext context, ILogger<FeatureStoreRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task StoreFeatureVectorAsync(QuantFeatureVector vector, CancellationToken cancellationToken = default)
    {
        var entity = new FeatureStore
        {
            InstrumentId = vector.InstrumentId,
            Symbol = vector.Symbol,
            Timestamp = vector.Timestamp,
            FeaturesJson = JsonSerializer.Serialize(vector.ToDictionary()),
            FeatureCount = vector.GetFeatureCount(),
            FeatureVersion = FEATURE_VERSION
        };

        _context.Set<FeatureStore>().Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Stored feature vector for {Symbol} with {Count} features",
            vector.Symbol, vector.GetFeatureCount());
    }

    public async Task StoreBatchAsync(List<QuantFeatureVector> vectors, CancellationToken cancellationToken = default)
    {
        var entities = vectors.Select(v => new FeatureStore
        {
            InstrumentId = v.InstrumentId,
            Symbol = v.Symbol,
            Timestamp = v.Timestamp,
            FeaturesJson = JsonSerializer.Serialize(v.ToDictionary()),
            FeatureCount = v.GetFeatureCount(),
            FeatureVersion = FEATURE_VERSION
        }).ToList();

        _context.Set<FeatureStore>().AddRange(entities);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Stored {Count} feature vectors in batch", vectors.Count);
    }

    public async Task<QuantFeatureVector?> GetLatestAsync(int instrumentId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.Set<FeatureStore>()
            .Where(f => f.InstrumentId == instrumentId)
            .OrderByDescending(f => f.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        return entity == null ? null : DeserializeFeatureVector(entity);
    }

    public async Task<List<QuantFeatureVector>> GetHistoricalAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.Set<FeatureStore>()
            .Where(f => f.InstrumentId == instrumentId
                     && f.Timestamp >= startDate
                     && f.Timestamp <= endDate)
            .OrderBy(f => f.Timestamp)
            .ToListAsync(cancellationToken);

        return entities.Select(DeserializeFeatureVector).ToList();
    }

    public async Task<List<QuantFeatureVector>> GetSnapshotAsync(
        List<int> instrumentIds,
        DateTimeOffset timestamp,
        TimeSpan tolerance,
        CancellationToken cancellationToken = default)
    {
        var startTime = timestamp - tolerance;
        var endTime = timestamp + tolerance;

        var entities = await _context.Set<FeatureStore>()
            .Where(f => instrumentIds.Contains(f.InstrumentId)
                     && f.Timestamp >= startTime
                     && f.Timestamp <= endTime)
            .GroupBy(f => f.InstrumentId)
            .Select(g => g.OrderBy(f => Math.Abs((f.Timestamp - timestamp).Ticks)).First())
            .ToListAsync(cancellationToken);

        return entities.Select(DeserializeFeatureVector).ToList();
    }

    public async Task<List<TrainingDataPoint>> BuildTrainingDatasetAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        int forwardReturnDays = 5,
        CancellationToken cancellationToken = default)
    {
        var features = await GetHistoricalAsync(instrumentId, startDate, endDate, cancellationToken);
        var priceData = await GetPriceDataForLabelsAsync(instrumentId, startDate, endDate.AddDays(forwardReturnDays), cancellationToken);

        var trainingData = new List<TrainingDataPoint>();

        foreach (var feature in features)
        {
            var currentPrice = GetPriceAtTimestamp(priceData, feature.Timestamp);
            if (currentPrice == null) continue;

            var futurePrice = GetPriceAtTimestamp(priceData, feature.Timestamp.AddDays(forwardReturnDays));
            if (futurePrice == null) continue;

            var return5d = (float)((futurePrice.Value - currentPrice.Value) / currentPrice.Value * 100);
            var return10d = forwardReturnDays >= 10
                ? CalculateReturn(priceData, feature.Timestamp, 10)
                : 0f;

            var dataPoint = new TrainingDataPoint
            {
                InstrumentId = instrumentId,
                Symbol = feature.Symbol,
                Timestamp = feature.Timestamp,
                Features = feature,
                TargetReturn5D = return5d,
                TargetReturn10D = return10d,
                TradeSuccessLabel = return5d > 2.0f  // Configurable threshold
            };

            trainingData.Add(dataPoint);
        }

        _logger.LogInformation("Built {Count} training data points for {Symbol}",
            trainingData.Count, trainingData.FirstOrDefault()?.Symbol ?? "unknown");

        return trainingData;
    }

    private QuantFeatureVector DeserializeFeatureVector(FeatureStore entity)
    {
        var features = JsonSerializer.Deserialize<Dictionary<string, float>>(entity.FeaturesJson)
                      ?? new Dictionary<string, float>();

        var vector = new QuantFeatureVector
        {
            InstrumentId = entity.InstrumentId,
            Symbol = entity.Symbol,
            Timestamp = entity.Timestamp
        };

        // Map dictionary back to properties
        var properties = typeof(QuantFeatureVector).GetProperties()
            .Where(p => p.PropertyType == typeof(float));

        foreach (var prop in properties)
        {
            if (features.TryGetValue(prop.Name, out var value))
            {
                prop.SetValue(vector, value);
            }
        }

        return vector;
    }

    private async Task<List<(DateTimeOffset Timestamp, decimal Price)>> GetPriceDataForLabelsAsync(
        int instrumentId,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        CancellationToken cancellationToken)
    {
        // Assuming you have a candles table
        var candles = await _context.Set<TradingSystem.Core.Models.Candle>()
            .Where(c => c.InstrumentId == instrumentId
                     && c.Timestamp >= startDate
                     && c.Timestamp <= endDate)
            .OrderBy(c => c.Timestamp)
            .Select(c => new { c.Timestamp, c.Close })
            .ToListAsync(cancellationToken);

        return candles.Select(c => (c.Timestamp, c.Close)).ToList();
    }

    private decimal? GetPriceAtTimestamp(List<(DateTimeOffset Timestamp, decimal Price)> prices, DateTimeOffset timestamp)
    {
        return prices
            .OrderBy(p => Math.Abs((p.Timestamp - timestamp).Ticks))
            .FirstOrDefault()
            .Price;
    }

    private float CalculateReturn(List<(DateTimeOffset Timestamp, decimal Price)> prices, DateTimeOffset startTime, int days)
    {
        var startPrice = GetPriceAtTimestamp(prices, startTime);
        var endPrice = GetPriceAtTimestamp(prices, startTime.AddDays(days));

        if (startPrice == null || endPrice == null || startPrice == 0) return 0f;

        return (float)((endPrice.Value - startPrice.Value) / startPrice.Value * 100);
    }
}