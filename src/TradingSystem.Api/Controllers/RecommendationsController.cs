using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Api.DTOs;
using TradingSystem.Data;
using TradingSystem.Scanner;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/recommendations")]
public class RecommendationsController : ControllerBase
{
    private readonly TradeRecommendationService _recommender;
    private readonly TradingDbContext _db;

    public RecommendationsController(TradeRecommendationService recommender, TradingDbContext db)
    {
        _recommender = recommender;
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<RecommendationDto>>> GetActive()
    {
        var recommendations = await _recommender.GetActiveRecommendationsAsync();

        var instruments = await _db.Instruments
            .ToDictionaryAsync(i => i.InstrumentKey, i => i.Symbol);

        var dtos = recommendations.Select(r => new RecommendationDto
        {
            Id = r.Id,
            InstrumentKey = r.InstrumentKey,
            Symbol = instruments.TryGetValue(r.InstrumentKey, out var sym) ? sym : r.InstrumentKey,
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
