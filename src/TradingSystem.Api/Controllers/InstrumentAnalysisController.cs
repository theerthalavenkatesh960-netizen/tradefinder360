using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Helpers;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/instrumentAnalysis")]
public class InstrumentAnalysisController : ControllerBase
{
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    private static DateTime IstToday =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist).Date;

    private readonly IInstrumentService _instrumentService;
    private readonly IIndicatorService _indicatorService;
    private readonly ICandleService _candleService;
    private readonly MarketScannerService _scanner;
    private readonly TradeRecommendationService _recommender;
    private readonly IInstrumentPriceRepository _priceRepository;

    public InstrumentAnalysisController(
        IInstrumentService instrumentService,
        IIndicatorService indicatorService,
        ICandleService candleService,
        MarketScannerService scanner,
        TradeRecommendationService recommender,
        IInstrumentPriceRepository priceRepository)
    {
        _instrumentService = instrumentService;
        _indicatorService = indicatorService;
        _candleService = candleService;
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

        // Ensure indicators are calculated for the appropriate date range
        var daysBack = CandleDataLimits.GetDefaultDaysBack(instrument.InstrumentType, timeframe);
        var fromDate = IstToday.AddDays(-daysBack);
        var toDate = IstToday.AddDays(1);

        var allIndicators = await _indicatorService.EnsureIndicatorsCalculatedAsync(
            instrument.Id, timeframe, fromDate, toDate);

        var latestIndicator = allIndicators.LastOrDefault();

        if (latestIndicator == null)
            return NotFound($"No indicator data found for '{symbol}'. Ensure candle data has been fetched.");

        // Fetch recent candles for context calculations (DB only, no API)
        var candles = await _candleService.GetCandlesFromDbAsync(
            instrument.Id, timeframe, IstToday.AddDays(-30), toDate);

        var scanResult = await _scanner.ScanInstrumentAsync(instrument, timeframe);

        // Build indicator values for helper calculations
        var indicatorValues = new IndicatorValues
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
        };

        var lastClose = scanResult?.LastClose ?? candles.LastOrDefault()?.Close ?? 0;

        // Recommendation / entry guidance
        var recommendation = await _recommender.GetLatestForInstrumentAsync(instrument.Id);
        EntryGuidanceDto? guidance = null;
        NoTradeContextDto? noTradeContext = null;
        string explanation;
        List<string> reasoningPoints;
        int confidence;

        if (recommendation != null && recommendation.IsActive
            && recommendation.ExpiresAt > DateTimeOffset.UtcNow)
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

            // Enrich with confidence breakdown and excursion estimates
            if (scanResult != null)
            {
                guidance.ConfidenceBreakdown = AnalysisContextBuilder.BuildConfidenceBreakdown(
                    scanResult, indicatorValues, candles);
            }

            guidance.ExpectedHoldingMinutes = AnalysisContextBuilder.EstimateHoldingMinutes(
                recommendation.EntryPrice, recommendation.Target, indicatorValues.ATR, timeframe);

            var (mae, mfe) = AnalysisContextBuilder.EstimateExcursion(
                recommendation.EntryPrice, indicatorValues.ATR);
            guidance.MaxAdverseExcursionPct = mae;
            guidance.MaxFavorableExcursionPct = mfe;

            explanation = recommendation.ExplanationText;
            reasoningPoints = recommendation.ReasoningPoints;
            confidence = recommendation.Confidence;
        }
        else
        {
            var direction = scanResult?.Bias == ScanBias.BULLISH ? "BUY" : "SELL";

            explanation = scanResult != null
                ? $"No active recommendation — {BuildGateExplanation(scanResult, indicatorValues, lastClose, direction)}"
                : "No recommendation generated for current market conditions.";
            reasoningPoints = scanResult?.Reasons ?? [];
            confidence = scanResult?.SetupScore ?? 0;

            // Build no-trade diagnostics
            noTradeContext = AnalysisContextBuilder.BuildNoTradeContext(
                scanResult, indicatorValues, lastClose, candles, timeframe);
        }

        // Build enriched context sections (always present)
        var recentIndicators = allIndicators
            .OrderByDescending(s => s.Timestamp)
            .Take(50)
            .OrderBy(s => s.Timestamp)
            .ToList();

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
            NoTradeContext = noTradeContext,
            VolumeContext = AnalysisContextBuilder.BuildVolumeContext(candles),
            StructureLevels = AnalysisContextBuilder.BuildStructureLevels(candles),
            SignalTiming = AnalysisContextBuilder.BuildSignalTiming(recentIndicators),
            MarketRegime = AnalysisContextBuilder.BuildMarketRegime(indicatorValues, candles),
            Confidence = confidence,
            Explanation = explanation,
            ReasoningPoints = reasoningPoints,
            AnalysedAt = DateTimeOffset.UtcNow
        };

        return Ok(dto);
    }

    /// <summary>
    /// Summarizes which gate failed for the analysis explanation text.
    /// </summary>
    private static string BuildGateExplanation(
        ScanResult scan, IndicatorValues indicators, decimal lastClose, string direction)
    {
        if (scan.SetupScore < 50 || scan.Bias == ScanBias.NONE)
            return $"setup score {scan.SetupScore}/100 too low or no directional bias";

        if (scan.SetupScore < 65)
            return $"confidence {scan.SetupScore}/100 below minimum 65";

        if (scan.MarketState != ScanMarketState.PULLBACK_READY)
            return $"market state is {scan.MarketState}, not PULLBACK_READY";

        if (indicators.ADX < 25)
            return $"ADX {indicators.ADX:F1} below 25 — trend not confirmed";

        if (indicators.ADX > 60)
            return $"ADX {indicators.ADX:F1} above 60 — trend exhaustion";

        if (direction == "BUY" && lastClose < indicators.VWAP && indicators.VWAP > 0)
            return $"price below VWAP — no buy setup";

        if (direction == "SELL" && lastClose > indicators.VWAP && indicators.VWAP > 0)
            return $"price above VWAP — no sell setup";

        return "market is closed or conditions not met";
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

        // Use the appropriate date range based on instrument type + timeframe
        var daysBack = CandleDataLimits.GetDefaultDaysBack(instrument.InstrumentType, timeframe);
        var fromDate = IstToday.AddDays(-daysBack);
        var toDate = IstToday.AddDays(1);

        // Ensure indicators are calculated for all available candle data
        var allIndicators = await _indicatorService.EnsureIndicatorsCalculatedAsync(
            instrument.Id, timeframe, fromDate, toDate);

        if (allIndicators.Count == 0)
            return NotFound($"No indicator history found for '{symbol}'.");

        // Take the most recent 'limit' snapshots
        var snapshots = allIndicators
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .OrderBy(s => s.Timestamp)
            .ToList();

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
            return NotFound($"Instrument '{symbol}' not found.");

        var key = instrument.InstrumentKey;
        var result = await _recommender.GenerateAsync(key, timeframe);

        if (!result.IsGenerated)
        {
            return Ok(new RecommendationDto
            {
                Symbol = symbol,
                Direction = "NONE",
                Confidence = 0,
                ExplanationText = result.BlockedReason ?? "No recommendation for current market conditions",
                ReasoningPoints = new List<string> { result.BlockedReason ?? "Signal gates not met" },
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        var rec = result.Recommendation!;
        return Ok(new RecommendationDto
        {
            Id = rec.Id,
            InstrumentId = instrument.Id,
            Symbol = instrument.Symbol,
            Direction = rec.Direction,
            EntryPrice = rec.EntryPrice,
            StopLoss = rec.StopLoss,
            Target = rec.Target,
            RiskRewardRatio = rec.RiskRewardRatio,
            Confidence = rec.Confidence,
            OptionType = rec.OptionType,
            OptionStrike = rec.OptionStrike,
            ExplanationText = rec.ExplanationText,
            ReasoningPoints = rec.ReasoningPoints,
            Timestamp = rec.Timestamp,
            ExpiresAt = rec.ExpiresAt
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
