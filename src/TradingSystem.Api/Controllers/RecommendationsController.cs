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
}
