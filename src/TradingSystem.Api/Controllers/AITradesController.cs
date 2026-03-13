using Microsoft.AspNetCore.Mvc;
using TradingSystem.AI.Services;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/trades")]
public class AITradesController : ControllerBase
{
    private readonly AIRecommendationService _aiRecommendationService;
    private readonly ILogger<AITradesController> _logger;

    public AITradesController(
        AIRecommendationService aiRecommendationService,
        ILogger<AITradesController> logger)
    {
        _aiRecommendationService = aiRecommendationService;
        _logger = logger;
    }

    /// <summary>
    /// Generate AI-powered trade recommendations ranked by predicted success probability
    /// </summary>
    /// <remarks>
    /// Uses machine learning to predict trade success based on:
    /// - Technical indicators (RSI, MACD, moving averages, ATR, ADX)
    /// - Volume analysis
    /// - Volatility metrics (Bollinger Bands, historical volatility)
    /// - Market sentiment
    /// - Risk-reward ratios
    /// - Strategy signals
    /// 
    /// Returns recommendations ranked by composite score combining:
    /// - AI predicted success probability (60% weight)
    /// - Strategy confidence score (40% weight)
    /// 
    /// Example request:
    /// ```json
    /// {
    ///   "topCount": 10,
    ///   "minConfidence": 65,
    ///   "minAIProbability": 0.6,
    ///   "timeframeMinutes": 15,
    ///   "allowedStrategies": ["MOMENTUM", "BREAKOUT"]
    /// }
    /// ```
    /// </remarks>
    [HttpPost("ai-recommendations")]
    [ProducesResponseType(typeof(List<AIRecommendationDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<List<AIRecommendationDto>>> GetAIRecommendations(
        [FromBody] AIRecommendationRequest request)
    {
        try
        {
            // Validate request
            if (request.TopCount < 1 || request.TopCount > 50)
                return BadRequest("TopCount must be between 1 and 50");

            if (request.MinConfidence < 0 || request.MinConfidence > 100)
                return BadRequest("MinConfidence must be between 0 and 100");

            if (request.MinAIProbability < 0 || request.MinAIProbability > 1)
                return BadRequest("MinAIProbability must be between 0 and 1");

            // Parse strategies
            var allowedStrategies = new List<StrategyType>();
            if (request.AllowedStrategies.Any())
            {
                foreach (var strategyStr in request.AllowedStrategies)
                {
                    if (Enum.TryParse<StrategyType>(strategyStr, true, out var strategy))
                    {
                        allowedStrategies.Add(strategy);
                    }
                    else
                    {
                        return BadRequest($"Invalid strategy: {strategyStr}");
                    }
                }
            }

            // Generate AI recommendations
            var recommendations = await _aiRecommendationService.GenerateAIRecommendationsAsync(
                topCount: request.TopCount,
                minConfidence: request.MinConfidence,
                minAIProbability: request.MinAIProbability,
                timeframeMinutes: request.TimeframeMinutes,
                allowedStrategies: allowedStrategies.Any() ? allowedStrategies : null);

            // Map to DTOs
            var dtos = recommendations.Select(r => new AIRecommendationDto
            {
                InstrumentId = r.InstrumentId,
                Symbol = r.Symbol,
                InstrumentName = r.InstrumentName,
                Exchange = r.Exchange,
                Sector = r.Sector,
                Direction = r.Direction,
                EntryPrice = r.EntryPrice,
                StopLoss = r.StopLoss,
                Target = r.Target,
                RiskRewardRatio = r.RiskRewardRatio,
                SuccessProbability = r.SuccessProbability,
                AIScore = r.AIScore,
                PredictionConfidence = r.PredictionConfidence,
                StrategyScore = r.StrategyScore,
                StrategyConfidence = r.StrategyConfidence,
                CompositeScore = r.CompositeScore,
                TopFeatures = r.TopFeatures,
                Strategy = r.Strategy,
                Signals = r.Signals,
                Explanation = r.Explanation,
                MarketSentiment = r.MarketSentiment,
                MarketCondition = r.MarketCondition,
                RiskLevel = r.RiskLevel,
                RiskFactors = r.RiskFactors,
                OpportunityFactors = r.OpportunityFactors
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI recommendations");
            return StatusCode(500, "Error generating AI recommendations");
        }
    }

    /// <summary>
    /// Get high-confidence AI recommendations (probability >= 70%)
    /// </summary>
    [HttpGet("ai-recommendations/high-confidence")]
    [ProducesResponseType(typeof(List<AIRecommendationDto>), 200)]
    public async Task<ActionResult<List<AIRecommendationDto>>> GetHighConfidenceRecommendations(
        [FromQuery] int topCount = 10)
    {
        var request = new AIRecommendationRequest
        {
            TopCount = topCount,
            MinConfidence = 70,
            MinAIProbability = 0.7f,
            TimeframeMinutes = 15
        };

        return await GetAIRecommendations(request);
    }

    /// <summary>
    /// Get momentum-focused AI recommendations
    /// </summary>
    [HttpGet("ai-recommendations/momentum")]
    [ProducesResponseType(typeof(List<AIRecommendationDto>), 200)]
    public async Task<ActionResult<List<AIRecommendationDto>>> GetMomentumRecommendations(
        [FromQuery] int topCount = 10)
    {
        var request = new AIRecommendationRequest
        {
            TopCount = topCount,
            MinConfidence = 65,
            MinAIProbability = 0.6f,
            TimeframeMinutes = 15,
            AllowedStrategies = new List<string> { "MOMENTUM" }
        };

        return await GetAIRecommendations(request);
    }
}