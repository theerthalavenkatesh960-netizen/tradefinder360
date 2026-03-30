using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Services;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/backtest")]
public class BacktestRunController : ControllerBase
{
    private static readonly HashSet<int> AllowedTimeframes = [1, 5, 15, 30];
    private static readonly HashSet<string> AllowedStrategies = ["ORB", "RSI_REVERSAL", "EMA_CROSSOVER", "EMA_PULLBACK"];

    private readonly BacktestRunnerService _backtestService;
    private readonly ILogger<BacktestRunController> _logger;

    public BacktestRunController(
        BacktestRunnerService backtestService,
        ILogger<BacktestRunController> logger)
    {
        _backtestService = backtestService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(BacktestResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    [ProducesResponseType(500)]
    public async Task<ActionResult<BacktestResponse>> RunBacktest([FromBody] BacktestRunRequest request)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return BadRequest("Symbol must not be null or empty");

        if (request.From >= request.To)
            return BadRequest("From date must be before To date");

        if ((request.To - request.From).TotalDays > 365)
            return BadRequest("Date range must not exceed 365 days");

        if (!AllowedTimeframes.Contains(request.Strategy.Params.Timeframe))
            return BadRequest("Timeframe must be one of: 1, 5, 15, 30");

        var strategyName = request.Strategy.Name?.ToUpperInvariant() ?? "";
        if (!AllowedStrategies.Contains(strategyName))
            return BadRequest("Strategy name must be one of: ORB, RSI_REVERSAL, EMA_CROSSOVER, EMA_PULLBACK");

        if (request.Strategy.Params.RiskPercent < 0.1 || request.Strategy.Params.RiskPercent > 10)
            return BadRequest("RiskPercent must be between 0.1 and 10");

        if (request.Strategy.Params.TargetType.Equals("RR_RATIO", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Strategy.Params.RrRatio.HasValue || request.Strategy.Params.RrRatio <= 0)
                return BadRequest("RrRatio must be provided and greater than 0 when TargetType is RR_RATIO");
        }

        if (request.Strategy.Params.StopLossType.Equals("FIXED_PERCENT", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.Strategy.Params.SlPercent.HasValue || request.Strategy.Params.SlPercent <= 0)
                return BadRequest("SlPercent must be provided and greater than 0 when StopLossType is FIXED_PERCENT");
        }

        try
        {
            var result = await _backtestService.RunAsync(request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running backtest for {Symbol} with strategy {Strategy}",
                request.Symbol, request.Strategy.Name);
            return StatusCode(500, "An error occurred while running the backtest");
        }
    }
}
