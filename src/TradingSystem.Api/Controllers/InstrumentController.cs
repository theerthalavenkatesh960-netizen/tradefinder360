using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/instrument")]
public class InstrumentController : ControllerBase
{
    private readonly IInstrumentService _instrumentService;
    private readonly IIndicatorService _indicatorService;
    private readonly MarketScannerService _scanner;
    private readonly TradeRecommendationService _recommender;

    public InstrumentController(
        IInstrumentService instrumentService,
        IIndicatorService indicatorService,
        MarketScannerService scanner,
        TradeRecommendationService recommender)
    {
        _instrumentService = instrumentService;
        _indicatorService = indicatorService;
        _scanner = scanner;
        _recommender = recommender;
    }

    [HttpGet("{symbol}/analysis")]
    public async Task<ActionResult<AnalysisDto>> GetAnalysis(
        string symbol,
        [FromQuery] int timeframe = 15)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);

        if (instrument == null || !instrument.IsActive)
            return NotFound($"Instrument '{symbol}' not found.");

        var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, timeframe);

        if (latestIndicator == null)
            return NotFound($"No indicator data found for '{symbol}'. Ensure data has been fetched.");
 
        var scanResult = await _scanner.ScanInstrumentAsync(instrument, timeframe);

        var recommendation = await _recommender.GetLatestForInstrumentAsync(instrument.Id);
        EntryGuidanceDto? guidance = null;
        if (recommendation != null)
        {
            guidance = new EntryGuidanceDto
            {
                Direction = recommendation.Direction,
                EntryPrice = recommendation.EntryPrice,
                StopLoss = recommendation.StopLoss,
                Target = recommendation.Target,
                RiskRewardRatio = recommendation.RiskRewardRatio,
                OptionType = recommendation.OptionType,
                OptionStrike = recommendation.OptionStrike
            };
        }

        var dto = new AnalysisDto
        {
            InstrumentKey = instrument.InstrumentKey,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            Indicators = new IndicatorSnapshotDto
            {
                EMAFast = latestIndicator.EMAFast,
                EMASlow = latestIndicator.EMASlow,
                RSI = latestIndicator.RSI,
                MacdLine = latestIndicator.MacdLine,
                MacdSignal = latestIndicator.MacdSignal,
                MacdHistogram = latestIndicator.MacdHistogram,
                ADX = latestIndicator.ADX,
                PlusDI = latestIndicator.PlusDI,
                MinusDI = latestIndicator.MinusDI,
                ATR = latestIndicator.ATR,
                BollingerUpper = latestIndicator.BollingerUpper,
                BollingerMiddle = latestIndicator.BollingerMiddle,
                BollingerLower = latestIndicator.BollingerLower,
                VWAP = latestIndicator.VWAP,
                Timestamp = latestIndicator.Timestamp
            },
            TrendState = scanResult != null ? new TrendStateDto
            {
                State = scanResult.MarketState.ToString(),
                Bias = scanResult.Bias.ToString(),
                SetupScore = scanResult.SetupScore,
                QualityLabel = scanResult.QualityLabel,
                ScoreBreakdown = new ScoreBreakdownDto
                {
                    ADX = scanResult.ScoreBreakdown.AdxScore,
                    RSI = scanResult.ScoreBreakdown.RsiScore,
                    EmaVwap = scanResult.ScoreBreakdown.EmaVwapScore,
                    Volume = scanResult.ScoreBreakdown.VolumeScore,
                    Bollinger = scanResult.ScoreBreakdown.BollingerScore,
                    Structure = scanResult.ScoreBreakdown.StructureScore,
                    Total = scanResult.ScoreBreakdown.Total
                }
            } : new TrendStateDto { State = "UNKNOWN" },
            EntryGuidance = guidance,
            Confidence = recommendation?.Confidence ?? scanResult?.SetupScore ?? 0,
            Explanation = recommendation?.ExplanationText ?? "No recommendation generated for current market conditions.",
            ReasoningPoints = recommendation?.ReasoningPoints ?? scanResult?.Reasons ?? new(),
            AnalysedAt = DateTime.UtcNow
        };

        return Ok(dto);
    }

    [HttpGet("{symbol}/indicators")]
    public async Task<ActionResult<List<IndicatorSnapshotDto>>> GetIndicatorHistory(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int limit = 50)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        var snapshots = await _indicatorService.GetRecentAsync(instrument.Id, timeframe, limit);

        if (!snapshots.Any())
            return NotFound($"No indicator history found for '{symbol}'.");

        var dtos = snapshots.Select(s => new IndicatorSnapshotDto
        {
            EMAFast = s.EMAFast,
            EMASlow = s.EMASlow,
            RSI = s.RSI,
            MacdLine = s.MacdLine,
            MacdSignal = s.MacdSignal,
            MacdHistogram = s.MacdHistogram,
            ADX = s.ADX,
            PlusDI = s.PlusDI,
            MinusDI = s.MinusDI,
            ATR = s.ATR,
            BollingerUpper = s.BollingerUpper,
            BollingerMiddle = s.BollingerMiddle,
            BollingerLower = s.BollingerLower,
            VWAP = s.VWAP,
            Timestamp = s.Timestamp
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost("{symbol}/recommend")]
    public async Task<ActionResult<RecommendationDto>> GenerateRecommendation(
        string symbol,
        [FromQuery] int timeframe = 15)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
        {
            return NotFound($"Instrument '{symbol}' not found.");
        }

        var key = instrument.InstrumentKey;
        try
        {
            var r = await _recommender.GenerateAsync(key, timeframe);
        }catch (Exception ex)
        {
            return StatusCode(500, $"Error generating recommendation: {ex.Message}");
        }
        var recommendation = await _recommender.GenerateAsync(key, timeframe);

        if (recommendation == null)
            return NoContent();

        //var instrument = await _instrumentService.GetByKeyAsync(key);

        return Ok(new RecommendationDto
        {
            Id = recommendation.Id,
            InstrumentId = instrument.Id,
            Symbol = instrument?.Symbol ?? key,
            Direction = recommendation.Direction,
            EntryPrice = recommendation.EntryPrice,
            StopLoss = recommendation.StopLoss,
            Target = recommendation.Target,
            RiskRewardRatio = recommendation.RiskRewardRatio,
            Confidence = recommendation.Confidence,
            OptionType = recommendation.OptionType,
            OptionStrike = recommendation.OptionStrike,
            ExplanationText = recommendation.ExplanationText,
            ReasoningPoints = recommendation.ReasoningPoints,
            Timestamp = recommendation.Timestamp,
            ExpiresAt = recommendation.ExpiresAt
        });
    }
    

    [HttpPost("sectors")]
    public async Task<ActionResult<List<Sector>>> GetSectors()
    {
        var sectors = await _instrumentService.GetSectorsAsync();
        return Ok(sectors);
    }
}
