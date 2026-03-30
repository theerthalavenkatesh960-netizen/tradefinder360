using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingSystem.Api.Strategy.Interfaces;
using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Services;

/// <summary>
/// Background service that runs the ORB+FVG strategy during market hours.
/// Polls Upstox for 1-min candles and feeds them to the StrategyOrchestrator.
/// Emits trade signals via ITradeSignalNotifier.
/// </summary>
public sealed class NiftyOrbStrategyWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NiftyOrbStrategyWorker> _logger;
    private readonly IntraDayStrategyConfig _config;

    // Default instrument key for NIFTY 50 index on Upstox
    private const string DefaultInstrumentKey = "NSE_INDEX|Nifty 50";
    private const string SymbolName = "NIFTY";

    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private static readonly TimeSpan MarketOpen = new(9, 15, 0);
    private static readonly TimeSpan SessionEndTime = new(15, 30, 0);

    public NiftyOrbStrategyWorker(
        IServiceProvider serviceProvider,
        IOptions<IntraDayStrategyConfig> config,
        ILogger<NiftyOrbStrategyWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WORKER] NIFTY ORB+FVG Strategy Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);

                // Wait until market opens
                if (nowIst.TimeOfDay < MarketOpen)
                {
                    var waitTime = MarketOpen - nowIst.TimeOfDay;
                    _logger.LogInformation("[WORKER] Waiting {Wait} until market opens at 09:15 IST", waitTime);
                    await Task.Delay(waitTime, stoppingToken);
                    continue;
                }

                // If market is closed, wait until next day 09:15
                if (nowIst.TimeOfDay > SessionEndTime)
                {
                    var nextOpen = nowIst.Date.AddDays(1).Add(MarketOpen);
                    // Skip weekends
                    while (nextOpen.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        nextOpen = nextOpen.AddDays(1);

                    var waitTime = nextOpen - nowIst;
                    _logger.LogInformation("[WORKER] Market closed. Next session in {Wait}", waitTime);
                    await Task.Delay(waitTime, stoppingToken);
                    continue;
                }

                // Run one trading session
                await RunSessionAsync(DateOnly.FromDateTime(nowIst), stoppingToken);

                // After session completes, wait until next day
                var nextDayOpen = nowIst.Date.AddDays(1).Add(MarketOpen);
                while (nextDayOpen.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    nextDayOpen = nextDayOpen.AddDays(1);

                var waitForNextSession = nextDayOpen - TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);
                if (waitForNextSession > TimeSpan.Zero)
                {
                    _logger.LogInformation("[WORKER] Session complete. Next session in {Wait}", waitForNextSession);
                    await Task.Delay(waitForNextSession, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WORKER] Unhandled error in strategy worker. Restarting in 60s...");
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }

        _logger.LogInformation("[WORKER] NIFTY ORB+FVG Strategy Worker stopped");
    }

    private async Task RunSessionAsync(DateOnly sessionDate, CancellationToken ct)
    {
        _logger.LogInformation("[WORKER] === Starting trading session for {Date} ===", sessionDate);

        using var scope = _serviceProvider.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<IStrategyOrchestrator>();
        var feedService = scope.ServiceProvider.GetRequiredService<UpstoxCandleFeedService>();
        var signalNotifier = scope.ServiceProvider.GetRequiredService<ITradeSignalNotifier>();

        // Start polling Upstox for 1-min candles every 60 seconds
        feedService.StartPolling(DefaultInstrumentKey, TimeSpan.FromSeconds(60));

        try
        {
            var candleStream = feedService.GetCandleStreamAsync(ct);

            await foreach (var signal in orchestrator.RunAsync(SymbolName, candleStream, sessionDate, _config, ct))
            {
                _logger.LogWarning(
                    "[WORKER] ?? TRADE SIGNAL: {Dir} {Symbol} @ {Entry} | SL={SL} | T={Target} | Conf={Conf}%",
                    signal.SignalDirection, signal.Symbol, signal.EntryPrice,
                    signal.StopLoss, signal.Target, signal.ConfidenceScore);

                await signalNotifier.NotifyAsync(signal, ct);
            }
        }
        finally
        {
            feedService.StopPolling();
        }

        _logger.LogInformation("[WORKER] === Session ended for {Date} ===", sessionDate);
    }
}
