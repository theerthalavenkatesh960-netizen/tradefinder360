using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/market")]
public class MarketController : ControllerBase
{
    private readonly IMarketSentimentService _sentimentService;
    private readonly ILogger<MarketController> _logger;

    public MarketController(
        IMarketSentimentService sentimentService,
        ILogger<MarketController> logger)
    {
        _sentimentService = sentimentService;
        _logger = logger;
    }

    /// <summary>
    /// Get current market sentiment and context
    /// </summary>
    [HttpGet("sentiment")]
    [ProducesResponseType(typeof(MarketSentimentDto), 200)]
    public async Task<ActionResult<MarketSentimentDto>> GetMarketSentiment()
    {
        try
        {
            var context = await _sentimentService.GetCurrentMarketContextAsync();

            var dto = new MarketSentimentDto
            {
                Timestamp = context.Timestamp,
                Sentiment = context.Sentiment.ToString(),
                SentimentScore = context.SentimentScore,
                SentimentDescription = GetSentimentDescription(context.Sentiment, context.SentimentScore),
                Volatility = new MarketVolatilityDto
                {
                    Index = context.VolatilityIndex,
                    Level = GetVolatilityLevel(context.VolatilityIndex),
                    Impact = GetVolatilityImpact(context.VolatilityIndex)
                },
                Breadth = new MarketBreadthDto
                {
                    AdvanceDeclineRatio = context.MarketBreadth,
                    Interpretation = GetBreadthInterpretation(context.MarketBreadth)
                },
                MajorIndices = context.MajorIndices.Select(i => new IndexPerformanceDto
                {
                    Name = i.IndexName,
                    Symbol = i.Symbol,
                    CurrentValue = i.CurrentValue,
                    ChangePercent = i.ChangePercent,
                    DayHigh = i.DayHigh,
                    DayLow = i.DayLow,
                    Trend = GetTrend(i.ChangePercent)
                }).ToList(),
                Sectors = context.Sectors.Select(s => new SectorPerformanceDto
                {
                    Name = s.SectorName,
                    ChangePercent = s.ChangePercent,
                    StocksAdvancing = s.StocksAdvancing,
                    StocksDeclining = s.StocksDeclining,
                    RelativeStrength = s.RelativeStrength,
                    Performance = GetPerformanceCategory(s.ChangePercent)
                }).ToList(),
                KeyFactors = context.KeyFactors,
                Summary = context.Summary
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving market sentiment");
            return StatusCode(500, "Error retrieving market sentiment");
        }
    }

    /// <summary>
    /// Force refresh market sentiment analysis
    /// </summary>
    [HttpPost("sentiment/refresh")]
    [ProducesResponseType(typeof(MarketSentimentDto), 200)]
    public async Task<ActionResult<MarketSentimentDto>> RefreshMarketSentiment()
    {
        try
        {
            var context = await _sentimentService.AnalyzeAndUpdateMarketSentimentAsync();

            var dto = new MarketSentimentDto
            {
                Timestamp = context.Timestamp,
                Sentiment = context.Sentiment.ToString(),
                SentimentScore = context.SentimentScore,
                SentimentDescription = GetSentimentDescription(context.Sentiment, context.SentimentScore),
                Volatility = new MarketVolatilityDto
                {
                    Index = context.VolatilityIndex,
                    Level = GetVolatilityLevel(context.VolatilityIndex),
                    Impact = GetVolatilityImpact(context.VolatilityIndex)
                },
                Breadth = new MarketBreadthDto
                {
                    AdvanceDeclineRatio = context.MarketBreadth,
                    Interpretation = GetBreadthInterpretation(context.MarketBreadth)
                },
                MajorIndices = context.MajorIndices.Select(i => new IndexPerformanceDto
                {
                    Name = i.IndexName,
                    Symbol = i.Symbol,
                    CurrentValue = i.CurrentValue,
                    ChangePercent = i.ChangePercent,
                    DayHigh = i.DayHigh,
                    DayLow = i.DayLow,
                    Trend = GetTrend(i.ChangePercent)
                }).ToList(),
                Sectors = context.Sectors.Select(s => new SectorPerformanceDto
                {
                    Name = s.SectorName,
                    ChangePercent = s.ChangePercent,
                    StocksAdvancing = s.StocksAdvancing,
                    StocksDeclining = s.StocksDeclining,
                    RelativeStrength = s.RelativeStrength,
                    Performance = GetPerformanceCategory(s.ChangePercent)
                }).ToList(),
                KeyFactors = context.KeyFactors,
                Summary = context.Summary
            };

            return Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing market sentiment");
            return StatusCode(500, "Error refreshing market sentiment");
        }
    }

    /// <summary>
    /// Get market sentiment history
    /// </summary>
    [HttpGet("sentiment/history")]
    [ProducesResponseType(typeof(List<MarketSentimentDto>), 200)]
    public async Task<ActionResult<List<MarketSentimentDto>>> GetSentimentHistory(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        try
        {
            var from = fromDate ?? DateTime.Today.AddDays(-7);
            var to = toDate ?? DateTime.UtcNow;

            var history = await _sentimentService.GetHistoryAsync(from, to);

            var dtos = history.Select(h => new MarketSentimentDto
            {
                Timestamp = h.Timestamp,
                Sentiment = h.Sentiment.ToString(),
                SentimentScore = h.SentimentScore,
                SentimentDescription = GetSentimentDescription(h.Sentiment, h.SentimentScore),
                Volatility = new MarketVolatilityDto
                {
                    Index = h.VolatilityIndex,
                    Level = GetVolatilityLevel(h.VolatilityIndex),
                    Impact = GetVolatilityImpact(h.VolatilityIndex)
                },
                Breadth = new MarketBreadthDto
                {
                    AdvanceDeclineRatio = h.MarketBreadth,
                    Interpretation = GetBreadthInterpretation(h.MarketBreadth)
                },
                KeyFactors = h.KeyFactors,
                Summary = string.Empty
            }).ToList();

            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sentiment history");
            return StatusCode(500, "Error retrieving sentiment history");
        }
    }

    private static string GetSentimentDescription(SentimentType sentiment, decimal score)
    {
        return sentiment switch
        {
            SentimentType.BULLISH when score > 60 => "Strongly Bullish - High conviction for long positions",
            SentimentType.BULLISH => "Moderately Bullish - Favorable for long trades",
            SentimentType.BEARISH when score < -60 => "Strongly Bearish - High conviction for short positions",
            SentimentType.BEARISH => "Moderately Bearish - Favorable for short trades",
            _ => "Neutral - Mixed signals, trade with caution"
        };
    }

    private static string GetVolatilityLevel(decimal vix)
    {
        return vix switch
        {
            < 12 => "LOW",
            < 20 => "MODERATE",
            < 30 => "HIGH",
            _ => "EXTREME"
        };
    }

    private static string GetVolatilityImpact(decimal vix)
    {
        return vix switch
        {
            < 12 => "Low risk environment, tighter stops recommended",
            < 20 => "Normal trading conditions",
            < 30 => "Increased risk, wider stops recommended",
            _ => "Extreme volatility, reduce position sizes"
        };
    }

    private static string GetBreadthInterpretation(decimal ratio)
    {
        return ratio switch
        {
            > 2.0m => "Strong bullish breadth - widespread participation",
            > 1.5m => "Healthy bullish breadth",
            > 1.0m => "Slight bullish bias",
            > 0.67m => "Slight bearish bias",
            > 0.5m => "Weak breadth - limited participation",
            _ => "Very weak breadth - broad market weakness"
        };
    }

    private static string GetTrend(decimal changePercent)
    {
        return changePercent switch
        {
            > 1.0m => "STRONG_UP",
            > 0.25m => "UP",
            > -0.25m => "FLAT",
            > -1.0m => "DOWN",
            _ => "STRONG_DOWN"
        };
    }

    private static string GetPerformanceCategory(decimal changePercent)
    {
        return changePercent switch
        {
            > 0.5m => "OUTPERFORMING",
            > -0.5m => "INLINE",
            _ => "UNDERPERFORMING"
        };
    }
}