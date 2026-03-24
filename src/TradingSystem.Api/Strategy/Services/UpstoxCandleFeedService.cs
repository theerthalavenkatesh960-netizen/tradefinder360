using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Upstox;

namespace TradingSystem.Api.Strategy.Services;

/// <summary>
/// Polls Upstox intraday candle API every minute to produce a real-time stream of 1-min candles.
/// Acts as the live data feed for the ORB+FVG strategy.
/// </summary>
public sealed class UpstoxCandleFeedService : IDisposable
{
    private readonly UpstoxClient _upstoxClient;
    private readonly ILogger<UpstoxCandleFeedService> _logger;
    private readonly Channel<Candle> _channel;
    private readonly HashSet<DateTimeOffset> _emittedTimestamps = new();
    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private static readonly TimeSpan MarketOpen = new(9, 15, 0);
    private static readonly TimeSpan MarketClose = new(15, 30, 0);

    public UpstoxCandleFeedService(UpstoxClient upstoxClient, ILogger<UpstoxCandleFeedService> logger)
    {
        _upstoxClient = upstoxClient;
        _logger = logger;
        _channel = Channel.CreateUnbounded<Candle>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = true
        });
    }

    /// <summary>
    /// Starts polling for 1-min candles at the specified interval.
    /// </summary>
    public void StartPolling(string instrumentKey, TimeSpan pollInterval)
    {
        if (_pollingTask is not null)
        {
            _logger.LogWarning("[FEED] Polling already active for a session. Stop first.");
            return;
        }

        _cts = new CancellationTokenSource();
        _emittedTimestamps.Clear();

        _pollingTask = Task.Run(() => PollLoopAsync(instrumentKey, pollInterval, _cts.Token));
        _logger.LogInformation("[FEED] Started polling {Instrument} every {Interval}s", instrumentKey, pollInterval.TotalSeconds);
    }

    /// <summary>
    /// Stops the current polling session.
    /// </summary>
    public void StopPolling()
    {
        _cts?.Cancel();
        _pollingTask = null;
        _channel.Writer.TryComplete();
        _logger.LogInformation("[FEED] Polling stopped");
    }

    /// <summary>
    /// Returns an async enumerable of candles as they arrive. Used by the strategy orchestrator.
    /// </summary>
    public IAsyncEnumerable<Candle> GetCandleStreamAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }

    private async Task PollLoopAsync(string instrumentKey, TimeSpan pollInterval, CancellationToken ct)
    {
        _logger.LogInformation("[FEED] Poll loop started for {Instrument}", instrumentKey);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);

                if (nowIst.TimeOfDay < MarketOpen)
                {
                    var waitUntil = MarketOpen - nowIst.TimeOfDay;
                    _logger.LogDebug("[FEED] Before market hours. Waiting {Wait}", waitUntil);
                    await Task.Delay(waitUntil, ct);
                    continue;
                }

                if (nowIst.TimeOfDay > MarketClose)
                {
                    _logger.LogInformation("[FEED] Market closed for the day. Stopping poll.");
                    _channel.Writer.TryComplete();
                    return;
                }

                var candles = await _upstoxClient.GetIntradayCandlesV3Async(instrumentKey, intervalMinutes: 1);

                if (candles.Count > 0)
                {
                    // Only emit candles we haven't seen before (dedup by timestamp)
                    var newCandles = candles
                        .Where(c => _emittedTimestamps.Add(c.Timestamp))
                        .OrderBy(c => c.Timestamp)
                        .ToList();

                    foreach (var candle in newCandles)
                    {
                        await _channel.Writer.WriteAsync(candle, ct);
                        _logger.LogDebug("[FEED] Emitted candle at {Time}: O={O} H={H} L={L} C={C} V={V}",
                            candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
                    }

                    if (newCandles.Count > 0)
                        _logger.LogInformation("[FEED] Emitted {Count} new candle(s). Total tracked: {Total}",
                            newCandles.Count, _emittedTimestamps.Count);
                }

                await Task.Delay(pollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FEED] Error polling candles. Retrying in {Interval}s", pollInterval.TotalSeconds);
                await Task.Delay(pollInterval, ct);
            }
        }

        _channel.Writer.TryComplete();
        _logger.LogInformation("[FEED] Poll loop ended");
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
