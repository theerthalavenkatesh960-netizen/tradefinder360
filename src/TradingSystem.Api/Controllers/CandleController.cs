using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Helpers;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/candles")]
public class CandleController : ControllerBase
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private readonly IInstrumentService _instrumentService;
    private readonly ICandleService _candleService;
    private readonly ILogger<CandleController> _logger;

    public CandleController(
        IInstrumentService instrumentService,
        ICandleService candleService,
        ILogger<CandleController> logger)
    {
        _instrumentService = instrumentService;
        _candleService = candleService;
        _logger = logger;
    }

    /// <summary>
    /// Get candles for a specific instrument by symbol
    /// </summary>
    /// <param name="symbol">Instrument symbol (e.g., RELIANCE, INFY)</param>
    /// <param name="timeframe">Timeframe in minutes (1, 5, 15, 30, 60, etc.)</param>
    /// <param name="daysBack">Number of days to look back (default: auto-determined based on instrument type and timeframe)</param>
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(CandleResponseDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<CandleResponseDto>> GetCandlesBySymbol(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int daysBack = 0)
    {
        if (timeframe <= 0)
        {
            return BadRequest("Timeframe must be greater than 0");
        }

        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
        {
            return NotFound($"Instrument with symbol '{symbol}' not found");
        }

        var maxDays = CandleDataLimits.GetMaxDaysBack(instrument.InstrumentType, timeframe);

        if (daysBack <= 0)
        {
            daysBack = maxDays;
        }
        else if (daysBack > maxDays)
        {
            return BadRequest($"Days back must be between 1 and {maxDays} for {instrument.InstrumentType} with {timeframe}-minute timeframe");
        }

        var candles = await _candleService.GetRecentCandlesAsync(
            instrument.Id,
            timeframe,
            daysBack);

        var response = new CandleResponseDto
        {
            InstrumentKey = instrument.InstrumentKey,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            Timeframe = timeframe,
            FromDate = DateTime.Today.AddDays(-daysBack),
            ToDate = DateTime.Today,
            Count = candles.Count,
            Candles = candles.Select(c => ToCandleDto(c)).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Get candles for a specific date range
    /// </summary>
    /// <param name="symbol">Instrument symbol</param>
    /// <param name="timeframe">Timeframe in minutes</param>
    /// <param name="fromDate">Start date (YYYY-MM-DD)</param>
    /// <param name="toDate">End date (YYYY-MM-DD)</param>
    [HttpGet("{symbol}/range")]
    [ProducesResponseType(typeof(CandleResponseDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<CandleResponseDto>> GetCandlesByDateRange(
        string symbol,
        [FromQuery] int timeframe,
        [FromQuery] DateTime fromDate,
        [FromQuery] DateTime toDate)
    {
        if (timeframe <= 0)
        {
            return BadRequest("Timeframe must be greater than 0");
        }

        if (fromDate >= toDate)
        {
            return BadRequest("fromDate must be before toDate");
        }

        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
        {
            return NotFound($"Instrument with symbol '{symbol}' not found");
        }

        var maxDays = CandleDataLimits.GetMaxDaysBack(instrument.InstrumentType, timeframe);

        if ((toDate - fromDate).TotalDays > maxDays)
        {
            return BadRequest($"Date range cannot exceed {maxDays} days for {instrument.InstrumentType} with {timeframe}-minute timeframe");
        }

        var candles = await _candleService.GetCandlesAsync(
            instrument.Id,
            timeframe,
            fromDate,
            toDate);

        var response = new CandleResponseDto
        {
            InstrumentKey = instrument.InstrumentKey,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            Timeframe = timeframe,
            FromDate = fromDate,
            ToDate = toDate,
            Count = candles.Count,
            Candles = candles.Select(c => ToCandleDto(c)).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Get the latest candle for an instrument
    /// </summary>
    /// <param name="symbol">Instrument symbol</param>
    /// <param name="timeframe">Timeframe in minutes (default: 15)</param>
    [HttpGet("{symbol}/latest")]
    [ProducesResponseType(typeof(LatestCandleResponseDto), 200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<LatestCandleResponseDto>> GetLatestCandle(
        string symbol,
        [FromQuery] int timeframe = 15)
    {
        if (timeframe <= 0)
        {
            return BadRequest("Timeframe must be greater than 0");
        }

        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
        {
            return NotFound($"Instrument with symbol '{symbol}' not found");
        }

        var candle = await _candleService.GetLatestCandleAsync(instrument.Id, timeframe);
        if (candle == null)
        {
            return NotFound($"No candle data found for '{symbol}' with {timeframe}-minute timeframe");
        }

        var response = new LatestCandleResponseDto
        {
            InstrumentKey = instrument.InstrumentKey,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            Timeframe = timeframe,
            Candle = ToCandleDto(candle)
        };

        return Ok(response);
    }

    /// <summary>
    /// Get available candle timeframes and market info
    /// </summary>
    [HttpGet("info")]
    [ProducesResponseType(typeof(CandleInfoDto), 200)]
    public ActionResult<CandleInfoDto> GetCandleInfo()
    {
        var info = new CandleInfoDto
        {
            AvailableTimeframes = new List<TimeframeInfoDto>
            {
                new() { Minutes = 1, Description = "1 Minute", CandlesPerDay = 375 },
                new() { Minutes = 3, Description = "3 Minutes", CandlesPerDay = 125 },
                new() { Minutes = 5, Description = "5 Minutes", CandlesPerDay = 75 },
                new() { Minutes = 15, Description = "15 Minutes", CandlesPerDay = 25 },
                new() { Minutes = 30, Description = "30 Minutes", CandlesPerDay = 13 },
                new() { Minutes = 60, Description = "1 Hour", CandlesPerDay = 6 },
                new() { Minutes = 375, Description = "1 Day", CandlesPerDay = 1 }
            },
            MarketHours = new MarketHoursDto
            {
                OpenTime = "09:15",
                CloseTime = "15:30",
                Timezone = "IST (India Standard Time)",
                TradingMinutesPerDay = 375
            }
        };

        return Ok(info);
    }

    /// <summary>
    /// Converts a Candle to CandleDto, converting the UTC timestamp back to IST.
    /// EF/Npgsql normalizes TIMESTAMPTZ to UTC on read, so a 09:15 IST candle
    /// arrives as 03:45 UTC. This restores the original IST time for the UI.
    /// </summary>
    private static CandleDto ToCandleDto(Candle c) => new()
    {
        Timestamp = TimeZoneInfo.ConvertTime(c.Timestamp, Ist),
        Open = c.Open,
        High = c.High,
        Low = c.Low,
        Close = c.Close,
        Volume = c.Volume
    };
}