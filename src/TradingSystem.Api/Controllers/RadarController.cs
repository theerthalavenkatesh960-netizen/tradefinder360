using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;
using System.Diagnostics;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/radar")]
public class RadarController : ControllerBase
{
    private readonly MarketScannerService _scanner;

    public RadarController(MarketScannerService scanner)
    {
        _scanner = scanner;
    }

    [HttpGet]
    public async Task<ActionResult<RadarResponseDto>> GetRadar(
        [FromQuery] int minScore = 0,
        [FromQuery] int timeframe = 15)
    {
        var results = await _scanner.ScanAllAsync(timeframe);

        var filtered = minScore > 0
            ? results.Where(r => r.SetupScore >= minScore).ToList()
            : results;

        var items = filtered.Select(r => new RadarItemDto
        {
            instrumentId = r.InstrumentId,
            Symbol = r.Symbol,
            Exchange = r.Exchange,
            MarketState = r.MarketState.ToString(),
            SetupScore = r.SetupScore,
            QualityLabel = r.QualityLabel,
            Bias = r.Bias.ToString(),
            LastClose = r.LastClose,
            ATR = r.ATR,
            LastUpdated = r.ScannedAt
        }).ToList();

        return Ok(new RadarResponseDto
        {
            Items = items,
            TotalScanned = results.Count,
            HighQuality = results.Count(r => r.SetupScore >= 70),
            Watchlist = results.Count(r => r.SetupScore >= 50 && r.SetupScore < 70),
            ScannedAt = DateTime.UtcNow
        });
    }

    [HttpGet("top")]
    public async Task<ActionResult<List<RadarItemDto>>> GetTopSetups(
        [FromQuery] int minScore = 70,
        [FromQuery] int limit = 10)
    {
        var results = await _scanner.GetTopSetups(minScore, limit);

        var items = results.Select(r => new RadarItemDto
        {
            instrumentId = r.InstrumentId,
            Symbol = r.Symbol,
            Exchange = r.Exchange,
            MarketState = r.MarketState.ToString(),
            SetupScore = r.SetupScore,
            QualityLabel = r.QualityLabel,
            Bias = r.Bias.ToString(),
            LastClose = r.LastClose,
            ATR = r.ATR,
            LastUpdated = r.ScannedAt
        }).ToList();

        return Ok(items);
    }

    /// <summary>
    /// Returns all intraday section data in a single parallel call for fast Radar dashboard hydration.
    /// Sections: TopGainers, TopLosers, SectorLeaders, Breakouts30Min, NearSupport, NearResistance, Patterns.
    /// Optimized with batch candle loading to avoid N+1 queries.
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<RadarSectionsDto>> GetSections(
        [FromQuery] int timeframe = 15,
        [FromQuery] int sectionLimit = 10,
        [FromQuery] decimal srThresholdPct = 1.5m)
    {
        var overallTimer = Stopwatch.StartNew();
        
        // Scan all instruments once
        var scanTimer = Stopwatch.StartNew();
        var scanResults = await _scanner.ScanAllAsync(timeframe);
        scanTimer.Stop();
        System.Diagnostics.Debug.WriteLine($"[Radar] ScanAllAsync completed in {scanTimer.ElapsedMilliseconds}ms");

        // Batch-load daily candles for all instruments upfront (5 days for trend context)
        var batchTimer = Stopwatch.StartNew();
        var dailyCandlesByInstrument = await _scanner.BatchGetDailyCandles(scanResults, daysBack: 5);
        batchTimer.Stop();
        System.Diagnostics.Debug.WriteLine($"[Radar] BatchGetDailyCandles completed in {batchTimer.ElapsedMilliseconds}ms for {dailyCandlesByInstrument.Count} instruments");

        // Execute all section queries in parallel with pre-loaded candles
        var gainersTimer = Stopwatch.StartNew();
        var gainersTask = _scanner.GetMoversAsync(scanResults, timeframe, sectionLimit, gainers: true);
        gainersTimer.Stop();

        var losersTimer = Stopwatch.StartNew();
        var losersTask = _scanner.GetMoversAsync(scanResults, timeframe, sectionLimit, gainers: false);
        losersTimer.Stop();

        var sectorsTimer = Stopwatch.StartNew();
        var sectorsTask = _scanner.GetSectorLeadersAsync(scanResults, perSector: 3);
        sectorsTimer.Stop();

        var breakoutsTimer = Stopwatch.StartNew();
        var breakoutsTask = _scanner.GetBreakoutsAsync(scanResults, sectionLimit);
        breakoutsTimer.Stop();

        var srTimer = Stopwatch.StartNew();
        var srTask = _scanner.GetNearSRAsync(scanResults, timeframe, srThresholdPct, sectionLimit * 2);
        srTimer.Stop();

        var patternsTimer = Stopwatch.StartNew();
        var patternsTask = _scanner.GetPatternsAsync(scanResults, timeframe, sectionLimit);
        patternsTimer.Stop();

        // Wait for all queries
        await Task.WhenAll(gainersTask, losersTask, sectorsTask, breakoutsTask, srTask, patternsTask);

        var gainers = await gainersTask;
        var losers = await losersTask;
        var sectors = await sectorsTask;
        var breakouts = await breakoutsTask;
        var sr = await srTask;
        var patterns = await patternsTask;

        System.Diagnostics.Debug.WriteLine($"[Radar] Gainers: {gainersTimer.ElapsedMilliseconds}ms, Losers: {losersTimer.ElapsedMilliseconds}ms");
        System.Diagnostics.Debug.WriteLine($"[Radar] Sectors: {sectorsTimer.ElapsedMilliseconds}ms, Breakouts: {breakoutsTimer.ElapsedMilliseconds}ms");
        System.Diagnostics.Debug.WriteLine($"[Radar] SR: {srTimer.ElapsedMilliseconds}ms, Patterns: {patternsTimer.ElapsedMilliseconds}ms");

        // Map results to DTOs with trend candles for gainers/losers
        var moverToDtoFunc = new Func<(ScanResult, decimal), MoverItemDto>(mover =>
        {
            dailyCandlesByInstrument.TryGetValue(mover.Item1.InstrumentId, out var trendCandles);
            return new MoverItemDto
            {
                Symbol = mover.Item1.Symbol,
                Exchange = mover.Item1.Exchange,
                LastClose = mover.Item1.LastClose,
                ChangePercent = Math.Round(mover.Item2, 2),
                ATR = mover.Item1.ATR,
                Bias = mover.Item1.Bias.ToString(),
                SetupScore = mover.Item1.SetupScore,
                ScannedAt = mover.Item1.ScannedAt,
                TrendCandles = trendCandles?.Select(c => new CandleDto
                {
                    Timestamp = c.Timestamp,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume
                }).ToList() ?? new(),
                AIAnalysis = "Ready" // Placeholder for AI insights
            };
        });

        overallTimer.Stop();
        System.Diagnostics.Debug.WriteLine($"[Radar] Total GetSections completed in {overallTimer.ElapsedMilliseconds}ms");

        return Ok(new RadarSectionsDto
        {
            TopGainers = gainers.Select(moverToDtoFunc).ToList(),
            TopLosers = losers.Select(moverToDtoFunc).ToList(),

            SectorLeaders = sectors.Take(sectionLimit).Select(r => new SectorLeaderItemDto
            {
                Symbol = r.Symbol,
                Exchange = r.Exchange,
                LastClose = r.LastClose,
                ChangePercent = 0m,
                SetupScore = r.SetupScore,
                Bias = r.Bias.ToString(),
                ScannedAt = r.ScannedAt
            }).ToList(),

            Breakouts30Min = breakouts.Select(b => new BreakoutItemDto
            {
                Symbol = b.Result.Symbol,
                Exchange = b.Result.Exchange,
                LastClose = b.Result.LastClose,
                OpenRangeHigh = b.OrHigh,
                OpenRangeLow = b.OrLow,
                BreakoutPercent = Math.Round(b.BreakoutPct, 2),
                Direction = b.Direction,
                SetupScore = b.Result.SetupScore,
                ScannedAt = b.Result.ScannedAt
            }).ToList(),

            NearSupport = sr.Where(x => x.LevelType == "SUPPORT").Take(sectionLimit).Select(x => new SRProximityItemDto
            {
                Symbol = x.Result.Symbol,
                Exchange = x.Result.Exchange,
                LastClose = x.Result.LastClose,
                Level = x.Level,
                DistancePercent = Math.Round(x.DistancePct, 2),
                LevelType = x.LevelType,
                Bias = x.Result.Bias.ToString(),
                SetupScore = x.Result.SetupScore,
                ScannedAt = x.Result.ScannedAt
            }).ToList(),

            NearResistance = sr.Where(x => x.LevelType == "RESISTANCE").Take(sectionLimit).Select(x => new SRProximityItemDto
            {
                Symbol = x.Result.Symbol,
                Exchange = x.Result.Exchange,
                LastClose = x.Result.LastClose,
                Level = x.Level,
                DistancePercent = Math.Round(x.DistancePct, 2),
                LevelType = x.LevelType,
                Bias = x.Result.Bias.ToString(),
                SetupScore = x.Result.SetupScore,
                ScannedAt = x.Result.ScannedAt
            }).ToList(),

            Patterns = patterns.Select(p => new PatternItemDto
            {
                Symbol = p.Result.Symbol,
                Exchange = p.Result.Exchange,
                LastClose = p.Result.LastClose,
                PatternName = p.PatternName,
                PatternDirection = p.Direction,
                Confidence = p.Confidence,
                SetupScore = p.Result.SetupScore,
                ScannedAt = p.Result.ScannedAt
            }).ToList(),

            GeneratedAt = DateTime.UtcNow
        });
    }
}
