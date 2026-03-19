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
    private readonly ICandleService _candleService;
    private readonly MarketScannerService _scanner;
    private readonly TradeRecommendationService _recommender;
    private readonly IInstrumentPriceRepository _priceRepository;

    public InstrumentController(
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

    // =========================================================================
    // GET /api/instrument — list all instruments with filters & pagination
    // =========================================================================
    [HttpGet]
    [ProducesResponseType(typeof(PaginatedResult<InstrumentDto>), 200)]
    public async Task<ActionResult<PaginatedResult<InstrumentDto>>> GetAllInstruments(
        [FromQuery] InstrumentFilterQuery filter,
        [FromQuery] string priceTimeframe = "1D",
        [FromQuery] int scanTimeframe = 15)
    {
        var instruments = await _instrumentService.GetActiveAsync();
        if (!instruments.Any())
            return Ok(new PaginatedResult<InstrumentDto>());

        // Step 1 — cheap metadata filters (no I/O)
        var filtered = ApplyMetadataFilters(instruments, filter);

        // Step 2 — bulk-fetch prices
        var ids = filtered.Select(i => i.Id);
        var priceMap = await _priceRepository.GetLatestPricesForInstrumentsAsync(ids, priceTimeframe);

        // Step 3 — price-based filters
        if (filter.MinChangePercent.HasValue || filter.MaxChangePercent.HasValue)
        {
            filtered = filtered.Where(inst =>
            {
                if (!priceMap.TryGetValue(inst.Id, out var p) || p.Open == 0) return false;
                var pct = (p.Close - p.Open) / p.Open * 100;
                if (filter.MinChangePercent.HasValue && pct < filter.MinChangePercent.Value) return false;
                if (filter.MaxChangePercent.HasValue && pct > filter.MaxChangePercent.Value) return false;
                return true;
            }).ToList();
        }

        // Step 4 — build DTOs with scan + recommendation data
        var dtoTasks = filtered.Select(async inst =>
        {
            var dto = MapToInstrumentDto(inst);

            if (priceMap.TryGetValue(inst.Id, out var price))
                ApplyPriceData(dto, price);

            var scanResult = await _scanner.ScanInstrumentAsync(inst, scanTimeframe);
            if (scanResult != null)
                dto.Trend = scanResult.Bias.ToString().ToLower();

            var recommendation = await _recommender.GetLatestForInstrumentAsync(inst.Id);
            if (recommendation != null)
                ApplyRecommendationData(dto, recommendation);

            return (dto, scanResult, recommendation);
        });

        var results = await Task.WhenAll(dtoTasks);

        // Step 5 — analysis-based filters (need scan data)
        var finalList = results.AsEnumerable();

        if (filter.Trend is not null)
            finalList = finalList.Where(r =>
                string.Equals(r.dto.Trend, filter.Trend, StringComparison.OrdinalIgnoreCase));

        if (filter.MinSetupScore.HasValue)
            finalList = finalList.Where(r =>
                r.scanResult != null && r.scanResult.SetupScore >= filter.MinSetupScore.Value);

        if (filter.HasRecommendation == true)
            finalList = finalList.Where(r =>
                r.recommendation is { IsActive: true });

        // Step 6 — indicator-based filters (lazy — only fetched if needed)
        if (filter.MinAdx.HasValue || filter.RsiBelow.HasValue || filter.RsiAbove.HasValue)
        {
            var indicatorTasks = finalList.Select(async r =>
            {
                var ind = await _indicatorService.GetLatestAsync(r.dto.Id, scanTimeframe);
                return (r.dto, r.scanResult, r.recommendation, ind);
            });

            var withIndicators = await Task.WhenAll(indicatorTasks);

            finalList = withIndicators.Where(r =>
            {
                if (r.ind == null) return false;
                if (filter.MinAdx.HasValue && r.ind.ADX < filter.MinAdx.Value) return false;
                if (filter.RsiBelow.HasValue && r.ind.RSI >= filter.RsiBelow.Value) return false;
                if (filter.RsiAbove.HasValue && r.ind.RSI <= filter.RsiAbove.Value) return false;
                return true;
            }).Select(r => (r.dto, r.scanResult, r.recommendation));
        }

        var dtos = finalList.Select(r => r.dto).ToList();

        // Step 7 — sort
        dtos = ApplySorting(dtos, filter.SortBy, filter.SortDirection);

        // Step 8 — paginate
        var totalCount = dtos.Count;
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page = Math.Max(filter.Page, 1);
        var paged = dtos.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Ok(new PaginatedResult<InstrumentDto>
        {
            Items = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // =========================================================================
    // GET /api/instrument/{symbol} — full detail with candles for charting
    // =========================================================================
    [HttpGet("{symbol}")]
    [ProducesResponseType(typeof(InstrumentDetailDto), 200)]
    public async Task<ActionResult<InstrumentDetailDto>> GetInstrument(
        string symbol,
        [FromQuery] string priceTimeframe = "1D",
        [FromQuery] int scanTimeframe = 15,
        [FromQuery] int candleDays = 30)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        var dto = new InstrumentDetailDto
        {
            Id = instrument.Id,
            Name = instrument.Name,
            Symbol = instrument.Symbol,
            Exchange = instrument.Exchange,
            InstrumentKey = instrument.InstrumentKey,
            Sector = instrument.Sector?.Name,
            Industry = instrument.Industry,
            MarketCap = instrument.MarketCap,
            InstrumentType = instrument.InstrumentType.ToString(),
            TradingMode = instrument.DefaultTradingMode.ToString(),
            LotSize = instrument.LotSize,
            TickSize = instrument.TickSize,
            IsDerivativesEnabled = instrument.IsDerivativesEnabled
        };

        // Latest price
        var latestPrice = await _priceRepository.GetLatestPriceAsync(instrument.Id, priceTimeframe);
        if (latestPrice != null)
        {
            dto.Price = latestPrice.Close;
            dto.Volume = latestPrice.Volume;
            dto.DayOpen = latestPrice.Open;
            dto.DayHigh = latestPrice.High;
            dto.DayLow = latestPrice.Low;
            dto.Change = latestPrice.Close - latestPrice.Open;
            dto.ChangePercent = latestPrice.Open != 0
                ? Math.Round((latestPrice.Close - latestPrice.Open) / latestPrice.Open * 100, 2)
                : 0;
        }

        // Scan result
        var scanResult = await _scanner.ScanInstrumentAsync(instrument, scanTimeframe);
        if (scanResult != null)
        {
            dto.Trend = scanResult.Bias.ToString().ToLower();
            dto.SetupScore = scanResult.SetupScore;
            dto.MarketState = scanResult.MarketState.ToString();
        }

        // Latest indicators
        var latestIndicator = await _indicatorService.GetLatestAsync(instrument.Id, scanTimeframe);
        if (latestIndicator != null)
        {
            dto.LatestIndicators = new IndicatorSnapshotDto
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
        }

        // Recommendation
        var recommendation = await _recommender.GetLatestForInstrumentAsync(instrument.Id);
        if (recommendation is { IsActive: true }
            && recommendation.ExpiresAt > DateTimeOffset.UtcNow)
        {
            dto.EntryPrice = recommendation.EntryPrice;
            dto.ExitPrice = recommendation.Target;
            dto.StopLoss = recommendation.StopLoss;
            dto.RiskRewardRatio = recommendation.RiskRewardRatio;
            dto.Confidence = recommendation.Confidence;
            dto.RecommendationDirection = recommendation.Direction;
            dto.RecommendationExpiresAt = recommendation.ExpiresAt;
            if (recommendation.EntryPrice > 0)
            {
                dto.ExpectedProfit = Math.Round(
                    (recommendation.Target - recommendation.EntryPrice)
                    / recommendation.EntryPrice * 100, 2);
            }
        }

        // Candles for charting
        var fromDate = DateTime.UtcNow.AddDays(-candleDays);
        var toDate = DateTime.UtcNow.AddDays(1);
        var candles = await _candleService.GetCandlesAsync(
            instrument.Id, scanTimeframe, fromDate, toDate);

        dto.Candles = candles
            .OrderBy(c => c.Timestamp)
            .Select(c => new CandleDto
            {
                Timestamp = c.Timestamp,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        return Ok(dto);
    }

    // =========================================================================
    // GET /api/instrument/{symbol}/candles — raw candle data for chart updates
    // =========================================================================
    [HttpGet("{symbol}/candles")]
    [ProducesResponseType(typeof(List<CandleDto>), 200)]
    public async Task<ActionResult<List<CandleDto>>> GetCandles(
        string symbol,
        [FromQuery] int timeframe = 15,
        [FromQuery] int days = 30,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        var fromDate = from ?? DateTime.UtcNow.AddDays(-days);
        var toDate = to ?? DateTime.UtcNow.AddDays(1);

        var candles = await _candleService.GetCandlesAsync(
            instrument.Id, timeframe, fromDate, toDate);

        var dtos = candles
            .OrderBy(c => c.Timestamp)
            .Select(c => new CandleDto
            {
                Timestamp = c.Timestamp,
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume
            })
            .ToList();

        return Ok(dtos);
    }

    // =========================================================================
    // GET /api/instrument/sectors
    // =========================================================================
    [HttpGet("sectors")]
    [ProducesResponseType(typeof(List<Sector>), 200)]
    public async Task<ActionResult<List<Sector>>> GetSectors()
    {
        var sectors = await _instrumentService.GetSectorsAsync();
        return Ok(sectors);
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    private static List<TradingInstrument> ApplyMetadataFilters(
        List<TradingInstrument> instruments, InstrumentFilterQuery filter)
    {
        var q = instruments.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            q = q.Where(i =>
                i.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Exchange))
            q = q.Where(i =>
                i.Exchange.Equals(filter.Exchange, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.Sector))
            q = q.Where(i => i.Sector != null &&
                (i.Sector.Name.Contains(filter.Sector, StringComparison.OrdinalIgnoreCase) ||
                 i.Sector.Code.Contains(filter.Sector, StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(filter.Industry))
            q = q.Where(i =>
                i.Industry.Contains(filter.Industry, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(filter.InstrumentType) &&
            Enum.TryParse<InstrumentType>(filter.InstrumentType, true, out var instType))
            q = q.Where(i => i.InstrumentType == instType);

        if (filter.DerivativesEnabled.HasValue)
            q = q.Where(i => i.IsDerivativesEnabled == filter.DerivativesEnabled.Value);

        if (filter.MinMarketCap.HasValue)
            q = q.Where(i => i.MarketCap.HasValue && i.MarketCap >= filter.MinMarketCap.Value);

        if (filter.MaxMarketCap.HasValue)
            q = q.Where(i => i.MarketCap.HasValue && i.MarketCap <= filter.MaxMarketCap.Value);

        return q.ToList();
    }

    private static List<InstrumentDto> ApplySorting(
        List<InstrumentDto> dtos, string? sortBy, string? sortDirection)
    {
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return (sortBy?.ToLowerInvariant()) switch
        {
            "symbol"     => desc ? dtos.OrderByDescending(d => d.Symbol).ToList()
                                : dtos.OrderBy(d => d.Symbol).ToList(),
            "price"      => desc ? dtos.OrderByDescending(d => d.Price ?? 0).ToList()
                                : dtos.OrderBy(d => d.Price ?? 0).ToList(),
            "change"     => desc ? dtos.OrderByDescending(d => d.ChangePercent ?? 0).ToList()
                                : dtos.OrderBy(d => d.ChangePercent ?? 0).ToList(),
            "marketcap"  => desc ? dtos.OrderByDescending(d => d.MarketCap ?? 0).ToList()
                                : dtos.OrderBy(d => d.MarketCap ?? 0).ToList(),
            "confidence" => desc ? dtos.OrderByDescending(d => d.Confidence ?? 0).ToList()
                                : dtos.OrderBy(d => d.Confidence ?? 0).ToList(),
            "volume"     => desc ? dtos.OrderByDescending(d => d.Volume ?? 0).ToList()
                                : dtos.OrderBy(d => d.Volume ?? 0).ToList(),
            _            => dtos.OrderBy(d => d.Symbol).ToList()
        };
    }

    private static InstrumentDto MapToInstrumentDto(TradingInstrument inst) => new()
    {
        Id = inst.Id,
        Name = inst.Name,
        Symbol = inst.Symbol,
        Exchange = inst.Exchange,
        InstrumentKey = inst.InstrumentKey,
        Sector = inst.Sector?.Name,
        Industry = inst.Industry,
        MarketCap = inst.MarketCap,
        InstrumentType = inst.InstrumentType.ToString(),
        IsDerivativesEnabled = inst.IsDerivativesEnabled
    };

    private static void ApplyPriceData(InstrumentDto dto, InstrumentPrice price)
    {
        dto.Price = price.Close;
        dto.Volume = price.Volume;
        dto.Change = price.Close - price.Open;
        dto.ChangePercent = price.Open != 0
            ? Math.Round((price.Close - price.Open) / price.Open * 100, 2)
            : 0;
    }

    private static void ApplyRecommendationData(InstrumentDto dto, Recommendation rec)
    {
        dto.EntryPrice = rec.EntryPrice;
        dto.ExitPrice = rec.Target;
        dto.StopLoss = rec.StopLoss;
        dto.Confidence = rec.Confidence;
        if (rec.EntryPrice > 0)
        {
            dto.ExpectedProfit = Math.Round(
                (rec.Target - rec.EntryPrice) / rec.EntryPrice * 100, 2);
        }
    }
}