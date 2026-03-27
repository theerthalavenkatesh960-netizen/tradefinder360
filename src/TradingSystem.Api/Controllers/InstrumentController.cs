using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Helpers;
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
    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    /// <summary>Consistent IST "today" for the lifetime of a single call-chain.</summary>
    private static DateTime IstNow =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Ist);

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
    // GET /api/instrument — simple list for the instruments page
    // Returns all active instruments with price data. No filters, no scan.
    // =========================================================================
    [HttpGet]
    [ProducesResponseType(typeof(List<InstrumentDto>), 200)]
    public async Task<ActionResult<List<InstrumentDto>>> GetAllInstruments(
        [FromQuery] string priceTimeframe = "1D")
    {
        var instruments = await _instrumentService.GetActiveAsync();
        if (!instruments.Any())
            return Ok(new List<InstrumentDto>());

        var ids = instruments.Select(i => i.Id);
        var priceMap = await _priceRepository
            .GetLatestPricesForInstrumentsAsync(ids, priceTimeframe);

        var dtos = instruments.Select(inst =>
        {
            var dto = MapToInstrumentDto(inst);
            if (priceMap.TryGetValue(inst.Id, out var price))
                ApplyPriceData(dto, price);
            return dto;
        }).OrderBy(d => d.Symbol).ToList();

        return Ok(dtos);
    }

    // =========================================================================
    // POST /api/instrument/search — advanced filtered search
    // Send {} for defaults → all STOCK instruments, sorted by symbol, page 1.
    // =========================================================================
    [HttpPost("search")]
    [ProducesResponseType(typeof(PaginatedResult<InstrumentDto>), 200)]
    public async Task<ActionResult<PaginatedResult<InstrumentDto>>> SearchInstruments(
        [FromBody] InstrumentSearchRequest request)
    {
        var instruments = await _instrumentService.GetActiveAsync();
        if (!instruments.Any())
            return Ok(new PaginatedResult<InstrumentDto>());

        // Step 1 — metadata filters (no I/O)
        var filtered = ApplyMetadataFilters(instruments, request);

        // Step 2 — bulk-fetch prices
        var ids = filtered.Select(i => i.Id);
        var priceMap = await _priceRepository
            .GetLatestPricesForInstrumentsAsync(ids, request.PriceTimeframe);

        // Step 3 — price-based filters
        if (request.MinChangePercent.HasValue || request.MaxChangePercent.HasValue)
        {
            filtered = filtered.Where(inst =>
            {
                if (!priceMap.TryGetValue(inst.Id, out var p) || p.Open == 0) return false;
                var pct = (p.Close - p.Open) / p.Open * 100;
                if (request.MinChangePercent.HasValue && pct < request.MinChangePercent.Value) return false;
                if (request.MaxChangePercent.HasValue && pct > request.MaxChangePercent.Value) return false;
                return true;
            }).ToList();
        }

        // Step 4 — build DTOs with scan + recommendation
        var dtoTasks = filtered.Select(async inst =>
        {
            var dto = MapToInstrumentDto(inst);

            if (priceMap.TryGetValue(inst.Id, out var price))
                ApplyPriceData(dto, price);

            ScanResult? scanResult = null;
            Recommendation? recommendation = null;

            // Only scan if analysis filters are requested
            if (NeedsAnalysisData(request))
            {
                scanResult = await _scanner.ScanInstrumentAsync(inst, request.ScanTimeframe);
                if (scanResult != null)
                    dto.Trend = scanResult.Bias.ToString().ToLower();

                recommendation = await _recommender.GetLatestForInstrumentAsync(inst.Id);
                if (recommendation != null)
                    ApplyRecommendationData(dto, recommendation);
            }

            return (dto, scanResult, recommendation);
        });

        var results = await Task.WhenAll(dtoTasks);

        // Step 5 — analysis-based filters
        var finalList = results.AsEnumerable();

        if (request.Trend is not null)
            finalList = finalList.Where(r =>
                string.Equals(r.dto.Trend, request.Trend, StringComparison.OrdinalIgnoreCase));

        if (request.MinSetupScore.HasValue)
            finalList = finalList.Where(r =>
                r.scanResult != null && r.scanResult.SetupScore >= request.MinSetupScore.Value);

        if (request.HasRecommendation == true)
            finalList = finalList.Where(r =>
                r.recommendation is { IsActive: true });

        // Step 6 — indicator-based filters (only fetched when needed)
        if (request.MinAdx.HasValue || request.RsiBelow.HasValue || request.RsiAbove.HasValue)
        {
            var indicatorTasks = finalList.Select(async r =>
            {
                var ind = await _indicatorService.GetLatestAsync(r.dto.Id, request.ScanTimeframe);
                return (r.dto, r.scanResult, r.recommendation, ind);
            });

            var withIndicators = await Task.WhenAll(indicatorTasks);

            finalList = withIndicators.Where(r =>
            {
                if (r.ind == null) return false;
                if (request.MinAdx.HasValue && r.ind.ADX < request.MinAdx.Value) return false;
                if (request.RsiBelow.HasValue && r.ind.RSI >= request.RsiBelow.Value) return false;
                if (request.RsiAbove.HasValue && r.ind.RSI <= request.RsiAbove.Value) return false;
                return true;
            }).Select(r => (r.dto, r.scanResult, r.recommendation));
        }

        var dtos = finalList.Select(r => r.dto).ToList();

        // Step 7 — sort
        dtos = ApplySorting(dtos, request.SortBy, request.SortDirection);

        // Step 8 — paginate
        var totalCount = dtos.Count;
        var pageSize = Math.Clamp(request.PageSize, 1, 200);
        var page = Math.Max(request.Page, 1);
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
        [FromQuery] int candleDays = 0)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        if (candleDays <= 0)
            candleDays = CandleDataLimits.GetDefaultDaysBack(instrument.InstrumentType, scanTimeframe);

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
        // var fromDate = DateTime.UtcNow.AddDays(-candleDays);
        // var toDate = DateTime.UtcNow.AddDays(1);
        var fromDateUtc = IstNow.AddDays(-candleDays);

        var toDateUtc = IstNow.AddDays(1);

        var candles = await _candleService.GetCandlesAsync(
            instrument.Id, scanTimeframe, fromDateUtc, toDateUtc);

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
        [FromQuery] int days = 0,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        if (days <= 0)
            days = CandleDataLimits.GetDefaultDaysBack(instrument.InstrumentType, timeframe);

        var fromDateUtc = IstNow.AddDays(-days);
        var toDateUtc = IstNow.AddDays(1);

        var candles = await _candleService.GetCandlesAsync(instrument.Id, timeframe, fromDateUtc, toDateUtc);
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
    // GET /api/instrument/sectors/{sectorName}/instruments
    // Instruments belonging to a specific sector, with latest prices.
    // =========================================================================
    [HttpGet("sectors/{sectorName}/instruments")]
    [ProducesResponseType(typeof(List<InstrumentDto>), 200)]
    public async Task<ActionResult<List<InstrumentDto>>> GetInstrumentsBySector(
        string sectorName,
        [FromQuery] string priceTimeframe = "1D")
    {
        var instruments = await _instrumentService.GetActiveAsync();

        var filtered = instruments
            .Where(i => i.Sector != null &&
                (i.Sector.Name.Equals(sectorName, StringComparison.OrdinalIgnoreCase) ||
                 i.Sector.Code.Equals(sectorName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (!filtered.Any())
            return Ok(new List<InstrumentDto>());

        var ids = filtered.Select(i => i.Id);
        var priceMap = await _priceRepository
            .GetLatestPricesForInstrumentsAsync(ids, priceTimeframe);

        var dtos = filtered.Select(inst =>
        {
            var dto = MapToInstrumentDto(inst);
            if (priceMap.TryGetValue(inst.Id, out var price))
                ApplyPriceData(dto, price);
            return dto;
        }).OrderBy(d => d.Symbol).ToList();

        return Ok(dtos);
    }

    // =========================================================================
    // GET /api/instrument/exchanges — distinct exchanges for UI dropdowns
    // =========================================================================
    [HttpGet("exchanges")]
    [ProducesResponseType(typeof(List<string>), 200)]
    public async Task<ActionResult<List<string>>> GetExchanges()
    {
        var instruments = await _instrumentService.GetActiveAsync();
        var exchanges = instruments
            .Select(i => i.Exchange)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e)
            .ToList();

        return Ok(exchanges);
    }

    // =========================================================================
    // GET /api/instrument/{symbol}/summary — lightweight preview for stock cards
    // No scanner, no recommendation generation — just metadata + latest price.
    // =========================================================================
    [HttpGet("{symbol}/summary")]
    [ProducesResponseType(typeof(InstrumentSummaryDto), 200)]
    public async Task<ActionResult<InstrumentSummaryDto>> GetInstrumentSummary(
        string symbol,
        [FromQuery] string priceTimeframe = "1D")
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument '{symbol}' not found.");

        var dto = new InstrumentSummaryDto
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

        return Ok(dto);
    }

    // =========================================================================
    // PRIVATE HELPERS
    // =========================================================================

    /// <summary>
    /// Returns true if any analysis/indicator filter is set,
    /// meaning we need to run the scanner and fetch recommendations.
    /// Avoids expensive I/O when only metadata filters are used.
    /// </summary>
    private static bool NeedsAnalysisData(InstrumentSearchRequest r) =>
        r.Trend is not null
        || r.MinSetupScore.HasValue
        || r.HasRecommendation == true
        || r.MinAdx.HasValue
        || r.RsiBelow.HasValue
        || r.RsiAbove.HasValue;

    private static List<TradingInstrument> ApplyMetadataFilters(
        List<TradingInstrument> instruments, InstrumentSearchRequest filter)
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

        // Default: STOCK — always applied unless explicitly set to something else
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
        List<InstrumentDto> dtos, string sortBy, string sortDirection)
    {
        bool desc = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        return (sortBy.ToLowerInvariant()) switch
        {
            "symbol" => desc ? dtos.OrderByDescending(d => d.Symbol).ToList()
                                : dtos.OrderBy(d => d.Symbol).ToList(),
            "price" => desc ? dtos.OrderByDescending(d => d.Price ?? 0).ToList()
                                : dtos.OrderBy(d => d.Price ?? 0).ToList(),
            "change" => desc ? dtos.OrderByDescending(d => d.ChangePercent ?? 0).ToList()
                                : dtos.OrderBy(d => d.ChangePercent ?? 0).ToList(),
            "marketcap" => desc ? dtos.OrderByDescending(d => d.MarketCap ?? 0).ToList()
                                : dtos.OrderBy(d => d.MarketCap ?? 0).ToList(),
            "confidence" => desc ? dtos.OrderByDescending(d => d.Confidence ?? 0).ToList()
                                : dtos.OrderBy(d => d.Confidence ?? 0).ToList(),
            "volume" => desc ? dtos.OrderByDescending(d => d.Volume ?? 0).ToList()
                                : dtos.OrderBy(d => d.Volume ?? 0).ToList(),
            _ => dtos.OrderBy(d => d.Symbol).ToList()
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