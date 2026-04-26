using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Services;

namespace TradingSystem.Api.Controllers;

/// <summary>
/// REST API for portfolio fusion learning workflow
/// Exposes learning trigger, approval, rejection, and rollback endpoints
/// </summary>
[ApiController]
[Route("api/portfolio/learning")]
public class LearningController : ControllerBase
{
    private readonly PortfolioLearningOrchestrator _learningOrchestrator;
    private readonly ILogger<LearningController> _logger;

    public LearningController(
        PortfolioLearningOrchestrator learningOrchestrator,
        ILogger<LearningController> logger)
    {
        _learningOrchestrator = learningOrchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Trigger learning analysis
    /// Analyzes recent session performance and proposes adaptive config if needed
    /// 
    /// POST /api/portfolio/learning/trigger
    /// </summary>
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerLearning(
        [FromBody] TriggerLearningRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Learning trigger request: {Source}, sessions: {Sessions}",
                request.TriggerSource ?? "USER_MANUAL",
                request.SessionsToAnalyze ?? 5);

            var result = await _learningOrchestrator.TriggerLearningAsync(
                request.UserId ?? "default_user",
                request.TriggerSource ?? "USER_MANUAL",
                request.SessionsToAnalyze ?? 5,
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error triggering learning");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Approve a proposed learning config
    /// Makes the proposed config active; deactivates the previous one
    /// 
    /// POST /api/portfolio/learning/approve/{configId}
    /// </summary>
    [HttpPost("approve/{configId}")]
    public async Task<IActionResult> ApproveConfig(
        long configId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Approving config {ConfigId}", configId);

            var result = await _learningOrchestrator.ApproveConfigAsync(
                configId, cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Config not found: {ConfigId}", configId);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving config");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reject a proposed learning config
    /// Marks config as REJECTED; active config remains unchanged
    /// 
    /// POST /api/portfolio/learning/reject/{configId}
    /// </summary>
    [HttpPost("reject/{configId}")]
    public async Task<IActionResult> RejectConfig(
        long configId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Rejecting config {ConfigId}", configId);

            var result = await _learningOrchestrator.RejectConfigAsync(
                configId, cancellationToken);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Config not found: {ConfigId}", configId);
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting config");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Rollback active config to prior config
    /// Used if current config underperforms
    /// 
    /// POST /api/portfolio/learning/rollback
    /// </summary>
    [HttpPost("rollback")]
    public async Task<IActionResult> RollbackConfig(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Rollback requested");

            var result = await _learningOrchestrator.RollbackConfigAsync(cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Rollback failed: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollback");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get learning history
    /// Returns all learning iterations (proposed configs, approvals, rejections, rollbacks)
    /// 
    /// GET /api/portfolio/learning/history?limit=10
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetLearningHistory(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching learning history with limit {Limit}", limit);

            var history = await _learningOrchestrator.GetLearningHistoryAsync(
                limit, cancellationToken);

            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching history");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get current active fusion learning config
    /// Returns the config currently active in the trading engine
    /// 
    /// GET /api/portfolio/learning/current-config
    /// </summary>
    [HttpGet("current-config")]
    public async Task<IActionResult> GetCurrentConfig(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching current active config");

            var config = await _learningOrchestrator.GetCurrentConfigAsync(cancellationToken);

            if (config == null)
                return NotFound(new { message = "No active config found" });

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current config");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
