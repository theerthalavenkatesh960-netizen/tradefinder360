using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;

namespace TradingSystem.Upstox.Services;

public class UpstoxPriceService : IUpstoxPriceService
{
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<UpstoxPriceService> _logger;

    public UpstoxPriceService(UpstoxClient upstoxClient, ILogger<UpstoxPriceService> logger)
    {
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task<List<InstrumentPrice>> FetchHistoricalPricesAsync(
        string instrumentKey,
        string interval,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var candles = await _upstoxClient.GetHistoricalCandlesAsync(instrumentKey, interval, fromDate, toDate);

            var prices = candles.Select(c => new InstrumentPrice
            {
                Timestamp = c.Timestamp,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume,
                Timeframe = MapIntervalToTimeframe(interval),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }).ToList();

            return prices;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching historical prices for instrument: {InstrumentKey}", instrumentKey);
            return new List<InstrumentPrice>();
        }
    }

    public async Task<Dictionary<string, List<InstrumentPrice>>> FetchBulkHistoricalPricesAsync(
        IEnumerable<string> instrumentKeys,
        string interval,
        DateTime fromDate,
        DateTime toDate,
        int batchSize = 10,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<InstrumentPrice>>();
        var keyList = instrumentKeys.ToList();

        _logger.LogInformation("Fetching historical prices for {Count} instruments", keyList.Count);

        for (int i = 0; i < keyList.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Bulk price fetch cancelled");
                break;
            }

            var batch = keyList.Skip(i).Take(batchSize).ToList();
            var tasks = batch.Select(async key =>
            {
                var prices = await FetchHistoricalPricesAsync(key, interval, fromDate, toDate, cancellationToken);
                return new { Key = key, Prices = prices };
            });

            var batchResults = await Task.WhenAll(tasks);

            foreach (var item in batchResults)
            {
                result[item.Key] = item.Prices;
            }

            _logger.LogInformation("Processed batch {CurrentBatch}/{TotalBatches}",
                (i / batchSize) + 1,
                (keyList.Count + batchSize - 1) / batchSize);

            await Task.Delay(1000, cancellationToken);
        }

        return result;
    }

    public async Task<Dictionary<string, InstrumentPrice>> FetchCurrentQuotesAsync(
        IEnumerable<string> instrumentKeys,
        int batchSize = 500,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, InstrumentPrice>();
        var keyList = instrumentKeys.ToList();

        _logger.LogInformation("Fetching current quotes for {Count} instruments in batches of {BatchSize}", keyList.Count, batchSize);

        for (int i = 0; i < keyList.Count; i += batchSize)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Quote fetch cancelled");
                break;
            }

            var batch = keyList.Skip(i).Take(batchSize).ToList();
            var commaSeparatedKeys = string.Join(",", batch);

            try
            {
                var quotes = await _upstoxClient.GetQuotesAsync(commaSeparatedKeys);

                foreach (var (key, price) in quotes)
                {
                    result[key] = price;
                }

                _logger.LogInformation("Fetched quotes for batch {CurrentBatch}/{TotalBatches} ({Count} instruments)",
                    (i / batchSize) + 1,
                    (keyList.Count + batchSize - 1) / batchSize,
                    quotes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching quotes for batch {CurrentBatch}", (i / batchSize) + 1);
            }

            // Small delay between batches to respect rate limits
            if (i + batchSize < keyList.Count)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        return result;
    }

    private string MapIntervalToTimeframe(string interval)
    {
        return interval switch
        {
            "1minute" => "1m",
            "5minute" => "5m",
            "15minute" => "15m",
            "30minute" => "30m",
            "60minute" => "1h",
            "1day" => "1D",
            _ => "1D"
        };
    }
}
