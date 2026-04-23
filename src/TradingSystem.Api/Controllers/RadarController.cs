using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;

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
    /// </summary>
    [HttpGet("sections")]
    public async Task<ActionResult<RadarSectionsDto>> GetSections(
        [FromQuery] int timeframe = 15,
        [FromQuery] int sectionLimit = 10,
        [FromQuery] decimal srThresholdPct = 1.5m)
    {
        // Run all sections in parallel — each uses the bounded parallel scanner internally.
        var gainersTask = _scanner.GetMoversAsync(timeframe, sectionLimit, gainers: true);
        var losersTask = _scanner.GetMoversAsync(timeframe, sectionLimit, gainers: false);
        var sectorsTask = _scanner.GetSectorLeadersAsync(timeframe, perSector: 3);
        var breakoutsTask = _scanner.GetBreakoutsAsync(sectionLimit);
        var srTask = _scanner.GetNearSRAsync(timeframe, srThresholdPct, sectionLimit * 2);
        var patternsTask = _scanner.GetPatternsAsync(timeframe, sectionLimit);

        await Task.WhenAll(gainersTask, losersTask, sectorsTask, breakoutsTask, srTask, patternsTask);

        var gainers = await gainersTask;
        var losers = await losersTask;
        var sectors = await sectorsTask;
        var breakouts = await breakoutsTask;
        var sr = await srTask;
        var patterns = await patternsTask;

        return Ok(new RadarSectionsDto
        {
            TopGainers = gainers.Select(x => new MoverItemDto
            {
                Symbol = x.Result.Symbol,
                Exchange = x.Result.Exchange,
                LastClose = x.Result.LastClose,
                ChangePercent = Math.Round(x.ChangePercent, 2),
                ATR = x.Result.ATR,
                Bias = x.Result.Bias.ToString(),
                SetupScore = x.Result.SetupScore,
                ScannedAt = x.Result.ScannedAt
            }).ToList(),

            TopLosers = losers.Select(x => new MoverItemDto
            {
                Symbol = x.Result.Symbol,
                Exchange = x.Result.Exchange,
                LastClose = x.Result.LastClose,
                ChangePercent = Math.Round(x.ChangePercent, 2),
                ATR = x.Result.ATR,
                Bias = x.Result.Bias.ToString(),
                SetupScore = x.Result.SetupScore,
                ScannedAt = x.Result.ScannedAt
            }).ToList(),

            SectorLeaders = sectors.Take(sectionLimit).Select(r => new SectorLeaderItemDto
            {
                Symbol = r.Symbol,
                Exchange = r.Exchange,
                LastClose = r.LastClose,
                ChangePercent = 0m, // populated via movers if needed; sector leaders ranked by score
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
