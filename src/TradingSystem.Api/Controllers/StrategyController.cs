using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Scanner.Services;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/strategies")]
public class StrategyController : ControllerBase
{
    private readonly StrategyService _strategyService;
    private readonly ILogger<StrategyController> _logger;

    public StrategyController(
        StrategyService strategyService,
        ILogger<StrategyController> logger)
    {
        _strategyService = strategyService;
        _logger = logger;
    }

    /// <summary>
    /// Get all available trading strategies
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<StrategyDto>), 200)]
    public ActionResult<List<StrategyDto>> GetStrategies()
    {
        var strategies = _strategyService.GetAvailableStrategies();

        var dtos = strategies.Select(s => new StrategyDto
        {
            Type = s.StrategyType.ToString(),
            Name = s.Name,
            Description = s.Description,
            IsActive = s.IsActive
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Generate trade recommendations for a specific strategy
    /// </summary>
    /// <param name="strategy">Strategy type: MOMENTUM, BREAKOUT, MEAN_REVERSION, SWING_TRADING</param>
    /// <param name="timeframe">Timeframe in minutes (default: 15)</param>
    /// <param name="minConfidence">Minimum confidence percentage (default: 60)</param>
    /// <param name="topCount">Number of recommendations to return (default: 10)</param>
    [HttpPost("recommendations")]
    [ProducesResponseType(typeof(List<StrategyRecommendationDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<List<StrategyRecommendationDto>>> GenerateRecommendations(
        [FromQuery] string strategy,
        [FromQuery] int timeframe = 15,
        [FromQuery] int minConfidence = 60,
        [FromQuery] int topCount = 10)
    {
        if (!Enum.TryParse<StrategyType>(strategy, true, out var strategyType))
        {
            return BadRequest($"Invalid strategy: {strategy}. Valid values: MOMENTUM, BREAKOUT, MEAN_REVERSION, SWING_TRADING");
        }

        if (minConfidence < 0 || minConfidence > 100)
        {
            return BadRequest("Minimum confidence must be between 0 and 100");
        }

        if (topCount < 1 || topCount > 50)
        {
            return BadRequest("Top count must be between 1 and 50");
        }

        try
        {
            var recommendations = await _strategyService.GenerateRecommendationsAsync(
                strategyType,
                timeframe,
                minConfidence,
                topCount);

            var dtos = recommendations.Select(r => new StrategyRecommendationDto
            {
                InstrumentId = r.Instrument.Id,
                Symbol = r.Instrument.Symbol,
                Exchange = r.Instrument.Exchange,
                Signal = MapSignalToDto(r.PrimarySignal),
                AlternativeSignals = r.AlternativeSignals?.Select(MapSignalToDto).ToList(),
                GeneratedAt = r.GeneratedAt,
                ExpiresAt = r.ExpiresAt
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating recommendations for {Strategy}", strategy);
            return StatusCode(500, "Error generating recommendations");
        }
    }

    /// <summary>
    /// Evaluate all strategies for a specific instrument
    /// </summary>
    /// <param name="symbol">Instrument symbol</param>
    /// <param name="timeframe">Timeframe in minutes (default: 15)</param>
    [HttpGet("{symbol}/evaluate")]
    [ProducesResponseType(typeof(List<StrategySignalDto>), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<List<StrategySignalDto>>> EvaluateStrategies(
        string symbol,
        [FromQuery] int timeframe = 15)
    {
        try
        {
            var signals = await _strategyService.EvaluateAllStrategiesAsync(symbol, timeframe);

            var dtos = signals.Select(MapSignalToDto).ToList();

            return Ok(dtos);
        }
        catch (ArgumentException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating strategies for {Symbol}", symbol);
            return StatusCode(500, "Error evaluating strategies");
        }
    }

    private static StrategySignalDto MapSignalToDto(StrategySignal signal)
    {
        return new StrategySignalDto
        {
            Strategy = signal.Strategy.ToString(),
            IsValid = signal.IsValid,
            Score = signal.Score,
            Direction = signal.Direction,
            EntryPrice = signal.EntryPrice,
            StopLoss = signal.StopLoss,
            Target = signal.Target,
            Confidence = signal.Confidence,
            Signals = signal.Signals,
            Metrics = signal.Metrics,
            Explanation = signal.Explanation
        };
    }
}