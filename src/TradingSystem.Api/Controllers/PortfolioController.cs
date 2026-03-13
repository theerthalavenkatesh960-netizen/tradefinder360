using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Scanner.Services;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/portfolio")]
public class PortfolioController : ControllerBase
{
    private readonly PortfolioOptimizationService _portfolioService;
    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(
        PortfolioOptimizationService portfolioService,
        ILogger<PortfolioController> logger)
    {
        _portfolioService = portfolioService;
        _logger = logger;
    }

    /// <summary>
    /// Generate optimized portfolio with capital allocation across multiple trades
    /// </summary>
    /// <remarks>
    /// Optimizes portfolio allocation with constraints:
    /// - Maximum risk per trade (default: 2%)
    /// - Maximum total portfolio risk (default: 6%)
    /// - Sector diversification limits (default: 30% per sector)
    /// - Minimum position size (default: 5%)
    /// 
    /// Example request:
    /// ```json
    /// {
    ///   "totalCapital": 1000000,
    ///   "maxRiskPerTradePercent": 2.0,
    ///   "maxPortfolioRiskPercent": 6.0,
    ///   "maxPositions": 10,
    ///   "enableSectorDiversification": true,
    ///   "maxSectorAllocationPercent": 30,
    ///   "minPositionSizePercent": 5,
    ///   "allowedStrategies": ["MOMENTUM", "BREAKOUT"],
    ///   "timeframeMinutes": 15,
    ///   "minConfidence": 65
    /// }
    /// ```
    /// </remarks>
    [HttpPost("optimize")]
    [ProducesResponseType(typeof(OptimizedPortfolioDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<OptimizedPortfolioDto>> OptimizePortfolio(
        [FromBody] PortfolioOptimizationRequestDto requestDto)
    {
        try
        {
            // Parse allowed strategies
            var allowedStrategies = new List<StrategyType>();
            if (requestDto.AllowedStrategies.Any())
            {
                foreach (var strategyStr in requestDto.AllowedStrategies)
                {
                    if (Enum.TryParse<StrategyType>(strategyStr, true, out var strategy))
                    {
                        allowedStrategies.Add(strategy);
                    }
                    else
                    {
                        return BadRequest($"Invalid strategy: {strategyStr}. Valid values: MOMENTUM, BREAKOUT, MEAN_REVERSION, SWING_TRADING");
                    }
                }
            }

            var request = new PortfolioOptimizationRequest
            {
                TotalCapital = requestDto.TotalCapital,
                MaxRiskPerTradePercent = requestDto.MaxRiskPerTradePercent,
                MaxPortfolioRiskPercent = requestDto.MaxPortfolioRiskPercent,
                MaxPositions = requestDto.MaxPositions,
                EnableSectorDiversification = requestDto.EnableSectorDiversification,
                MaxSectorAllocationPercent = requestDto.MaxSectorAllocationPercent,
                MinPositionSizePercent = requestDto.MinPositionSizePercent,
                AllowedStrategies = allowedStrategies,
                TimeframeMinutes = requestDto.TimeframeMinutes,
                MinConfidence = requestDto.MinConfidence
            };

            var portfolio = await _portfolioService.OptimizePortfolioAsync(request);

            var dto = new OptimizedPortfolioDto
            {
                TotalCapital = portfolio.TotalCapital,
                AllocatedCapital = portfolio.AllocatedCapital,
                UnallocatedCapital = portfolio.UnallocatedCapital,
                AllocationPercent = portfolio.AllocationPercent,
                TotalRiskAmount = portfolio.TotalRiskAmount,
                TotalRiskPercent = portfolio.TotalRiskPercent,
                MaxRiskPerTrade = portfolio.MaxRiskPerTrade,
                MaxPortfolioRisk = portfolio.MaxPortfolioRisk,
                TotalPositions = portfolio.TotalPositions,
                UniqueSectors = portfolio.UniqueSectors,
                SectorAllocation = portfolio.SectorAllocation,
                StrategyDistribution = portfolio.StrategyDistribution.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value
                ),
                TotalExpectedReturn = portfolio.TotalExpectedReturn,
                TotalExpectedReturnPercent = portfolio.TotalExpectedReturnPercent,
                AverageConfidence = portfolio.AverageConfidence,
                AverageRiskReward = portfolio.AverageRiskReward,
                Positions = portfolio.Positions.Select(p => new OptimizedPositionDto
                {
                    InstrumentId = p.InstrumentId,
                    Symbol = p.Symbol,
                    InstrumentName = p.InstrumentName,
                    Exchange = p.Exchange,
                    Sector = p.Sector,
                    Strategy = p.Strategy.ToString(),
                    Direction = p.Direction,
                    EntryPrice = p.EntryPrice,
                    StopLoss = p.StopLoss,
                    Target = p.Target,
                    AllocatedCapital = p.AllocatedCapital,
                    AllocationPercent = p.AllocationPercent,
                    Quantity = p.Quantity,
                    RiskAmount = p.RiskAmount,
                    RiskPercent = p.RiskPercent,
                    RiskRewardRatio = p.RiskRewardRatio,
                    Confidence = p.Confidence,
                    Score = p.Score,
                    ExpectedReturn = p.ExpectedReturn,
                    ExpectedReturnPercent = p.ExpectedReturnPercent,
                    Signals = p.Signals,
                    Explanation = p.Explanation
                }).ToList(),
                RejectedOpportunities = portfolio.RejectedOpportunities.Select(r => new RejectedOpportunityDto
                {
                    Symbol = r.Symbol,
                    Strategy = r.Strategy.ToString(),
                    Confidence = r.Confidence,
                    RejectionReason = r.RejectionReason
                }).ToList(),
                GeneratedAt = portfolio.GeneratedAt,
                OptimizationNotes = portfolio.OptimizationNotes,
                HealthScore = new PortfolioHealthScoreDto
                {
                    OverallScore = portfolio.HealthScore.OverallScore,
                    DiversificationScore = portfolio.HealthScore.DiversificationScore,
                    RiskManagementScore = portfolio.HealthScore.RiskManagementScore,
                    QualityScore = portfolio.HealthScore.QualityScore,
                    HealthRating = portfolio.HealthScore.HealthRating,
                    Strengths = portfolio.HealthScore.Strengths,
                    Concerns = portfolio.HealthScore.Concerns
                }
            };

            return Ok(dto);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing portfolio");
            return StatusCode(500, "Error optimizing portfolio");
        }
    }

    /// <summary>
    /// Get portfolio optimization with default conservative settings
    /// </summary>
    [HttpGet("optimize/conservative")]
    [ProducesResponseType(typeof(OptimizedPortfolioDto), 200)]
    public async Task<ActionResult<OptimizedPortfolioDto>> GetConservativePortfolio(
        [FromQuery] decimal capital = 1000000)
    {
        var request = new PortfolioOptimizationRequestDto
        {
            TotalCapital = capital,
            MaxRiskPerTradePercent = 1.5m,
            MaxPortfolioRiskPercent = 4.0m,
            MaxPositions = 8,
            EnableSectorDiversification = true,
            MaxSectorAllocationPercent = 25m,
            MinPositionSizePercent = 8m,
            TimeframeMinutes = 15,
            MinConfidence = 70
        };

        return await OptimizePortfolio(request);
    }

    /// <summary>
    /// Get portfolio optimization with aggressive growth settings
    /// </summary>
    [HttpGet("optimize/aggressive")]
    [ProducesResponseType(typeof(OptimizedPortfolioDto), 200)]
    public async Task<ActionResult<OptimizedPortfolioDto>> GetAggressivePortfolio(
        [FromQuery] decimal capital = 1000000)
    {
        var request = new PortfolioOptimizationRequestDto
        {
            TotalCapital = capital,
            MaxRiskPerTradePercent = 3.0m,
            MaxPortfolioRiskPercent = 10.0m,
            MaxPositions = 15,
            EnableSectorDiversification = true,
            MaxSectorAllocationPercent = 40m,
            MinPositionSizePercent = 3m,
            TimeframeMinutes = 15,
            MinConfidence = 60
        };

        return await OptimizePortfolio(request);
    }

    /// <summary>
    /// Get portfolio optimization with balanced settings
    /// </summary>
    [HttpGet("optimize/balanced")]
    [ProducesResponseType(typeof(OptimizedPortfolioDto), 200)]
    public async Task<ActionResult<OptimizedPortfolioDto>> GetBalancedPortfolio(
        [FromQuery] decimal capital = 1000000)
    {
        var request = new PortfolioOptimizationRequestDto
        {
            TotalCapital = capital,
            MaxRiskPerTradePercent = 2.0m,
            MaxPortfolioRiskPercent = 6.0m,
            MaxPositions = 10,
            EnableSectorDiversification = true,
            MaxSectorAllocationPercent = 30m,
            MinPositionSizePercent = 5m,
            TimeframeMinutes = 15,
            MinConfidence = 65
        };

        return await OptimizePortfolio(request);
    }
}