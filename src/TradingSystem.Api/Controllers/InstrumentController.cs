using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/instrument")]
public class InstrumentController : ControllerBase
{
    private readonly IInstrumentService _instrumentService;
    private readonly IIndicatorService _indicatorService;
    private readonly MarketScannerService _scanner;
    private readonly TradeRecommendationService _recommender;
    private readonly IInstrumentPriceRepository _priceRepository;

    public InstrumentController(
        IInstrumentService instrumentService,
        IIndicatorService indicatorService,
        MarketScannerService scanner,
        TradeRecommendationService recommender,
        IInstrumentPriceRepository priceRepository)
    {
        _instrumentService = instrumentService;
        _indicatorService = indicatorService;
        _scanner = scanner;
        _recommender = recommender;
        _priceRepository = priceRepository;
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
    

    // helper used by multiple endpoints to construct DTO from a trading instrument
    private async Task<InstrumentDto> BuildInstrumentDtoAsync(TradingSystem.Core.Models.TradingInstrument instrument, string priceTimeframe, int scanTimeframe)
    {
        var dto = new InstrumentDto
        {
            Id = instrument.Id,
            Name = instrument.Name,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange
        };

        // latest price
        var latestPrice = await _priceRepository.GetLatestPriceAsync(instrument.Id, priceTimeframe);
        if (latestPrice != null)
        {
            dto.Price = latestPrice.Close;
            dto.Volume = latestPrice.Volume;
            dto.Change = latestPrice.Close - latestPrice.Open;
            dto.ChangePercent = latestPrice.Open != 0
                ? Math.Round((dto.Change.Value / latestPrice.Open) * 100, 2)
                : 0;
        }

        // scan for trend/bias
        var scanResult = await _scanner.ScanInstrumentAsync(instrument, scanTimeframe);
        if (scanResult != null)
        {
            dto.Trend = scanResult.Bias.ToString().ToLower();
        }

        // recommendation info
        var recommendation = await _recommender.GetLatestForInstrumentAsync(instrument.Id);
        if (recommendation != null)
        {
            dto.EntryPrice = recommendation.EntryPrice;
            dto.ExitPrice = recommendation.Target;
            dto.StopLoss = recommendation.StopLoss;
            dto.Confidence = recommendation.Confidence;
            if (recommendation.EntryPrice > 0)
            {
                dto.ExpectedProfit = Math.Round(((recommendation.Target - recommendation.EntryPrice) / recommendation.EntryPrice) * 100, 2);
            }
        }

        return dto;
    }

    [HttpGet]
    public async Task<ActionResult<List<InstrumentDto>>> GetAllInstruments(
        [FromQuery] string priceTimeframe = "1D",
        [FromQuery] int scanTimeframe = 15)
    {
        var instruments = await _instrumentService.GetActiveAsync();
        if (!instruments.Any())
            return Ok(new List<InstrumentDto>());

        // optionally fetch prices in bulk to avoid n+1
        var ids = instruments.Select(i => i.Id);
        var priceMap = await _priceRepository.GetLatestPricesForInstrumentsAsync(ids, priceTimeframe);
        var dtoTasks = instruments.Select(async inst =>
        {
            var dto = new InstrumentDto
            {
                Id = inst.Id,
                Name = inst.Name,
                Symbol = inst.Symbol,
                Exchange = inst.Exchange
            };

            if (priceMap.TryGetValue(inst.Id, out var price))
            {
                dto.Price = price.Close;
                dto.Volume = price.Volume;
                dto.Change = price.Close - price.Open;
                dto.ChangePercent = price.Open != 0
                    ? Math.Round(((price.Close - price.Open) / price.Open) * 100, 2)
                    : 0;
            }

            var scanResult = await _scanner.ScanInstrumentAsync(inst, scanTimeframe);
            if (scanResult != null)
                dto.Trend = scanResult.Bias.ToString().ToLower();

            var recommendation = await _recommender.GetLatestForInstrumentAsync(inst.Id);
            if (recommendation != null)
            {
                dto.EntryPrice = recommendation.EntryPrice;
                dto.ExitPrice = recommendation.Target;
                dto.StopLoss = recommendation.StopLoss;
                dto.Confidence = recommendation.Confidence;
                if (recommendation.EntryPrice > 0)
                {
                    dto.ExpectedProfit = Math.Round(((recommendation.Target - recommendation.EntryPrice) / recommendation.EntryPrice) * 100, 2);
                }
            }

            return dto;
        });

        var dtos = await Task.WhenAll(dtoTasks);
        return Ok(dtos);
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<InstrumentDto>> GetInstrument(
        string symbol,
        [FromQuery] string priceTimeframe = "1D",
        [FromQuery] int scanTimeframe = 15)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        var dto = await BuildInstrumentDtoAsync(instrument, priceTimeframe, scanTimeframe);
        return Ok(dto);
    }

    [HttpPost("sectors")]
    public async Task<ActionResult<List<Sector>>> GetSectors()
    {
        var sectors = await _instrumentService.GetSectorsAsync();
        return Ok(sectors);
    }
}
