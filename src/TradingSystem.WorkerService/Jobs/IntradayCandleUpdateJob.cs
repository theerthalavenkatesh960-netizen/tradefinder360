using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Job to fetch live 1-minute intraday candles during market hours.
/// Runs every minute to keep real-time candle data updated.
/// </summary>
[DisallowConcurrentExecution]
public class IntradayCandleUpdateJob : IJob
{
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly IMarketCandleRepository _candleRepository;
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<IntradayCandleUpdateJob> _logger;

    // Market hours (IST): 9:15 AM - 3:30 PM
    private static readonly TimeSpan MarketOpen = new(9, 15, 0);
    private static readonly TimeSpan MarketClose = new(15, 30, 0);

    public IntradayCandleUpdateJob(
        IInstrumentRepository instrumentRepository,
        IMarketCandleRepository candleRepository,
        UpstoxClient upstoxClient,
        ILogger<IntradayCandleUpdateJob> logger)
    {
        _instrumentRepository = instrumentRepository;
        _candleRepository = candleRepository;
        _upstoxClient = upstoxClient;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var now = DateTime.UtcNow.AddHours(5.5); // Convert to IST
        var currentTime = now.TimeOfDay;

        // Skip if outside market hours
        if (currentTime < MarketOpen || currentTime > MarketClose)
        {
            _logger.LogDebug("Outside market hours, skipping intraday update");
            return;
        }

        _logger.LogInformation("=== Intraday Candle Update Job Started (1m) ===");

        try
        {
            var activeInstruments = await _instrumentRepository.GetActiveInstrumentsAsync(context.CancellationToken);
            _logger.LogInformation("Updating {Count} instruments with latest 1m candles", activeInstruments.Count);

            // Process in parallel batches
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = 20,
                CancellationToken = context.CancellationToken
            };

            var totalSaved = 0;

            await Parallel.ForEachAsync(activeInstruments, options, async (instrument, ct) =>
            {
                try
                {
                    var candles = await _upstoxClient.GetIntradayCandlesV3Async(
                        instrument.InstrumentKey,
                        intervalMinutes: 1);

                    if (candles.Any())
                    {
                        var marketCandles = candles.Select(c => new MarketCandle
                        {
                            InstrumentId = instrument.Id,
                            TimeframeMinutes = 1,
                            Timestamp = c.Timestamp,
                            Open = c.Open,
                            High = c.High,
                            Low = c.Low,
                            Close = c.Close,
                            Volume = c.Volume,
                            CreatedAt = DateTimeOffset.UtcNow
                        }).ToList();

                        var saved = await _candleRepository.BulkUpsertAsync(marketCandles, ct);
                        Interlocked.Add(ref totalSaved, saved);

                        _logger.LogDebug(
                            "Updated {Symbol}: {Count} candles",
                            instrument.Symbol,
                            saved);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error updating intraday candles for {Symbol}",
                        instrument.Symbol);
                }
            });

            _logger.LogInformation(
                "=== Intraday Update Completed: {Total} candles saved ===",
                totalSaved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in intraday update job");
            throw;
        }
    }
}