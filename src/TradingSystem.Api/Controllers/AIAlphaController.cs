using Microsoft.AspNetCore.Mvc;
using TradingSystem.AI.Services;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/ai-alpha")]
public class AIAlphaController : ControllerBase
{
    private readonly AIAlphaModelService _alphaModel;
    private readonly MarketRegimeService _regimeService;
    private readonly RegimeBasedPortfolioOptimizer _portfolioOptimizer;
    private readonly IInstrumentService _instrumentService;
    private readonly ILogger<AIAlphaController> _logger;

    public AIAlphaController(
        AIAlphaModelService alphaModel,
        MarketRegimeService regimeService,
        RegimeBasedPortfolioOptimizer portfolioOptimizer,
        IInstrumentService instrumentService,
        ILogger<AIAlphaController> logger)
    {
        _alphaModel = alphaModel;
        _regimeService = regimeService;
        _portfolioOptimizer = portfolioOptimizer;
        _instrumentService = instrumentService;
        _logger = logger;
    }

    /// <summary>
    /// Get current market regime
    /// </summary>
    [HttpGet("regime")]
    public async Task<IActionResult> GetMarketRegime()
    {
        var regime = await _regimeService.DetectRegimeAsync();
        return Ok(regime);
    }

    /// <summary>
    /// Get AI alpha prediction for a single symbol
    /// </summary>
    [HttpGet("prediction/{symbol}")]
    public async Task<IActionResult> GetPrediction(string symbol)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(symbol);
        if (instrument == null)
            return NotFound($"Instrument {symbol} not found");

        var prediction = await _alphaModel.GeneratePredictionAsync(
            instrument.Id,
            instrument.Symbol,
            instrument.Sector?.Name ?? "Unknown");

        return Ok(prediction);
    }

    /// <summary>
    /// Get top AI alpha predictions
    /// </summary>
    [HttpGet("predictions/top")]
    public async Task<IActionResult> GetTopPredictions(
        [FromQuery] int count = 20,
        [FromQuery] float minProbability = 0.6f)
    {
        var instruments = await _instrumentService.GetActiveAsync();
        var instrumentData = instruments
            .Where(i => i.InstrumentType == Core.Models.InstrumentType.STOCK)
            .Select(i => (i.Id, i.Symbol, i.Sector?.Name ?? "Unknown"))
            .ToList();

        var predictions = await _alphaModel.GenerateRankedPredictionsAsync(
            instrumentData,
            count);

        var filtered = predictions
            .Where(p => p.SuccessProbability >= minProbability)
            .ToList();

        return Ok(filtered);
    }

    /// <summary>
    /// Generate AI-optimized portfolio based on alpha predictions
    /// </summary>
    [HttpPost("portfolio/optimize")]
    public async Task<IActionResult> OptimizePortfolio(
        [FromBody] AIPortfolioOptimizationRequest request)
    {
        if (request.TotalCapital <= 0)
            return BadRequest("Total capital must be positive");

        var instruments = await _instrumentService.GetActiveAsync();
        var instrumentData = instruments
            .Where(i => i.InstrumentType == Core.Models.InstrumentType.STOCK)
            .Select(i => (i.Id, i.Symbol, i.Sector?.Name ?? "Unknown"))
            .Take(request.MaxInstruments ?? 100)  // Limit to prevent timeout
            .ToList();

        // Apply include/exclude filters
        if (request.IncludeSymbols?.Any() == true)
        {
            instrumentData = instrumentData
                .Where(i => request.IncludeSymbols.Contains(i.Symbol))
                .ToList();
        }

        if (request.ExcludeSymbols?.Any() == true)
        {
            instrumentData = instrumentData
                .Where(i => !request.ExcludeSymbols.Contains(i.Symbol))
                .ToList();
        }

        var predictions = await _alphaModel.GenerateRankedPredictionsAsync(
            instrumentData,
            50);

        // Filter by minimum probability
        var filteredPredictions = predictions
            .Where(p => p.SuccessProbability >= request.MinProbability)
            .ToList();

        if (!filteredPredictions.Any())
        {
            return Ok(new
            {
                Message = "No predictions meet the minimum probability threshold",
                MinProbability = request.MinProbability,
                TotalPredictions = predictions.Count
            });
        }

        var portfolio = await _portfolioOptimizer.OptimizePortfolioAsync(
            filteredPredictions,
            request.TotalCapital);

        return Ok(portfolio);
    }
}

/// <summary>
/// AI-specific portfolio optimization request
/// Separate from the traditional portfolio optimization to avoid conflicts
/// </summary>
public class AIPortfolioOptimizationRequest
{
    /// <summary>
    /// Total capital available for allocation
    /// </summary>
    public decimal TotalCapital { get; set; }

    /// <summary>
    /// Symbols to specifically include (optional)
    /// </summary>
    public List<string>? IncludeSymbols { get; set; }

    /// <summary>
    /// Symbols to exclude from portfolio (optional)
    /// </summary>
    public List<string>? ExcludeSymbols { get; set; }

    /// <summary>
    /// Minimum success probability threshold (0.0 to 1.0)
    /// </summary>
    public float MinProbability { get; set; } = 0.6f;

    /// <summary>
    /// Maximum number of instruments to analyze
    /// </summary>
    public int? MaxInstruments { get; set; } = 100;

    /// <summary>
    /// Maximum risk score acceptable (0 to 100)
    /// </summary>
    public float? MaxRiskScore { get; set; }

    /// <summary>
    /// Sectors to focus on (optional)
    /// </summary>
    public List<string>? PreferredSectors { get; set; }
}