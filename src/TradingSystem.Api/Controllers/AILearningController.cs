using Microsoft.AspNetCore.Mvc;
using TradingSystem.AI.Services;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/ai")]
public class AILearningController : ControllerBase
{
    private readonly AIAlphaModelService _alphaModel;
    private readonly TradeOutcomeService _outcomeService;
    private readonly ModelTrainingPipeline _trainingPipeline;
    private readonly ModelPerformanceMonitor _performanceMonitor;
    private readonly ReinforcementLearningService _reinforcementLearning;
    private readonly MarketRegimeService _regimeService;
    private readonly IAIModelVersionRepository _modelVersionRepository;
    private readonly ITradeOutcomeRepository _outcomeRepository;
    private readonly ILogger<AILearningController> _logger;

    public AILearningController(
        AIAlphaModelService alphaModel,
        TradeOutcomeService outcomeService,
        ModelTrainingPipeline trainingPipeline,
        ModelPerformanceMonitor performanceMonitor,
        ReinforcementLearningService reinforcementLearning,
        MarketRegimeService regimeService,
        IAIModelVersionRepository modelVersionRepository,
        ITradeOutcomeRepository outcomeRepository,
        ILogger<AILearningController> logger)
    {
        _alphaModel = alphaModel;
        _outcomeService = outcomeService;
        _trainingPipeline = trainingPipeline;
        _performanceMonitor = performanceMonitor;
        _reinforcementLearning = reinforcementLearning;
        _regimeService = regimeService;
        _modelVersionRepository = modelVersionRepository;
        _outcomeRepository = outcomeRepository;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/ai/predict-trades - Generate AI predictions for trading
    /// </summary>
    [HttpPost("predict-trades")]
    public async Task<IActionResult> PredictTrades([FromBody] PredictTradesRequest request)
    {
        try
        {
            var predictions = await _alphaModel.GenerateRankedPredictionsAsync(
                request.Instruments,
                request.TopN ?? 20);

            var filtered = predictions
                .Where(p => p.SuccessProbability >= (request.MinProbability ?? 0.6f))
                .ToList();

            return Ok(new
            {
                TotalPredictions = predictions.Count,
                FilteredPredictions = filtered.Count,
                MinProbability = request.MinProbability ?? 0.6f,
                Predictions = filtered
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating trade predictions");
            return StatusCode(500, new { Error = "Error generating predictions", Details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/ai/retrain - Manually trigger model retraining
    /// </summary>
    [HttpPost("retrain")]
    public async Task<IActionResult> RetrainModel([FromBody] RetrainRequest? request)
    {
        try
        {
            _logger.LogInformation("Manual retraining triggered by API request");

            var result = await _trainingPipeline.ExecuteTrainingPipelineAsync(
                forceRetrain: request?.Force ?? false);

            if (!result.Success)
            {
                return Ok(new
                {
                    Success = false,
                    Message = result.Message,
                    result.TrainingDatasetSize
                });
            }

            return Ok(new
            {
                Success = true,
                ModelVersion = result.ModelVersion,
                TrainingDatasetSize = result.TrainingDatasetSize,
                ValidationAccuracy = $"{result.ValidationAccuracy * 100:F2}%",
                WinRate = $"{result.WinRate:F2}%",
                SharpeRatio = result.SharpeRatio,
                TrainingDuration = result.TrainingDuration.ToString(@"hh\:mm\:ss"),
                IsActivated = result.IsActivated,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model retraining");
            return StatusCode(500, new { Error = "Retraining failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/performance - Get current model performance metrics
    /// </summary>
    [HttpGet("performance")]
    public async Task<IActionResult> GetPerformance([FromQuery] string? modelVersion = null)
    {
        try
        {
            var activeModel = modelVersion == null
                ? await _modelVersionRepository.GetActiveModelAsync("AlphaModel")
                : await _modelVersionRepository.GetByVersionAsync(modelVersion, "AlphaModel");

            if (activeModel == null)
            {
                return NotFound(new { Error = "Model not found" });
            }

            var metrics = await _performanceMonitor.GetCurrentPerformanceMetricsAsync(
                activeModel.Version);

            var factorWeights = _reinforcementLearning.GetCurrentWeights();

            return Ok(new
            {
                ModelVersion = activeModel.Version,
                Status = activeModel.Status,
                IsActive = activeModel.IsActive,
                TrainingDate = activeModel.TrainingDate,
                
                // Performance metrics
                Performance = new
                {
                    Last7Days = new
                    {
                        WinRate = $"{metrics.WinRate7d:F2}%",
                        ProfitFactor = metrics.ProfitFactor7d,
                        SharpeRatio = metrics.SharpeRatio7d,
                        TotalPnL = metrics.TotalPnL7d,
                        TotalTrades = metrics.TotalTrades7d,
                        PredictionAccuracy = $"{metrics.PredictionAccuracy7d:F2}%"
                    },
                    Last30Days = new
                    {
                        WinRate = $"{metrics.WinRate30d:F2}%",
                        ProfitFactor = metrics.ProfitFactor30d,
                        SharpeRatio = metrics.SharpeRatio30d,
                        MaxDrawdown = $"{metrics.MaxDrawdown30d:F2}",
                        TotalPnL = metrics.TotalPnL30d,
                        TotalTrades = metrics.TotalTrades30d,
                        PredictionAccuracy = $"{metrics.PredictionAccuracy30d:F2}%"
                    },
                    AllTime = new
                    {
                        TotalPredictions = activeModel.TotalPredictions,
                        SuccessfulPredictions = activeModel.SuccessfulPredictions,
                        ProductionAccuracy = $"{activeModel.ProductionAccuracy:F2}%",
                        TotalPnL = activeModel.TotalPnL
                    }
                },
                
                // Current factor weights
                FactorWeights = factorWeights,
                
                LastUpdated = metrics.LastUpdated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving model performance");
            return StatusCode(500, new { Error = "Error retrieving performance", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/market-regime - Get current market regime
    /// </summary>
    [HttpGet("market-regime")]
    public async Task<IActionResult> GetMarketRegime()
    {
        try
        {
            var regime = await _regimeService.DetectRegimeAsync();

            return Ok(new
            {
                Regime = regime.Regime.ToString(),
                Confidence = $"{regime.Confidence * 100:F1}%",
                regime.TrendStrength,
                regime.VolatilityLevel,
                regime.LiquidityLevel,
                regime.MarketBreadth,
                regime.KeyIndicators,
                Guidance = new
                {
                    regime.Guidance.PreferredStrategies,
                    RecommendedExposure = $"{regime.Guidance.RecommendedExposure * 100:F0}%",
                    RecommendedLeverage = $"{regime.Guidance.RecommendedLeverage:F1}x",
                    regime.Guidance.RiskMultiplier,
                    regime.Guidance.AvoidStrategies,
                    regime.Guidance.TradingTips
                },
                DetectedAt = regime.DetectedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting market regime");
            return StatusCode(500, new { Error = "Error detecting regime", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/model-version - Get current active model version info
    /// </summary>
    [HttpGet("model-version")]
    public async Task<IActionResult> GetModelVersion()
    {
        try
        {
            var activeModel = await _modelVersionRepository.GetActiveModelAsync("AlphaModel");

            if (activeModel == null)
            {
                return NotFound(new { Error = "No active model found" });
            }

            return Ok(new
            {
                activeModel.Version,
                activeModel.ModelType,
                activeModel.Status,
                activeModel.IsActive,
                TrainingDate = activeModel.TrainingDate,
                TrainingDatasetSize = activeModel.TrainingDatasetSize,
                ValidationDatasetSize = activeModel.ValidationDatasetSize,
                activeModel.TrainingDuration,
                
                TrainingMetrics = new
                {
                    TrainingAccuracy = $"{activeModel.TrainingAccuracy * 100:F2}%",
                    ValidationAccuracy = $"{activeModel.ValidationAccuracy * 100:F2}%",
                    WinRate = $"{activeModel.WinRate:F2}%",
                    activeModel.ProfitFactor,
                    activeModel.SharpeRatio,
                    activeModel.MaxDrawdown
                },
                
                ProductionMetrics = new
                {
                    TotalPredictions = activeModel.TotalPredictions,
                    SuccessfulPredictions = activeModel.SuccessfulPredictions,
                    ProductionAccuracy = $"{activeModel.ProductionAccuracy:F2}%",
                    ProductionSharpeRatio = activeModel.ProductionSharpeRatio,
                    TotalPnL = activeModel.TotalPnL
                },
                
                activeModel.ChangeLog,
                activeModel.ImprovementNotes,
                ActivatedAt = activeModel.ActivatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving model version");
            return StatusCode(500, new { Error = "Error retrieving model version", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/model-versions - Get all model versions with history
    /// </summary>
    [HttpGet("model-versions")]
    public async Task<IActionResult> GetAllModelVersions()
    {
        try
        {
            var versions = await _modelVersionRepository.GetAllVersionsAsync("AlphaModel");

            return Ok(versions.Select(v => new
            {
                v.Id,
                v.Version,
                v.Status,
                v.IsActive,
                v.TrainingDate,
                v.TrainingDatasetSize,
                ValidationAccuracy = $"{v.ValidationAccuracy * 100:F2}%",
                WinRate = $"{v.WinRate:F2}%",
                ProductionAccuracy = $"{v.ProductionAccuracy:F2}%",
                TotalPredictions = v.TotalPredictions,
                TotalPnL = v.TotalPnL,
                v.ActivatedAt,
                v.DeprecatedAt,
                v.DeprecationReason
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving model versions");
            return StatusCode(500, new { Error = "Error retrieving versions", Details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/ai/model-version/rollback - Rollback to a previous model version
    /// </summary>
    [HttpPost("model-version/rollback")]
    public async Task<IActionResult> RollbackModel([FromBody] RollbackRequest request)
    {
        try
        {
            var targetModel = await _modelVersionRepository.GetByVersionAsync(
                request.Version, "AlphaModel");

            if (targetModel == null)
            {
                return NotFound(new { Error = $"Model version {request.Version} not found" });
            }

            if (targetModel.Status == "DEPRECATED" && string.IsNullOrEmpty(request.Reason))
            {
                return BadRequest(new { Error = "Reason required to rollback to deprecated model" });
            }

            // Activate the target model
            await _modelVersionRepository.ActivateModelAsync(targetModel.Id);

            _logger.LogWarning(
                "Model rollback: Activated version {Version}. Reason: {Reason}",
                request.Version, request.Reason ?? "Manual rollback");

            return Ok(new
            {
                Success = true,
                Message = $"Successfully rolled back to model version {request.Version}",
                Version = targetModel.Version,
                TrainingDate = targetModel.TrainingDate,
                Reason = request.Reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model rollback");
            return StatusCode(500, new { Error = "Rollback failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/ai/trade/record-entry - Record a new trade entry with AI prediction
    /// </summary>
    [HttpPost("trade/record-entry")]
    public async Task<IActionResult> RecordTradeEntry([FromBody] RecordTradeEntryRequest request)
    {
        try
        {
            var activeModel = await _modelVersionRepository.GetActiveModelAsync("AlphaModel");
            if (activeModel == null)
            {
                return BadRequest(new { Error = "No active AI model found" });
            }

            var outcome = await _outcomeService.RecordTradeEntryAsync(
                request.Prediction,
                request.EntryPrice,
                request.Quantity,
                activeModel.Version);

            return Ok(new
            {
                Success = true,
                TradeOutcomeId = outcome.Id,
                outcome.Symbol,
                outcome.Direction,
                outcome.EntryPrice,
                PredictedReturn = $"{outcome.PredictedReturn:F2}%",
                SuccessProbability = $"{outcome.PredictedSuccessProbability * 100:F1}%",
                MarketRegime = outcome.MarketRegimeAtEntry,
                Message = "Trade entry recorded successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording trade entry");
            return StatusCode(500, new { Error = "Failed to record trade", Details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/ai/trade/record-exit - Record trade exit and update outcome
    /// </summary>
    [HttpPost("trade/record-exit")]
    public async Task<IActionResult> RecordTradeExit([FromBody] RecordTradeExitRequest request)
    {
        try
        {
            var outcome = await _outcomeService.RecordTradeExitAsync(
                request.TradeOutcomeId,
                request.ExitPrice,
                request.ExitReason);

            return Ok(new
            {
                Success = true,
                TradeOutcomeId = outcome.Id,
                outcome.Symbol,
                outcome.Direction,
                EntryPrice = outcome.EntryPrice,
                ExitPrice = outcome.ExitPrice,
                ProfitLoss = outcome.ProfitLoss,
                ProfitLossPercent = $"{outcome.ProfitLossPercent:F2}%",
                PredictedReturn = $"{outcome.PredictedReturn:F2}%",
                ActualReturn = $"{outcome.ActualReturn:F2}%",
                PredictionError = $"{outcome.PredictionError:F2}%",
                PredictionAccuracy = $"{outcome.PredictionAccuracyScore:F1}%",
                IsSuccessful = outcome.IsSuccessful,
                LearningTags = outcome.LearningTags,
                Message = "Trade exit recorded and model updated"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording trade exit");
            return StatusCode(500, new { Error = "Failed to record exit", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/performance/monitor - Monitor performance and check if retraining needed
    /// </summary>
    [HttpGet("performance/monitor")]
    public async Task<IActionResult> MonitorPerformance()
    {
        try
        {
            var result = await _performanceMonitor.MonitorAndActAsync();

            return Ok(new
            {
                result.ModelVersion,
                result.Status,
                result.RetrainingTriggered,
                result.PerformanceIssues,
                result.NewModelVersion,
                Metrics = result.Metrics != null ? new
                {
                    Last7Days = new
                    {
                        WinRate = $"{result.Metrics.WinRate7d:F2}%",
                        ProfitFactor = result.Metrics.ProfitFactor7d,
                        SharpeRatio = result.Metrics.SharpeRatio7d,
                        TotalPnL = result.Metrics.TotalPnL7d,
                        TotalTrades = result.Metrics.TotalTrades7d
                    },
                    Last30Days = new
                    {
                        WinRate = $"{result.Metrics.WinRate30d:F2}%",
                        SharpeRatio = result.Metrics.SharpeRatio30d,
                        MaxDrawdown = result.Metrics.MaxDrawdown30d,
                        TotalPnL = result.Metrics.TotalPnL30d,
                        TotalTrades = result.Metrics.TotalTrades30d
                    }
                } : null,
                result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring performance");
            return StatusCode(500, new { Error = "Performance monitoring failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/ai/optimize-factors - Trigger reinforcement learning factor optimization
    /// </summary>
    [HttpPost("optimize-factors")]
    public async Task<IActionResult> OptimizeFactors()
    {
        try
        {
            var result = await _reinforcementLearning.OptimizeFactorWeightsAsync();

            if (!result.Success)
            {
                return Ok(new
                {
                    Success = false,
                    result.Message
                });
            }

            // Calculate weight changes
            var changes = result.NewWeights.Select(kv => new
            {
                Factor = kv.Key,
                OldWeight = $"{result.OldWeights[kv.Key] * 100:F1}%",
                NewWeight = $"{kv.Value * 100:F1}%",
                Change = $"{(kv.Value - result.OldWeights[kv.Key]) * 100:+0.0;-0.0}%",
                Performance = result.PerformanceMetrics.ContainsKey(kv.Key) ? new
                {
                    WinRate = $"{result.PerformanceMetrics[kv.Key].WinRate:F1}%",
                    AvgReturn = $"{result.PerformanceMetrics[kv.Key].AvgReturn:F2}%",
                    TradeCount = result.PerformanceMetrics[kv.Key].TradeCount
                } : null
            }).ToList();

            return Ok(new
            {
                Success = true,
                AnalyzedTrades = result.AnalyzedTrades,
                WeightAdjustments = changes,
                result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error optimizing factors");
            return StatusCode(500, new { Error = "Factor optimization failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/trade-outcomes - Get trade outcome history
    /// </summary>
    [HttpGet("trade-outcomes")]
    public async Task<IActionResult> GetTradeOutcomes(
        [FromQuery] string? status = null,
        [FromQuery] string? modelVersion = null,
        [FromQuery] int? days = 30)
    {
        try
        {
            List<TradeOutcome> outcomes;

            if (status == "OPEN")
            {
                outcomes = await _outcomeRepository.GetOpenTradesAsync();
            }
            else
            {
                var startDate = days.HasValue 
                    ? DateTimeOffset.UtcNow.AddDays(-days.Value) 
                    : (DateTimeOffset?)null;
                
                outcomes = await _outcomeRepository.GetClosedTradesAsync(startDate);
            }

            if (!string.IsNullOrEmpty(modelVersion))
            {
                outcomes = outcomes.Where(o => o.ModelVersion == modelVersion).ToList();
            }

            var result = outcomes.Select(o => new
            {
                o.Id,
                o.Symbol,
                o.Direction,
                o.EntryTime,
                o.ExitTime,
                o.EntryPrice,
                o.ExitPrice,
                PredictedReturn = $"{o.PredictedReturn:F2}%",
                ActualReturn = o.ActualReturn.HasValue ? $"{o.ActualReturn:F2}%" : null,
                ProfitLoss = o.ProfitLoss,
                ProfitLossPercent = o.ProfitLossPercent.HasValue ? $"{o.ProfitLossPercent:F2}%" : null,
                o.IsSuccessful,
                PredictionAccuracy = o.PredictionAccuracyScore.HasValue ? $"{o.PredictionAccuracyScore:F1}%" : null,
                o.MarketRegimeAtEntry,
                o.Status,
                o.ModelVersion
            }).ToList();

            return Ok(new
            {
                TotalOutcomes = result.Count,
                Outcomes = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving trade outcomes");
            return StatusCode(500, new { Error = "Error retrieving outcomes", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/factor-performance - Get factor performance tracking history
    /// </summary>
    [HttpGet("factor-performance")]
    public async Task<IActionResult> GetFactorPerformance([FromQuery] int days = 30)
    {
        try
        {
            var startDate = DateTimeOffset.UtcNow.AddDays(-days);
            
            var tracking = await _outcomeRepository.GetClosedTradesAsync(
                startDate, cancellationToken: default);

            // Get current weights
            var currentWeights = _reinforcementLearning.GetCurrentWeights();

            return Ok(new
            {
                CurrentWeights = currentWeights.Select(kv => new
                {
                    Factor = kv.Key,
                    Weight = $"{kv.Value * 100:F1}%"
                }),
                PeriodDays = days,
                TotalTrades = tracking.Count,
                Message = "Factor performance data retrieved"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving factor performance");
            return StatusCode(500, new { Error = "Error retrieving factor performance", Details = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/ai/dashboard - Comprehensive AI system dashboard
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var activeModel = await _modelVersionRepository.GetActiveModelAsync("AlphaModel");
            var metrics = activeModel != null 
                ? await _performanceMonitor.GetCurrentPerformanceMetricsAsync(activeModel.Version)
                : null;
            var regime = await _regimeService.DetectRegimeAsync();
            var factorWeights = _reinforcementLearning.GetCurrentWeights();
            var openTrades = await _outcomeRepository.GetOpenTradesAsync();

            return Ok(new
            {
                ModelInfo = activeModel != null ? new
                {
                    Version = activeModel.Version,
                    Status = activeModel.Status,
                    TrainingDate = activeModel.TrainingDate,
                    TotalPredictions = activeModel.TotalPredictions,
                    ProductionAccuracy = $"{activeModel.ProductionAccuracy:F2}%"
                } : null,
                
                Performance = metrics != null ? new
                {
                    WinRate7d = $"{metrics.WinRate7d:F2}%",
                    SharpeRatio7d = metrics.SharpeRatio7d,
                    TotalPnL7d = metrics.TotalPnL7d,
                    TotalTrades7d = metrics.TotalTrades7d
                } : null,
                
                MarketRegime = new
                {
                    Regime = regime.Regime.ToString(),
                    Confidence = $"{regime.Confidence * 100:F1}%",
                    PreferredStrategies = regime.Guidance.PreferredStrategies,
                    RecommendedExposure = $"{regime.Guidance.RecommendedExposure * 100:F0}%"
                },
                
                FactorWeights = factorWeights,
                
                ActiveTrades = new
                {
                    Count = openTrades.Count,
                    TotalExposure = openTrades.Sum(t => t.EntryPrice * t.Quantity)
                },
                
                LastUpdated = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI dashboard");
            return StatusCode(500, new { Error = "Dashboard generation failed", Details = ex.Message });
        }
    }
}

// Request DTOs
public class PredictTradesRequest
{
    public List<(int Id, string Symbol, string Sector)> Instruments { get; set; } = new();
    public int? TopN { get; set; } = 20;
    public float? MinProbability { get; set; } = 0.6f;
}

public class RetrainRequest
{
    public bool Force { get; set; }
    public string? Reason { get; set; }
}

public class RollbackRequest
{
    public string Version { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class RecordTradeEntryRequest
{
    public AIAlphaPrediction Prediction { get; set; } = null!;
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
}

public class RecordTradeExitRequest
{
    public long TradeOutcomeId { get; set; }
    public decimal ExitPrice { get; set; }
    public string ExitReason { get; set; } = string.Empty;
}