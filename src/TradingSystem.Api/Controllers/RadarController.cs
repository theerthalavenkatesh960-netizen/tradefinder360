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
}
