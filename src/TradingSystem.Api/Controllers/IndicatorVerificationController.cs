using Microsoft.AspNetCore.Mvc;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Controllers;

/// <summary>
/// Diagnostic API for verifying indicator calculation correctness
/// Use this to validate that indicators are calculating according to industry standards
/// </summary>
[ApiController]
[Route("api/verify")]
public class IndicatorVerificationController : ControllerBase
{
    private readonly ICandleService _candleService;
    private readonly IIndicatorService _indicatorService;
    private readonly IInstrumentRepository _instrumentRepository;
    private readonly ILogger<IndicatorVerificationController> _logger;

    public IndicatorVerificationController(
        ICandleService candleService,
        IIndicatorService indicatorService,
        IInstrumentRepository instrumentRepository,
        ILogger<IndicatorVerificationController> logger)
    {
        _candleService = candleService;
        _indicatorService = indicatorService;
        _instrumentRepository = instrumentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Verify indicator calculations for a specific instrument
    /// GET /api/verify/indicators/{symbol}?timeframe=15&candles=100
    /// </summary>
    [HttpGet("indicators/{symbol}")]
    [ProducesResponseType(typeof(IndicatorVerificationResultDto), 200)]
    public async Task<ActionResult<IndicatorVerificationResultDto>> VerifyIndicators(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int candles = 100)
    {
        try
        {
            // Find instrument
            var instruments = await _instrumentRepository.GetActiveInstrumentsAsync();
            var instrument = instruments.FirstOrDefault(i => 
                i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

            if (instrument == null)
                return NotFound(new { error = $"Instrument {symbol} not found" });

            // Fetch candle data
            var candleData = await _candleService.GetCandlesAsync(
                instrument.Id,
                timeframe,
                DateTime.UtcNow.AddDays(-90),
                DateTime.UtcNow.AddDays(1));

            if (candleData.Count < 100)
            {
                return BadRequest(new 
                { 
                    error = "Insufficient candle data", 
                    available = candleData.Count,
                    required = 100 
                });
            }

            var orderedCandles = candleData
                .OrderBy(c => c.Timestamp)
                .TakeLast(candles)
                .ToList();

            // Create fresh indicator engine
            var engine = new IndicatorEngine(
                emaFastPeriod: 20,
                emaSlowPeriod: 50,
                rsiPeriod: 14,
                macdFast: 12,
                macdSlow: 26,
                macdSignal: 9,
                adxPeriod: 14,
                atrPeriod: 14,
                bollingerPeriod: 20,
                bollingerStdDev: 2.0m
            );

            var verification = PerformVerification(orderedCandles, engine);

            // Fetch stored snapshots for comparison
            var storedSnapshots = await _indicatorService.GetRecentAsync(
                instrument.Id, 
                timeframe, 
                20);

            var comparisonResults = CompareWithStoredData(
                orderedCandles.TakeLast(20).ToList(),
                storedSnapshots,
                engine);

            var result = new IndicatorVerificationResultDto
            {
                InstrumentId = instrument.Id,
                Symbol = symbol,
                Exchange = instrument.Exchange,
                TimeframeMinutes = timeframe,
                TotalCandlesAnalyzed = candles,
                VerificationTimestamp = DateTimeOffset.UtcNow,
                WarmupPeriods = verification.WarmupPeriods,
                ValidationResults = verification.ValidationResults,
                SampleData = verification.SampleData.TakeLast(20).ToList(),
                StoredDataComparison = comparisonResults,
                OverallStatus = verification.IsValid ? "PASS" : "FAIL",
                Recommendations = GenerateRecommendations(verification, comparisonResults)
            };

            _logger.LogInformation(
                "Indicator verification for {Symbol}: {Status} - {Issues} issues found",
                symbol, result.OverallStatus, result.ValidationResults.Count(v => !v.IsValid));

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying indicators for {Symbol}", symbol);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Export indicator data as CSV for TradingView comparison
    /// GET /api/verify/indicators/{symbol}/export
    /// </summary>
    [HttpGet("indicators/{symbol}/export")]
    [Produces("text/csv")]
    public async Task<IActionResult> ExportIndicatorsCsv(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int candles = 100)
    {
        var instruments = await _instrumentRepository.GetActiveInstrumentsAsync();
        var instrument = instruments.FirstOrDefault(i => 
            i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (instrument == null)
            return NotFound();

        var candleData = await _candleService.GetCandlesAsync(
            instrument.Id,
            timeframe,
            DateTime.UtcNow.AddDays(-90),
            DateTime.UtcNow.AddDays(1));

        var ordered = candleData.OrderBy(c => c.Timestamp).TakeLast(candles).ToList();

        var engine = new IndicatorEngine(
            emaFastPeriod: 20,
            emaSlowPeriod: 50,
            rsiPeriod: 14,
            macdFast: 12,
            macdSlow: 26,
            macdSignal: 9,
            adxPeriod: 14,
            atrPeriod: 14,
            bollingerPeriod: 20,
            bollingerStdDev: 2.0m
        );

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Timestamp,Open,High,Low,Close,Volume,EMA20,EMA50,RSI,MACDLine,MACDSignal,MACDHistogram,ADX,PlusDI,MinusDI,ATR,BBUpper,BBMiddle,BBLower,VWAP");

        foreach (var candle in ordered)
        {
            var ind = engine.Calculate(candle);
            csv.AppendLine(
                $"{candle.Timestamp:yyyy-MM-dd HH:mm}," +
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
            $"indicators_{symbol}_{timeframe}m_{DateTime.UtcNow:yyyyMMddHHmm}.csv"
        );
    }

    /// <summary>
    /// Get warmup status for all indicators
    /// GET /api/verify/warmup/{symbol}
    /// </summary>
    [HttpGet("warmup/{symbol}")]
    public async Task<ActionResult> GetWarmupStatus(
        string symbol,
        [FromQuery] int timeframe = 15)
    {
        var instruments = await _instrumentRepository.GetActiveInstrumentsAsync();
        var instrument = instruments.FirstOrDefault(i => 
            i.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (instrument == null)
            return NotFound();

        var candleData = await _candleService.GetCandlesAsync(
            instrument.Id,
            timeframe,
            DateTime.UtcNow.AddDays(-30),
            DateTime.UtcNow.AddDays(1));

        if (!candleData.Any())
            return NotFound(new { error = "No candles found" });

        var ordered = candleData.OrderBy(c => c.Timestamp).ToList();

        var engine = new IndicatorEngine(
            emaFastPeriod: 20,
            emaSlowPeriod: 50,
            rsiPeriod: 14,
            macdFast: 12,
            macdSlow: 26,
            macdSignal: 9,
            adxPeriod: 14,
            atrPeriod: 14,
            bollingerPeriod: 20,
            bollingerStdDev: 2.0m
        );

        var warmupStatus = DetectWarmupPeriods(ordered, engine);

        return Ok(new
        {
            symbol,
            timeframeMinutes = timeframe,
            totalCandlesAvailable = ordered.Count,
            warmupComplete = warmupStatus.All(w => w.IsComplete),
            indicators = warmupStatus
        });
    }

    // ========== PRIVATE HELPER METHODS ==========

    private VerificationData PerformVerification(
        List<Candle> candles,
        IndicatorEngine engine)
    {
        var warmupPeriods = new Dictionary<string, WarmupInfo>();
        var sampleData = new List<SampleIndicatorData>();
        var validationResults = new List<ValidationResult>();

        int emaFastValid = -1, emaSlowValid = -1, rsiValid = -1;
        int macdSignalValid = -1, adxValid = -1, atrValid = -1, bbValid = -1;

        for (int i = 0; i < candles.Count; i++)
        {
            var indicators = engine.Calculate(candles[i]);

            // Detect when indicators become valid
            if (emaFastValid == -1 && indicators.EMAFast != 0) emaFastValid = i + 1;
            if (emaSlowValid == -1 && indicators.EMASlow != 0) emaSlowValid = i + 1;
            if (rsiValid == -1 && indicators.RSI != 0) rsiValid = i + 1;
            if (macdSignalValid == -1 && indicators.MacdSignal != 0) macdSignalValid = i + 1;
            if (adxValid == -1 && indicators.ADX != 0) adxValid = i + 1;
            if (atrValid == -1 && indicators.ATR != 0) atrValid = i + 1;
            if (bbValid == -1 && indicators.BollingerMiddle != 0) bbValid = i + 1;

            // Collect sample data (every 10 candles after warmup)
            if (i % 10 == 0 && i >= 50)
            {
                sampleData.Add(new SampleIndicatorData
                {
                    CandleIndex = i + 1,
                    Timestamp = candles[i].Timestamp,
                    Close = candles[i].Close,
                    Indicators = indicators
                });
            }
        }

        // Build warmup periods
        warmupPeriods["EMA(20)"] = new WarmupInfo 
        { 
            Expected = 20, 
            Actual = emaFastValid, 
            IsValid = emaFastValid == 20 
        };
        warmupPeriods["EMA(50)"] = new WarmupInfo 
        { 
            Expected = 50, 
            Actual = emaSlowValid, 
            IsValid = emaSlowValid == 50 
        };
        warmupPeriods["RSI(14)"] = new WarmupInfo 
        { 
            Expected = 15, 
            Actual = rsiValid, 
            IsValid = rsiValid >= 14 && rsiValid <= 16 
        };
        warmupPeriods["MACD Signal"] = new WarmupInfo 
        { 
            Expected = 35, 
            Actual = macdSignalValid, 
            IsValid = macdSignalValid >= 34 && macdSignalValid <= 36 
        };
        warmupPeriods["ADX(14)"] = new WarmupInfo 
        { 
            Expected = 28, 
            Actual = adxValid, 
            IsValid = adxValid >= 28 && adxValid <= 30 
        };
        warmupPeriods["ATR(14)"] = new WarmupInfo 
        { 
            Expected = 15, 
            Actual = atrValid, 
            IsValid = atrValid >= 14 && atrValid <= 16 
        };
        warmupPeriods["Bollinger(20)"] = new WarmupInfo 
        { 
            Expected = 20, 
            Actual = bbValid, 
            IsValid = bbValid == 20 
        };

        // Validation results
        foreach (var kvp in warmupPeriods)
        {
            validationResults.Add(new ValidationResult
            {
                Indicator = kvp.Key,
                IsValid = kvp.Value.IsValid,
                Message = kvp.Value.IsValid
                    ? $"✅ {kvp.Key} warmup correct (expected: {kvp.Value.Expected}, actual: {kvp.Value.Actual})"
                    : $"❌ {kvp.Key} warmup INCORRECT (expected: {kvp.Value.Expected}, actual: {kvp.Value.Actual})"
            });
        }

        return new VerificationData
        {
            WarmupPeriods = warmupPeriods,
            SampleData = sampleData,
            ValidationResults = validationResults,
            IsValid = validationResults.All(v => v.IsValid)
        };
    }

    private List<ComparisonResult> CompareWithStoredData(
        List<Candle> recentCandles,
        List<IndicatorSnapshot> storedSnapshots,
        IndicatorEngine engine)
    {
        var results = new List<ComparisonResult>();

        if (!storedSnapshots.Any())
            return results;

        var storedDict = storedSnapshots.ToDictionary(s => s.Timestamp);

        foreach (var candle in recentCandles)
        {
            var calculated = engine.Calculate(candle);

            if (storedDict.TryGetValue(candle.Timestamp, out var stored))
            {
                var comparison = new ComparisonResult
                {
                    Timestamp = candle.Timestamp,
                    Differences = new Dictionary<string, decimal>()
                };

                // Calculate percentage differences
                comparison.Differences["EMA20"] = CalculateDifference(calculated.EMAFast, stored.EMAFast);
                comparison.Differences["EMA50"] = CalculateDifference(calculated.EMASlow, stored.EMASlow);
                comparison.Differences["RSI"] = CalculateDifference(calculated.RSI, stored.RSI);
                comparison.Differences["ADX"] = CalculateDifference(calculated.ADX, stored.ADX);
                comparison.Differences["ATR"] = CalculateDifference(calculated.ATR, stored.ATR);
                comparison.Differences["BBMiddle"] = CalculateDifference(calculated.BollingerMiddle, stored.BollingerMiddle);

                comparison.MaxDifference = comparison.Differences.Values.Max();
                comparison.IsAcceptable = comparison.MaxDifference < 1.0m; // 1% tolerance

                results.Add(comparison);
            }
        }

        return results;
    }

    private decimal CalculateDifference(decimal calculated, decimal stored)
    {
        if (stored == 0) return 0;
        return Math.Abs((calculated - stored) / stored * 100);
    }

    private List<WarmupStatusDto> DetectWarmupPeriods(
        List<Candle> candles,
        IndicatorEngine engine)
    {
        var status = new List<WarmupStatusDto>();

        int emaFastValid = -1, emaSlowValid = -1, rsiValid = -1;
        int macdSignalValid = -1, adxValid = -1, atrValid = -1, bbValid = -1;

        for (int i = 0; i < candles.Count; i++)
        {
            var indicators = engine.Calculate(candles[i]);

            if (emaFastValid == -1 && indicators.EMAFast != 0) emaFastValid = i + 1;
            if (emaSlowValid == -1 && indicators.EMASlow != 0) emaSlowValid = i + 1;
            if (rsiValid == -1 && indicators.RSI != 0) rsiValid = i + 1;
            if (macdSignalValid == -1 && indicators.MacdSignal != 0) macdSignalValid = i + 1;
            if (adxValid == -1 && indicators.ADX != 0) adxValid = i + 1;
            if (atrValid == -1 && indicators.ATR != 0) atrValid = i + 1;
            if (bbValid == -1 && indicators.BollingerMiddle != 0) bbValid = i + 1;
        }

        status.Add(CreateWarmupStatus("EMA(20)", 20, emaFastValid));
        status.Add(CreateWarmupStatus("EMA(50)", 50, emaSlowValid));
        status.Add(CreateWarmupStatus("RSI(14)", 15, rsiValid));
        status.Add(CreateWarmupStatus("MACD Signal", 35, macdSignalValid));
        status.Add(CreateWarmupStatus("ADX(14)", 28, adxValid));
        status.Add(CreateWarmupStatus("ATR(14)", 15, atrValid));
        status.Add(CreateWarmupStatus("Bollinger(20)", 20, bbValid));

        return status;
    }

    private WarmupStatusDto CreateWarmupStatus(string name, int expected, int actual)
    {
        return new WarmupStatusDto
        {
            IndicatorName = name,
            ExpectedWarmupCandles = expected,
            ActualWarmupCandles = actual,
            IsComplete = actual > 0,
            IsCorrect = actual == expected,
            Status = actual == expected ? "✅ CORRECT" : actual > 0 ? "⚠️ DIFFERENT" : "❌ NOT READY"
        };
    }

    private List<string> GenerateRecommendations(
        VerificationData verification,
        List<ComparisonResult> comparisons)
    {
        var recommendations = new List<string>();

        var failedWarmups = verification.ValidationResults.Where(v => !v.IsValid).ToList();
        if (failedWarmups.Any())
        {
            recommendations.Add($"❌ {failedWarmups.Count} indicator(s) have incorrect warmup periods - review implementation");
            foreach (var failed in failedWarmups)
            {
                recommendations.Add($"  - {failed.Message}");
            }
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
                recommendations.Add($"⚠️ {mismatches.Count} stored snapshot(s) differ from freshly calculated values by >1%");
                recommendations.Add("  Consider recalculating historical indicator snapshots");
            }
            else
            {
                recommendations.Add("✅ Stored snapshots match freshly calculated values (within 1% tolerance)");
            }
        }

        return recommendations;
    }

    // ========== DTOs ==========

    private class VerificationData
    {
        public Dictionary<string, WarmupInfo> WarmupPeriods { get; set; } = new();
        public List<SampleIndicatorData> SampleData { get; set; } = new();
        public List<ValidationResult> ValidationResults { get; set; } = new();
        public bool IsValid { get; set; }
    }

    private class WarmupInfo
    {
        public int Expected { get; set; }
        public int Actual { get; set; }
        public bool IsValid { get; set; }
    }
}

// ========== RESPONSE DTOs ==========

public class IndicatorVerificationResultDto
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public int TimeframeMinutes { get; set; }
    public int TotalCandlesAnalyzed { get; set; }
    public DateTimeOffset VerificationTimestamp { get; set; }
    public Dictionary<string, WarmupInfo> WarmupPeriods { get; set; } = new();
    public List<ValidationResult> ValidationResults { get; set; } = new();
    public List<SampleIndicatorData> SampleData { get; set; } = new();
    public List<ComparisonResult> StoredDataComparison { get; set; } = new();
    public string OverallStatus { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = new();
}

public class SampleIndicatorData
{
    public int CandleIndex { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public decimal Close { get; set; }
    public IndicatorValues Indicators { get; set; } = null!;
}

public class ValidationResult
{
    public string Indicator { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ComparisonResult
{
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, decimal> Differences { get; set; } = new(); // % difference
    public decimal MaxDifference { get; set; }
    public bool IsAcceptable { get; set; }
}

public class WarmupStatusDto
{
    public string IndicatorName { get; set; } = string.Empty;
    public int ExpectedWarmupCandles { get; set; }
    public int ActualWarmupCandles { get; set; }
    public bool IsComplete { get; set; }
    public bool IsCorrect { get; set; }
    public string Status { get; set; } = string.Empty;
}