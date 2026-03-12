using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly TradeRecommendationService _recommender;
    private readonly IInstrumentService _instrumentService;

    public RecommendationsController(TradeRecommendationService recommender, IInstrumentService instrumentService)
    {
        _recommender = recommender;
        _instrumentService = instrumentService;
    }

    /// <summary>
    /// Get active recommendations (existing functionality)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<RecommendationDto>>> GetActive()
    {
        var recommendations = await _recommender.GetActiveRecommendationsAsync();

        var instruments = await _instrumentService.GetIdToSymbolMapAsync();

        var dtos = recommendations.Select(r => new RecommendationDto
        {
            Id = r.Id,
            InstrumentId = r.InstrumentId,
            Symbol = instruments.TryGetValue(r.InstrumentId, out var sym) ? sym : string.Empty,
            Direction = r.Direction,
            EntryPrice = r.EntryPrice,
            StopLoss = r.StopLoss,
            Target = r.Target,
            RiskRewardRatio = r.RiskRewardRatio,
            Confidence = r.Confidence,
            OptionType = r.OptionType,
            OptionStrike = r.OptionStrike,
            ExplanationText = r.ExplanationText,
            ReasoningPoints = r.ReasoningPoints,
            Timestamp = r.Timestamp,
            ExpiresAt = r.ExpiresAt
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Generate new trade recommendations based on user criteria
    /// </summary>
    /// <param name="request">User preferences for trade recommendations</param>
    [HttpPost]
    [ProducesResponseType(typeof(List<RankedRecommendationDto>), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<List<RankedRecommendationDto>>> GenerateRecommendations(
        [FromBody] RecommendationRequest request)
    {
        if (request.TargetReturnPercentage <= 0)
        {
            return BadRequest("Target return percentage must be greater than 0");
        }

        if (request.RiskTolerance <= 0 || request.RiskTolerance > 100)
        {
            return BadRequest("Risk tolerance must be between 0 and 100");
        }

        if (request.TopCount <= 0 || request.TopCount > 50)
        {
            return BadRequest("Top count must be between 1 and 50");
        }

        // Scan and evaluate stocks
        var recommendations = await _recommender.GenerateRecommendationsAsync(
            targetReturnPercentage: request.TargetReturnPercentage,
            riskTolerance: request.RiskTolerance,
            minRiskRewardRatio: request.MinRiskRewardRatio,
            timeframeMinutes: request.TimeframeMinutes);

        if (!recommendations.Any())
        {
            return Ok(new List<RankedRecommendationDto>());
        }

        // Get instrument symbols
        var instruments = await _instrumentService.GetIdToSymbolMapAsync();

        // Rank and filter recommendations
        var rankedRecommendations = recommendations
            .Select((r, index) => new RankedRecommendationDto
            {
                Rank = index + 1,
                Id = r.Id,
                InstrumentId = r.InstrumentId,
                Symbol = instruments.TryGetValue(r.InstrumentId, out var sym) ? sym : string.Empty,
                Direction = r.Direction,
                EntryPrice = r.EntryPrice,
                StopLoss = r.StopLoss,
                Target = r.Target,
                RiskRewardRatio = r.RiskRewardRatio,
                Confidence = r.Confidence,
                ExpectedReturnPercentage = CalculateExpectedReturn(r),
                RiskPercentage = CalculateRisk(r),
                IndicatorSignals = r.ReasoningPoints,
                OptionType = r.OptionType,
                OptionStrike = r.OptionStrike,
                ExplanationText = r.ExplanationText,
                Timestamp = r.Timestamp,
                ExpiresAt = r.ExpiresAt,
                Score = CalculateScore(r, request)
            })
            .OrderByDescending(r => r.Score)
            .Take(request.TopCount)
            .ToList();

        return Ok(rankedRecommendations);
    }

    /// <summary>
    /// Get top N recommendations with filters
    /// </summary>
    [HttpGet("top")]
    [ProducesResponseType(typeof(List<RankedRecommendationDto>), 200)]
    public async Task<ActionResult<List<RankedRecommendationDto>>> GetTopRecommendations(
        [FromQuery] int count = 5,
        [FromQuery] decimal? minRiskReward = null,
        [FromQuery] decimal? minConfidence = null)
    {
        var recommendations = await _recommender.GetActiveRecommendationsAsync();

        if (!recommendations.Any())
        {
            return Ok(new List<RankedRecommendationDto>());
        }

        // Apply filters
        var filtered = recommendations.AsQueryable();

        if (minRiskReward.HasValue)
        {
            filtered = filtered.Where(r => r.RiskRewardRatio >= minRiskReward.Value);
        }

        if (minConfidence.HasValue)
        {
            filtered = filtered.Where(r => r.Confidence >= minConfidence.Value);
        }

        // Get instrument symbols
        var instruments = await _instrumentService.GetIdToSymbolMapAsync();

        // Rank by confidence and risk-reward ratio
        var rankedRecommendations = filtered
            .OrderByDescending(r => r.Confidence)
            .ThenByDescending(r => r.RiskRewardRatio)
            .Take(count)
            .Select((r, index) => new RankedRecommendationDto
            {
                Rank = index + 1,
                Id = r.Id,
                InstrumentId = r.InstrumentId,
                Symbol = GetInstrumentSymbol(instruments, r.InstrumentId),
                Direction = r.Direction,
                EntryPrice = r.EntryPrice,
                StopLoss = r.StopLoss,
                Target = r.Target,
                RiskRewardRatio = r.RiskRewardRatio,
                Confidence = r.Confidence,
                ExpectedReturnPercentage = CalculateExpectedReturn(r),
                RiskPercentage = CalculateRisk(r),
                IndicatorSignals = r.ReasoningPoints,
                OptionType = r.OptionType,
                OptionStrike = r.OptionStrike,
                ExplanationText = r.ExplanationText,
                Timestamp = r.Timestamp,
                ExpiresAt = r.ExpiresAt,
                Score = r.Confidence * r.RiskRewardRatio * 100
            })
            .ToList();

        return Ok(rankedRecommendations);
    }

    private decimal CalculateExpectedReturn(Core.Models.Recommendation recommendation)
    {
        if (recommendation.EntryPrice == 0) return 0;

        return recommendation.Direction == "BUY"
            ? ((recommendation.Target - recommendation.EntryPrice) / recommendation.EntryPrice) * 100
            : ((recommendation.EntryPrice - recommendation.Target) / recommendation.EntryPrice) * 100;
    }

    private decimal CalculateRisk(Core.Models.Recommendation recommendation)
    {
        if (recommendation.EntryPrice == 0) return 0;

        return recommendation.Direction == "BUY"
            ? ((recommendation.EntryPrice - recommendation.StopLoss) / recommendation.EntryPrice) * 100
            : ((recommendation.StopLoss - recommendation.EntryPrice) / recommendation.EntryPrice) * 100;
    }

    private decimal CalculateScore(Core.Models.Recommendation recommendation, RecommendationRequest request)
    {
        var expectedReturn = CalculateExpectedReturn(recommendation);
        var risk = CalculateRisk(recommendation);

        // Scoring formula considering:
        // - How close to target return (weight: 30%)
        // - Risk-reward ratio (weight: 30%)
        // - Confidence (weight: 30%)
        // - Risk tolerance match (weight: 10%)

        var returnScore = Math.Min(expectedReturn / request.TargetReturnPercentage, 1) * 30;
        var riskRewardScore = Math.Min(recommendation.RiskRewardRatio / 3, 1) * 30; // 3:1 is excellent
        var confidenceScore = recommendation.Confidence * 30;
        var riskToleranceScore = (1 - Math.Abs(risk - request.RiskTolerance) / 100) * 10;

        return returnScore + riskRewardScore + confidenceScore + riskToleranceScore;
    }

    private static string GetInstrumentSymbol(Dictionary<int, string> instruments, int instrumentId)
    {
        return instruments.TryGetValue(instrumentId, out string sym) ? sym : string.Empty;
    }
}
