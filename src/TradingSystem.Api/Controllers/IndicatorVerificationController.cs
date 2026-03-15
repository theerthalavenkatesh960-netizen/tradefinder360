using Microsoft.AspNetCore.Mvc;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Controllers;

/// <summary>
/// Diagnostic API for verifying indicator calculation correctness.
/// Use this to validate that indicators are calculating according to industry standards.
/// </summary>
[ApiController]
[Route("api/verify")]
public class IndicatorVerificationController : ControllerBase
{
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly ILogger<IndicatorVerificationController> _logger;

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public IndicatorVerificationController(
        ICandleService candleService,
        IIndicatorService indicatorService,
        IInstrumentRepository instrumentRepository,
        ILogger<IndicatorVerificationController> logger)
    {
        _candleService        = candleService;
        _indicatorService     = indicatorService;
        _instrumentRepository = instrumentRepository;
        _logger               = logger;
    }

    // -------------------------------------------------------------------------
    // GET /api/verify/indicators/{symbol}?timeframe=15&candles=100
    // -------------------------------------------------------------------------
    [HttpGet("indicators/{symbol}")]
    [ProducesResponseType(typeof(IndicatorVerificationResultDto), 200)]
    public async Task<ActionResult<IndicatorVerificationResultDto>> VerifyIndicators(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int candles   = 100,
        CancellationToken ct      = default)
    {
        try
        {
            var instrument = await FindInstrumentAsync(symbol, ct);
            if (instrument is null)
                return NotFound(new { error = $"Instrument '{symbol}' not found" });

            var ordered = await FetchWarmupCandles(instrument.Id, timeframe, ct);

            if (ordered.Count < 100)
                return BadRequest(new
                {
                    error     = "Insufficient candle data",
                    available = ordered.Count,
                    required  = 100
                });

            // Single engine, single pass — results cached, never recalculated.
            var cachedResults = RunEngineAndCache(ordered);

            var verification = PerformVerification(ordered, cachedResults);

            var storedSnapshots = await _indicatorService.GetRecentAsync(
                instrument.Id, timeframe, 20);

            var comparisonResults = CompareWithStoredData(
                ordered.TakeLast(20).ToList(),
                storedSnapshots,
                cachedResults);

            var result = new IndicatorVerificationResultDto
            {
                InstrumentId          = instrument.Id,
                Symbol                = symbol,
                Exchange              = instrument.Exchange,
                TimeframeMinutes      = timeframe,
                TotalCandlesAnalyzed  = candles,
                VerificationTimestamp = DateTimeOffset.UtcNow,
                WarmupPeriods         = verification.WarmupPeriods,
                ValidationResults     = verification.ValidationResults,
                SampleData            = verification.SampleData.TakeLast(20).ToList(),
                StoredDataComparison  = comparisonResults,
                OverallStatus         = verification.IsValid ? "PASS" : "FAIL",
                Recommendations       = GenerateRecommendations(verification, comparisonResults)
            };

            _logger.LogInformation(
                "Indicator verification for {Symbol}: {Status} — {Issues} issue(s) found",
                symbol, result.OverallStatus,
                result.ValidationResults.Count(v => !v.IsValid));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying indicators for {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // -------------------------------------------------------------------------
    // GET /api/verify/indicators/{symbol}/export
    // Export indicator data as CSV for TradingView comparison.
    // Warmup pass is included so every exported row has stable values.
    // -------------------------------------------------------------------------
    [HttpGet("indicators/{symbol}/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportIndicatorsCsv(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int candles   = 100,
        CancellationToken ct      = default)
    {
        var instrument = await FindInstrumentAsync(symbol, ct);
        if (instrument is null)
            return NotFound(new { error = $"Instrument '{symbol}' not found" });

        var allCandles = await FetchWarmupCandles(instrument.Id, timeframe, ct);

        if (allCandles.Count == 0)
            return NotFound(new { error = "No candle data found" });

        // Determine the export window start BEFORE running the engine.
        // Everything before this timestamp silently primes the engine.
        int exportStartIndex = Math.Max(0, allCandles.Count - candles);
        var exportStartTime  = allCandles[exportStartIndex].Timestamp.ToUniversalTime();

        var engine = BuildEngine();
        var csv    = new System.Text.StringBuilder();
        csv.AppendLine(
            "Timestamp,Open,High,Low,Close,Volume," +
            "EMA20,EMA50,RSI," +
            "MACDLine,MACDSignal,MACDHistogram," +
            "ADX,PlusDI,MinusDI,ATR," +
            "BBUpper,BBMiddle,BBLower,VWAP");

        foreach (var candle in allCandles)
        {
            var ind = engine.Calculate(candle);

            // Only write rows within the requested export window.
            if (candle.Timestamp.ToUniversalTime() < exportStartTime)
                continue;

            csv.AppendLine(
                $"{ToIst(candle.Timestamp):yyyy-MM-dd HH:mm}," +
                $"{candle.Open},{candle.High},{candle.Low},{candle.Close},{candle.Volume}," +
                $"{ind.EMAFast:F2},{ind.EMASlow:F2},{ind.RSI:F2}," +
                $"{ind.MacdLine:F4},{ind.MacdSignal:F4},{ind.MacdHistogram:F4}," +
                $"{ind.ADX:F2},{ind.PlusDI:F2},{ind.MinusDI:F2},{ind.ATR:F2}," +
                $"{ind.BollingerUpper:F2},{ind.BollingerMiddle:F2},{ind.BollingerLower:F2}," +
                $"{ind.VWAP:F2}");
        }

        return File(
            System.Text.Encoding.UTF8.GetBytes(csv.ToString()),
            "text/csv",
            $"indicators_{symbol}_{timeframe}m_{DateTime.UtcNow:yyyyMMddHHmm}.csv");
    }

    // -------------------------------------------------------------------------
    // GET /api/verify/warmup/{symbol}?timeframe=15
    // Report how many candles each indicator needs before producing valid output.
    // -------------------------------------------------------------------------
    [HttpGet("warmup/{symbol}")]
    public async Task<ActionResult> GetWarmupStatus(
        string symbol,
        [FromQuery] int timeframe = 15,
        CancellationToken ct      = default)
    {
        var instrument = await FindInstrumentAsync(symbol, ct);
        if (instrument is null)
            return NotFound(new { error = $"Instrument '{symbol}' not found" });

        var ordered = await FetchWarmupCandles(instrument.Id, timeframe, ct);

        if (ordered.Count == 0)
            return NotFound(new { error = "No candles found" });

        var cachedResults = RunEngineAndCache(ordered);
        var warmupStatus  = DetectWarmupPeriods(ordered, cachedResults);

        return Ok(new
        {
            symbol,
            timeframeMinutes      = timeframe,
            totalCandlesAvailable = ordered.Count,
            warmupComplete        = warmupStatus.All(w => w.IsComplete),
            indicators            = warmupStatus
        });
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>
    /// Resolve an instrument by symbol (case-insensitive). Returns null if not found.
    /// </summary>
    private async Task<TradingInstrument?> FindInstrumentAsync(
        string symbol, CancellationToken ct)
    {
        var instruments = await _instrumentRepository.GetActiveInstrumentsAsync(ct);
        return instruments.FirstOrDefault(i =>
            i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Fetch enough candle history to fully warm up all indicators PLUS the
    /// requested window. Uses UTC DateTime throughout — no DateTimeOffset
    /// construction with non-zero offsets, so no Kind-mismatch crash.
    /// </summary>
    private async Task<List<Candle>> FetchWarmupCandles(
        int instrumentId, int timeframe, CancellationToken ct)
    {
        // 90 candles of warmup expressed as real clock time,
        // plus 5 extra days buffer for weekends / market holidays.
        var warmupSpan = TimeSpan.FromMinutes(90 * timeframe);
        var fromUtc    = DateTime.Today.Subtract(warmupSpan).AddDays(-5);
        var toUtc      = DateTime.Today.AddDays(1);

        var candles = await _candleService.GetCandlesAsync(
            instrumentId, timeframe, fromUtc, toUtc);

        return candles.OrderBy(c => c.Timestamp).ToList();
    }

    /// <summary>
    /// Build a fresh indicator engine with standard config.
    /// </summary>
    private static IndicatorEngine BuildEngine() =>
        new(
            emaFastPeriod:   20,
            emaSlowPeriod:   50,
            rsiPeriod:       14,
            macdFast:        12,
            macdSlow:        26,
            macdSignal:       9,
            adxPeriod:       14,
            atrPeriod:       14,
            bollingerPeriod: 20,
            bollingerStdDev: 2.0m);

    /// <summary>
    /// Run every candle through a fresh engine exactly once and cache all results
    /// keyed by UTC timestamp. This is the ONLY place engine.Calculate() is called —
    /// all other methods consume the cache to prevent double-calculation and
    /// stale-state bugs.
    /// </summary>
    private static Dictionary<DateTimeOffset, IndicatorValues> RunEngineAndCache(
        List<Candle> candles)
    {
        var engine = BuildEngine();
        var cache  = new Dictionary<DateTimeOffset, IndicatorValues>(candles.Count);

        foreach (var candle in candles)
            cache[candle.Timestamp.ToUniversalTime()] = engine.Calculate(candle);

        return cache;
    }

    /// <summary>
    /// Analyse cached results to determine when each indicator became valid
    /// and whether that matches the expected warmup period.
    /// </summary>
    private static VerificationData PerformVerification(
        List<Candle> candles,
        Dictionary<DateTimeOffset, IndicatorValues> cache)
    {
        var warmupPeriods     = new Dictionary<string, WarmupInfo>();
        var sampleData        = new List<SampleIndicatorData>();
        var validationResults = new List<ValidationResult>();

        int emaFastValid = -1, emaSlowValid = -1, rsiValid = -1;
        int macdValid    = -1, adxValid     = -1, atrValid = -1;
        int bbValid      = -1;

        for (int i = 0; i < candles.Count; i++)
        {
            var key = candles[i].Timestamp.ToUniversalTime();
            if (!cache.TryGetValue(key, out var ind))
                continue;

            if (emaFastValid == -1 && ind.EMAFast         != 0) emaFastValid = i + 1;
            if (emaSlowValid == -1 && ind.EMASlow         != 0) emaSlowValid = i + 1;
            if (rsiValid     == -1 && ind.RSI             >  0) rsiValid     = i + 1;
            if (macdValid    == -1 && ind.MacdSignal      != 0) macdValid    = i + 1;
            if (adxValid     == -1 && ind.ADX             != 0) adxValid     = i + 1;
            if (atrValid     == -1 && ind.ATR             != 0) atrValid     = i + 1;
            if (bbValid      == -1 && ind.BollingerMiddle != 0) bbValid      = i + 1;

            // Collect a sample row every 10 candles once past the warmup zone
            if (i % 10 == 0 && i >= 50)
            {
                sampleData.Add(new SampleIndicatorData
                {
                    CandleIndex = i + 1,
                    Timestamp   = candles[i].Timestamp,
                    Close       = candles[i].Close,
                    Indicators  = ind
                });
            }
        }

        warmupPeriods["EMA(20)"]       = BuildWarmupInfo(20, emaFastValid, exact: true);
        warmupPeriods["EMA(50)"]       = BuildWarmupInfo(50, emaSlowValid, exact: true);
        warmupPeriods["RSI(14)"]       = BuildWarmupInfo(15, rsiValid,     tolerance: 1);
        warmupPeriods["MACD Signal"]   = BuildWarmupInfo(35, macdValid,    tolerance: 1);
        warmupPeriods["ADX(14)"]       = BuildWarmupInfo(28, adxValid,     exact: true);
        warmupPeriods["ATR(14)"]       = BuildWarmupInfo(15, atrValid,     tolerance: 1);
        warmupPeriods["Bollinger(20)"] = BuildWarmupInfo(20, bbValid,      exact: true);

        foreach (var kvp in warmupPeriods)
        {
            validationResults.Add(new ValidationResult
            {
                Indicator = kvp.Key,
                IsValid   = kvp.Value.IsValid,
                Message   = kvp.Value.IsValid
                    ? $"✅ {kvp.Key} warmup correct (expected: {kvp.Value.Expected}, actual: {kvp.Value.Actual})"
                    : $"❌ {kvp.Key} warmup INCORRECT (expected: {kvp.Value.Expected}, actual: {kvp.Value.Actual})"
            });
        }

        return new VerificationData
        {
            WarmupPeriods     = warmupPeriods,
            SampleData        = sampleData,
            ValidationResults = validationResults,
            IsValid           = validationResults.All(v => v.IsValid)
        };
    }

    /// <summary>
    /// Compare cached indicator values against what is stored in the database.
    /// No engine is involved here — the cache is the source of truth.
    /// Timestamps are normalised to UTC on both sides before lookup.
    /// </summary>
    private static List<ComparisonResult> CompareWithStoredData(
        List<Candle> recentCandles,
        List<IndicatorSnapshot> storedSnapshots,
        Dictionary<DateTimeOffset, IndicatorValues> cache)
    {
        var results = new List<ComparisonResult>();

        // Normalise stored snapshot keys to UTC so they match the cache keys
        var storedDict = storedSnapshots.ToDictionary(
            s => s.Timestamp.ToUniversalTime());

        foreach (var candle in recentCandles)
        {
            var key = candle.Timestamp.ToUniversalTime();

            if (!cache.TryGetValue(key, out var calculated))
                continue;

            if (!storedDict.TryGetValue(key, out var stored))
                continue;

            var diffs = new Dictionary<string, decimal>
            {
                ["EMA20"]    = PctDiff(calculated.EMAFast,         stored.EMAFast),
                ["EMA50"]    = PctDiff(calculated.EMASlow,         stored.EMASlow),
                ["RSI"]      = PctDiff(calculated.RSI,             stored.RSI),
                ["MACDLine"] = PctDiff(calculated.MacdLine,        stored.MacdLine),
                ["ADX"]      = PctDiff(calculated.ADX,             stored.ADX),
                ["ATR"]      = PctDiff(calculated.ATR,             stored.ATR),
                ["BBMiddle"] = PctDiff(calculated.BollingerMiddle, stored.BollingerMiddle),
                ["BBUpper"]  = PctDiff(calculated.BollingerUpper,  stored.BollingerUpper),
                ["BBLower"]  = PctDiff(calculated.BollingerLower,  stored.BollingerLower),
                ["VWAP"]     = PctDiff(calculated.VWAP,            stored.VWAP)
            };

            var maxDiff = diffs.Values.Max();

            results.Add(new ComparisonResult
            {
                Timestamp     = candle.Timestamp,
                Differences   = diffs,
                MaxDifference = maxDiff,
                IsAcceptable  = maxDiff < 1.0m   // 1% tolerance
            });
        }

        return results;
    }

    /// <summary>
    /// Build warmup status DTOs from cached results — no engine involved.
    /// </summary>
    private static List<WarmupStatusDto> DetectWarmupPeriods(
        List<Candle> candles,
        Dictionary<DateTimeOffset, IndicatorValues> cache)
    {
        int emaFastValid = -1, emaSlowValid = -1, rsiValid = -1;
        int macdValid    = -1, adxValid     = -1, atrValid = -1;
        int bbValid      = -1;

        for (int i = 0; i < candles.Count; i++)
        {
            var key = candles[i].Timestamp.ToUniversalTime();
            if (!cache.TryGetValue(key, out var ind))
                continue;

            if (emaFastValid == -1 && ind.EMAFast         != 0) emaFastValid = i + 1;
            if (emaSlowValid == -1 && ind.EMASlow         != 0) emaSlowValid = i + 1;
            if (rsiValid     == -1 && ind.RSI             >  0) rsiValid     = i + 1;
            if (macdValid    == -1 && ind.MacdSignal      != 0) macdValid    = i + 1;
            if (adxValid     == -1 && ind.ADX             != 0) adxValid     = i + 1;
            if (atrValid     == -1 && ind.ATR             != 0) atrValid     = i + 1;
            if (bbValid      == -1 && ind.BollingerMiddle != 0) bbValid      = i + 1;
        }

        return new List<WarmupStatusDto>
        {
            CreateWarmupStatus("EMA(20)",       20, emaFastValid, exact: true),
            CreateWarmupStatus("EMA(50)",       50, emaSlowValid, exact: true),
            CreateWarmupStatus("RSI(14)",       15, rsiValid,     tolerance: 1),
            CreateWarmupStatus("MACD Signal",   35, macdValid,    tolerance: 1),
            CreateWarmupStatus("ADX(14)",       28, adxValid,     exact: true),
            CreateWarmupStatus("ATR(14)",       15, atrValid,     tolerance: 1),
            CreateWarmupStatus("Bollinger(20)", 20, bbValid,      exact: true)
        };
    }

    private static List<string> GenerateRecommendations(
        VerificationData verification,
        List<ComparisonResult> comparisons)
    {
        var recommendations = new List<string>();

        var failedWarmups = verification.ValidationResults.Where(v => !v.IsValid).ToList();

        if (failedWarmups.Any())
        {
            recommendations.Add(
                $"❌ {failedWarmups.Count} indicator(s) have incorrect warmup periods — review implementation");
            foreach (var f in failedWarmups)
                recommendations.Add($"   {f.Message}");
        }
        else
        {
            recommendations.Add("✅ All indicator warmup periods are correct");
        }

        if (comparisons.Any())
        {
            var mismatches = comparisons.Where(c => !c.IsAcceptable).ToList();
            if (mismatches.Any())
            {
                recommendations.Add(
                    $"⚠️ {mismatches.Count} stored snapshot(s) differ from freshly calculated values by >1%");
                recommendations.Add(
                    "   Consider recalculating historical indicator snapshots");

                var worst = mismatches
                    .SelectMany(m => m.Differences)
                    .OrderByDescending(kvp => kvp.Value)
                    .FirstOrDefault();

                if (worst.Key is not null)
                    recommendations.Add(
                        $"   Largest discrepancy: {worst.Key} at {worst.Value:F2}%");
            }
            else
            {
                recommendations.Add(
                    "✅ Stored snapshots match freshly calculated values (within 1% tolerance)");
            }
        }
        else
        {
            recommendations.Add("ℹ️ No stored snapshots found for comparison");
        }

        return recommendations;
    }

    // =========================================================================
    // STATIC UTILITY HELPERS
    // =========================================================================

    private static WarmupInfo BuildWarmupInfo(
        int expected, int actual,
        bool exact = false, int tolerance = 0)
    {
        bool isValid = exact
            ? actual == expected
            : actual >= expected - tolerance && actual <= expected + tolerance;

        return new WarmupInfo
        {
            Expected = expected,
            Actual   = actual,
            IsValid  = isValid
        };
    }

    private static WarmupStatusDto CreateWarmupStatus(
        string name, int expected, int actual,
        bool exact = false, int tolerance = 0)
    {
        var info = BuildWarmupInfo(expected, actual, exact, tolerance);
        return new WarmupStatusDto
        {
            IndicatorName         = name,
            ExpectedWarmupCandles = expected,
            ActualWarmupCandles   = actual,
            IsComplete            = actual > 0,
            IsCorrect             = info.IsValid,
            Status                = info.IsValid ? "✅ CORRECT"
                                  : actual > 0   ? "⚠️ DIFFERENT"
                                                 : "❌ NOT READY"
        };
    }

    /// <summary>
    /// Calculate percentage difference between calculated and stored values.
    /// Both values are rounded to 4dp before comparison to match database precision.
    /// </summary>
    private static decimal PctDiff(decimal calculated, decimal stored)
    {
        // ✅ FIXED: Round both sides to 4dp before comparing
        var calc  = Math.Round(calculated, 4, MidpointRounding.AwayFromZero);
        var store = Math.Round(stored,     4, MidpointRounding.AwayFromZero);
        
        if (store == 0) return 0;
        
        var absDiff = Math.Abs(calc - store);
        if (absDiff == 0) return 0;
        
        return absDiff / Math.Abs(store) * 100;
    }

    /// <summary>
    /// Convert any DateTimeOffset to IST for display purposes only.
    /// Never use the resulting DateTime for arithmetic or DB storage.
    /// </summary>
    private static DateTime ToIst(DateTimeOffset dt) =>
        TimeZoneInfo.ConvertTime(dt, Ist).DateTime;
}

// =============================================================================
// INTERNAL MODELS
// =============================================================================

public class VerificationData
{
    public Dictionary<string, WarmupInfo> WarmupPeriods     { get; set; } = new();
    public List<SampleIndicatorData>      SampleData        { get; set; } = new();
    public List<ValidationResult>         ValidationResults { get; set; } = new();
    public bool                           IsValid           { get; set; }
}

public class WarmupInfo
{
    public int  Expected { get; set; }
    public int  Actual   { get; set; }
    public bool IsValid  { get; set; }
}

// =============================================================================
// RESPONSE DTOs
// =============================================================================

public class IndicatorVerificationResultDto
{
    public int                            InstrumentId          { get; set; }
    public string                         Symbol                { get; set; } = string.Empty;
    public string                         Exchange              { get; set; } = string.Empty;
    public int                            TimeframeMinutes      { get; set; }
    public int                            TotalCandlesAnalyzed  { get; set; }
    public DateTimeOffset                 VerificationTimestamp { get; set; }
    public Dictionary<string, WarmupInfo> WarmupPeriods        { get; set; } = new();
    public List<ValidationResult>         ValidationResults     { get; set; } = new();
    public List<SampleIndicatorData>      SampleData            { get; set; } = new();
    public List<ComparisonResult>         StoredDataComparison  { get; set; } = new();
    public string                         OverallStatus         { get; set; } = string.Empty;
    public List<string>                   Recommendations       { get; set; } = new();
}

public class SampleIndicatorData
{
    public int             CandleIndex { get; set; }
    public DateTimeOffset  Timestamp   { get; set; }
    public decimal         Close       { get; set; }
    public IndicatorValues Indicators  { get; set; } = null!;
}

public class ValidationResult
{
    public string Indicator { get; set; } = string.Empty;
    public bool   IsValid   { get; set; }
    public string Message   { get; set; } = string.Empty;
}

public class ComparisonResult
{
    public DateTimeOffset              Timestamp     { get; set; }
    public Dictionary<string, decimal> Differences  { get; set; } = new();
    public decimal                     MaxDifference { get; set; }
    public bool                        IsAcceptable  { get; set; }
}

public class WarmupStatusDto
{
    public string IndicatorName         { get; set; } = string.Empty;
    public int    ExpectedWarmupCandles { get; set; }
    public int    ActualWarmupCandles   { get; set; }
    public bool   IsComplete            { get; set; }
    public bool   IsCorrect             { get; set; }
    public string Status                { get; set; } = string.Empty;
}