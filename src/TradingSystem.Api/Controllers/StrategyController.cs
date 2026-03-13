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
    private readonly BacktestingService _backtestingService;
    private readonly ILogger<StrategyController> _logger;

    public StrategyController(
        StrategyService strategyService,
        BacktestingService backtestingService,
        ILogger<StrategyController> logger)
    {
        _strategyService = strategyService;
        _backtestingService = backtestingService;
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

    /// <summary>
    /// Run a backtest for a specific strategy on historical data
    /// </summary>
    /// <param name="request">Backtest configuration</param>
    /// <param name="includeTradeHistory">Include individual trade details (default: false)</param>
    /// <param name="includeEquityCurve">Include equity curve data (default: false)</param>
    [HttpPost("backtest")]
    [ProducesResponseType(typeof(BacktestResultDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<BacktestResultDto>> RunBacktest(
        [FromBody] BacktestRequest request,
        [FromQuery] bool includeTradeHistory = false,
        [FromQuery] bool includeEquityCurve = false)
    {
        // Validate request
        if (!Enum.TryParse<StrategyType>(request.Strategy, true, out var strategyType))
        {
            return BadRequest($"Invalid strategy: {request.Strategy}. Valid values: MOMENTUM, BREAKOUT, MEAN_REVERSION, SWING_TRADING");
        }

        if (request.StartDate >= request.EndDate)
        {
            return BadRequest("Start date must be before end date");
        }

        if (request.InitialCapital <= 0)
        {
            return BadRequest("Initial capital must be positive");
        }

        if (request.PositionSizePercent <= 0 || request.PositionSizePercent > 100)
        {
            return BadRequest("Position size must be between 0 and 100");
        }

        try
        {
            var config = new BacktestConfig
            {
                Strategy = strategyType,
                InstrumentId = request.InstrumentId,
                TimeframeMinutes = request.TimeframeMinutes,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                InitialCapital = request.InitialCapital,
                PositionSizePercent = request.PositionSizePercent,
                CommissionPercent = request.CommissionPercent,
                UseStopLoss = request.UseStopLoss,
                UseTarget = request.UseTarget
            };

            var result = await _backtestingService.RunBacktestAsync(config);

            var dto = new BacktestResultDto
            {
                Strategy = result.Strategy.ToString(),
                Symbol = result.Symbol,
                TimeframeMinutes = result.TimeframeMinutes,
                StartDate = result.StartDate,
                EndDate = result.EndDate,
                TotalBars = result.TotalBars,
                InitialCapital = result.InitialCapital,
                FinalCapital = result.FinalCapital,
                TotalTrades = result.TotalTrades,
                WinningTrades = result.WinningTrades,
                LosingTrades = result.LosingTrades,
                WinRate = result.WinRate,
                AverageReturn = result.AverageReturn,
                MaxDrawdown = result.MaxDrawdown,
                MaxDrawdownPercent = result.MaxDrawdownPercent,
                ProfitFactor = result.ProfitFactor,
                TotalReturn = result.TotalReturn,
                TotalReturnPercent = result.TotalReturnPercent,
                AverageReturnPercent = result.AverageReturnPercent,
                AverageWinPercent = result.AverageWinPercent,
                AverageLossPercent = result.AverageLossPercent,
                SharpeRatio = result.SharpeRatio,
                SortinoRatio = result.SortinoRatio,
                LargestWin = result.LargestWin,
                LargestLoss = result.LargestLoss,
                AverageBarsHeld = result.AverageBarsHeld,
                TotalCommission = result.TotalCommission,
                ConsecutiveWins = result.ConsecutiveWins,
                ConsecutiveLosses = result.ConsecutiveLosses,
                MaxDrawdownDate = result.MaxDrawdownDate
            };

            // Optionally include detailed trade history
            if (includeTradeHistory)
            {
                dto.Trades = result.Trades.Select(t => new BacktestTradeDto
                {
                    TradeNumber = t.TradeNumber,
                    EntryTime = t.EntryTime,
                    ExitTime = t.ExitTime,
                    Direction = t.Direction,
                    EntryPrice = t.EntryPrice,
                    ExitPrice = t.ExitPrice,
                    StopLoss = t.StopLoss,
                    Target = t.Target,
                    Quantity = t.Quantity,
                    PnL = t.PnL,
                    PnLPercent = t.PnLPercent,
                    ExitReason = t.ExitReason,
                    Commission = t.Commission,
                    BarsHeld = t.BarsHeld
                }).ToList();
            }

            // Optionally include equity curve
            if (includeEquityCurve)
            {
                dto.EquityCurve = result.EquityCurve;
            }

            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running backtest for {Strategy} on instrument {InstrumentId}",
                request.Strategy, request.InstrumentId);
            return StatusCode(500, "Error running backtest");
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