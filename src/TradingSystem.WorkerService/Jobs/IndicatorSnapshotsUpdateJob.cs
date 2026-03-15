using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using System.Collections.Concurrent;
using TradingSystem.Core.Models;
using TradingSystem.Core.Utilities;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
 
namespace TradingSystem.WorkerService.Jobs;
 
[DisallowConcurrentExecution]
public class IndicatorSnapshotsUpdateJob : IJob
{
    private const int MaxParallelism     = 12;
    private const int MinCandlesRequired = 100; // CHANGED from 90 - need 50 (EMA slow) + 28 (ADX) + 22 buffer
    private const int TimeframeMinutes   = 15;
 
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
 
    private static DateTime IstToday =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist).Date;
 
    // Shared config — immutable, safe across all parallel workers
    private static readonly IndicatorEngineConfig EngineConfig = new(
        EmaFastPeriod:   20,
        EmaSlowPeriod:   50,
        RsiPeriod:       14,
        MacdFast:        12,
        MacdSlow:        26,
        MacdSignal:       9,
        AdxPeriod:       14,
        AtrPeriod:       14,
        BollingerPeriod: 20,
        BollingerStdDev: 2.0m
    );
 
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IndicatorSnapshotsUpdateJob> _logger;
 
    public IndicatorSnapshotsUpdateJob(
        IServiceScopeFactory scopeFactory,
        ILogger<IndicatorSnapshotsUpdateJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }
 
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();
 
        _logger.LogInformation("═══ Indicator snapshots update job STARTED ═══");
 
        var errors = new ConcurrentBag<(string Symbol, string Error)>();
 
        try
        {
            List<TradingInstrument> instruments;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
                instruments = (await repo.GetActiveInstrumentsAsync(ct)).ToList();
            }
 
            if (instruments.Count == 0)
            {
                _logger.LogWarning("No active instruments found — aborting");
                return;
            }
 
            _logger.LogInformation(
                "Processing {Count} instruments | TF: {TF}m | Parallelism: {P}",
                instruments.Count, TimeframeMinutes, MaxParallelism);
 
            int processed    = 0;
            int totalSaved   = 0;
            int totalSkipped = 0;
 
            await Parallel.ForEachAsync(
                instruments,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxParallelism,
                    CancellationToken      = ct
                },
                async (instrument, innerCt) =>
                {
                    // Isolated scope per instrument = isolated DbContext
                    await using var scope    = _scopeFactory.CreateAsyncScope();
                    var candleService        = scope.ServiceProvider.GetRequiredService<ICandleService>();
                    var indicatorService     = scope.ServiceProvider.GetRequiredService<IIndicatorService>();
 
                    try
                    {
                        var (saved, skipped) = await ProcessInstrumentAsync(
                            instrument, candleService, indicatorService, innerCt);
 
                        Interlocked.Add(ref totalSaved,   saved);
                        Interlocked.Add(ref totalSkipped, skipped);
 
                        var done = Interlocked.Increment(ref processed);
                        if (done % 200 == 0 || done == instruments.Count)
                            _logger.LogInformation(
                                "Progress: {Done}/{Total} | Saved: {Saved} | Skipped: {Skipped} | Errors: {Errors}",
                                done, instruments.Count, totalSaved, totalSkipped, errors.Count);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        errors.Add((instrument.Symbol, ex.Message));
                        _logger.LogError(ex, "{Symbol} — failed: {Msg}",
                            instrument.Symbol, ex.Message);
                    }
                });
 
            _logger.LogInformation(
                "FINAL: Instruments={I} | Saved={S} | Skipped={Sk} | Errors={E} | Elapsed={El:mm\\:ss}",
                instruments.Count, totalSaved, totalSkipped, errors.Count, sw.Elapsed);
 
            if (errors.Count > 0)
            {
                foreach (var group in errors.GroupBy(e => e.Error).OrderByDescending(g => g.Count()).Take(10))
                    _logger.LogWarning("  {Count}× {Error} — e.g. {Samples}",
                        group.Count(), group.Key,
                        string.Join(", ", group.Take(3).Select(e => e.Symbol)));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Job CANCELLED after {Elapsed:mm\\:ss}", sw.Elapsed);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL failure after {Elapsed:mm\\:ss}", sw.Elapsed);
            throw;
        }
    }
 
    private async Task<(int Saved, int Skipped)> ProcessInstrumentAsync(
        TradingInstrument instrument,
        ICandleService candleService,
        IIndicatorService indicatorService,
        CancellationToken ct)
    {
        // Step 1 — find where we left off
        var latestSnapshot = await indicatorService.GetLatestAsync(
            instrument.Id, TimeframeMinutes);
 
        DateTime fetchFrom;
        if (latestSnapshot is not null)
        {
            // FIX: .DateTime on DateTimeOffset returns UTC representation.
            // Convert to IST first, then advance by one timeframe interval.
            var latestIst = TimeZoneInfo.ConvertTime(latestSnapshot.Timestamp, Ist);
            fetchFrom = latestIst.DateTime.AddMinutes(TimeframeMinutes);
        }
        else
        {
            // First run — go back 3 months + warmup buffer
            fetchFrom = IstToday.AddMonths(-3).AddDays(-(MinCandlesRequired / 25));
        }
 
        // FIX: IstToday not DateTime.Today (server local != IST)
        var toDate = IstToday.AddDays(1);
 
        if (fetchFrom >= toDate)
            return (0, 1); // already up to date
 
        // Step 2 — fetch candles including warmup window before fetchFrom
        // so the engine has enough history to produce stable indicator values
        var warmupFrom = fetchFrom.AddMinutes(-(MinCandlesRequired * TimeframeMinutes));
 
        var allCandles = await candleService.GetCandlesAsync(
            instrument.Id, TimeframeMinutes, warmupFrom, toDate);
 
        if (allCandles.Count == 0)
            return (0, 1);
 
        // Step 3 — split into warmup (prime engine, don't save) and new (save)
        // FIX: compare timestamps in UTC — both sides must be the same offset
        var fetchFromUtc = new DateTimeOffset(fetchFrom, TimeSpan.FromHours(5.5))
                               .ToUniversalTime();
 
        var warmupCandles = allCandles
            .Where(c => c.Timestamp.ToUniversalTime() < fetchFromUtc)
            .OrderBy(c => c.Timestamp)
            .ToList();
 
        var newCandles = allCandles
            .Where(c => c.Timestamp.ToUniversalTime() >= fetchFromUtc)
            .OrderBy(c => c.Timestamp)
            .ToList();
 
        if (newCandles.Count == 0)
            return (0, 1);
 
        if (warmupCandles.Count + newCandles.Count < MinCandlesRequired)
        {
            _logger.LogDebug("{Symbol} {TF}m — insufficient candles ({Count}/{Min})",
                instrument.Symbol, TimeframeMinutes,
                warmupCandles.Count + newCandles.Count, MinCandlesRequired);
            return (0, 1);
        }
 
        // Step 4 — create fresh engine per instrument (engine is stateful)
        var engine = new IndicatorEngine(
            emaFastPeriod:   EngineConfig.EmaFastPeriod,
            emaSlowPeriod:   EngineConfig.EmaSlowPeriod,
            rsiPeriod:       EngineConfig.RsiPeriod,
            macdFast:        EngineConfig.MacdFast,
            macdSlow:        EngineConfig.MacdSlow,
            macdSignal:      EngineConfig.MacdSignal,
            adxPeriod:       EngineConfig.AdxPeriod,
            atrPeriod:       EngineConfig.AtrPeriod,
            bollingerPeriod: EngineConfig.BollingerPeriod,
            bollingerStdDev: EngineConfig.BollingerStdDev
        );
 
        // Step 5 — prime engine with warmup candles (results discarded)
        foreach (var candle in warmupCandles)
            engine.Calculate(candle);
 
        // Step 6 — calculate indicators for new candles, collect into list
        var snapshots = new List<IndicatorSnapshot>(newCandles.Count);
 
        foreach (var candle in newCandles)
        {
            var indicators = engine.Calculate(candle);
 
            // ✅ ADD VALIDATION: Skip snapshots during warmup period (indicators = 0)
            // Only save once critical indicators are fully seeded
            bool isValidSnapshot = 
                indicators.EMASlow != 0 &&    // EMA slow (50) is seeded
                indicators.ADX != 0 &&        // ADX (28 candles) is seeded
                indicators.ATR != 0 &&        // ATR (14) is seeded
                indicators.BollingerMiddle != 0; // Bollinger (20) is seeded
            
            if (!isValidSnapshot)
            {
                // Skip this candle - indicators still in warmup phase
                continue;
            }

            // Map IndicatorValues → IndicatorSnapshot with 4dp rounding
            snapshots.Add(new IndicatorSnapshot
            {
                InstrumentId     = instrument.Id,
                TimeframeMinutes = TimeframeMinutes,
                Timestamp        = candle.Timestamp,
                // ✅ ADDED: Round all values to 4 decimal places before saving
                EMAFast          = Math.Round(indicators.EMAFast,         4, MidpointRounding.AwayFromZero),
                EMASlow          = Math.Round(indicators.EMASlow,         4, MidpointRounding.AwayFromZero),
                RSI              = Math.Round(indicators.RSI,             4, MidpointRounding.AwayFromZero),
                MacdLine         = Math.Round(indicators.MacdLine,        4, MidpointRounding.AwayFromZero),
                MacdSignal       = Math.Round(indicators.MacdSignal,      4, MidpointRounding.AwayFromZero),
                MacdHistogram    = Math.Round(indicators.MacdHistogram,   4, MidpointRounding.AwayFromZero),
                ADX              = Math.Round(indicators.ADX,             4, MidpointRounding.AwayFromZero),
                PlusDI           = Math.Round(indicators.PlusDI,          4, MidpointRounding.AwayFromZero),
                MinusDI          = Math.Round(indicators.MinusDI,         4, MidpointRounding.AwayFromZero),
                ATR              = Math.Round(indicators.ATR,             4, MidpointRounding.AwayFromZero),
                BollingerUpper   = Math.Round(indicators.BollingerUpper,  4, MidpointRounding.AwayFromZero),
                BollingerMiddle  = Math.Round(indicators.BollingerMiddle, 4, MidpointRounding.AwayFromZero),
                BollingerLower   = Math.Round(indicators.BollingerLower,  4, MidpointRounding.AwayFromZero),
                VWAP             = Math.Round(indicators.VWAP,            4, MidpointRounding.AwayFromZero),
                CreatedAt        = DateTimeOffset.UtcNow
            });
        }
 
        // Step 7 — single bulk DB write for all new snapshots
        if (snapshots.Count > 0) // ✅ ADDED: Only save if we have valid snapshots
        {
            await indicatorService.BulkSaveAsync(snapshots, ct);
 
            _logger.LogDebug("{Symbol} {TF}m — saved {Count} snapshots",
                instrument.Symbol, TimeframeMinutes, snapshots.Count);
        }
        else
        {
            _logger.LogDebug("{Symbol} {TF}m — no valid snapshots (still in warmup)",
                instrument.Symbol, TimeframeMinutes);
        }
 
        return (snapshots.Count, newCandles.Count - snapshots.Count);
    }
 
    private record IndicatorEngineConfig(
        int     EmaFastPeriod,
        int     EmaSlowPeriod,
        int     RsiPeriod,
        int     MacdFast,
        int     MacdSlow,
        int     MacdSignal,
        int     AdxPeriod,
        int     AtrPeriod,
        int     BollingerPeriod,
        decimal BollingerStdDev);
}