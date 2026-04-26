using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TradingSystem.Api.DTOs;
using TradingSystem.Api.Services;

namespace TradingSystem.Api.Controllers;

[ApiController]
[Route("api/portfolio-manager")]
public class PortfolioManagerController : ControllerBase
{
    private readonly IPortfolioManagerService _portfolioManagerService;
    private readonly INewsIngestionService _newsIngestionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortfolioManagerController> _logger;

    public PortfolioManagerController(
        IPortfolioManagerService portfolioManagerService,
        INewsIngestionService newsIngestionService,
        IConfiguration configuration,
        ILogger<PortfolioManagerController> logger)
    {
        _portfolioManagerService = portfolioManagerService;
        _newsIngestionService = newsIngestionService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("sessions")]
    [ProducesResponseType(typeof(PortfolioSessionSummaryDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<PortfolioSessionSummaryDto>> CreateSession(
        [FromBody] CreatePortfolioSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        try
        {
            var session = await _portfolioManagerService.CreateSessionAsync(userId, request, cancellationToken);
            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating portfolio manager session");
            return StatusCode(500, "Unable to create session.");
        }
    }

    [HttpGet("sessions")]
    [ProducesResponseType(typeof(List<PortfolioSessionSummaryDto>), 200)]
    public async Task<ActionResult<IReadOnlyList<PortfolioSessionSummaryDto>>> GetSessions(
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var sessions = await _portfolioManagerService.GetSessionsAsync(userId, cancellationToken);
        return Ok(sessions);
    }

    [HttpPut("sessions/{sessionId:long}")]
    [ProducesResponseType(typeof(PortfolioSessionSummaryDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioSessionSummaryDto>> UpdateSession(
        long sessionId,
        [FromBody] UpdatePortfolioSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        try
        {
            var session = await _portfolioManagerService.UpdateSessionAsync(userId, sessionId, request, cancellationToken);
            if (session == null)
            {
                return NotFound();
            }

            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("sessions/{sessionId:long}/clone")]
    [ProducesResponseType(typeof(PortfolioSessionSummaryDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioSessionSummaryDto>> CloneSession(
        long sessionId,
        [FromBody] ClonePortfolioSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var cloned = await _portfolioManagerService.CloneSessionAsync(userId, sessionId, request, cancellationToken);

        if (cloned == null)
        {
            return NotFound();
        }

        return Ok(cloned);
    }

    [HttpDelete("sessions/{sessionId:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var deleted = await _portfolioManagerService.DeleteSessionAsync(userId, sessionId, cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("sessions/{sessionId:long}")]
    [ProducesResponseType(typeof(PortfolioSessionDetailDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioSessionDetailDto>> GetSessionDetail(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var detail = await _portfolioManagerService.GetSessionDetailAsync(userId, sessionId, cancellationToken);

        if (detail == null)
        {
            return NotFound();
        }

        return Ok(detail);
    }

    [HttpPost("sessions/{sessionId:long}/run")]
    [ProducesResponseType(typeof(PortfolioRunResponseDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioRunResponseDto>> RunSession(
        long sessionId,
        [FromBody] RunPortfolioSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var response = await _portfolioManagerService.RunSessionAsync(userId, sessionId, cancellationToken);

        if (response == null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost("sessions/{sessionId:long}/start")]
    [ProducesResponseType(typeof(PortfolioSessionSummaryDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioSessionSummaryDto>> StartSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var session = await _portfolioManagerService.SetScheduledStateAsync(userId, sessionId, true, cancellationToken);

        if (session == null)
        {
            return NotFound();
        }

        return Ok(session);
    }

    [HttpPost("sessions/{sessionId:long}/stop")]
    [ProducesResponseType(typeof(PortfolioSessionSummaryDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<PortfolioSessionSummaryDto>> StopSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var session = await _portfolioManagerService.SetScheduledStateAsync(userId, sessionId, false, cancellationToken);

        if (session == null)
        {
            return NotFound();
        }

        return Ok(session);
    }

    [HttpGet("sessions/{sessionId:long}/export")]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ExportSession(
        long sessionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var bytes = await _portfolioManagerService.ExportSessionCsvAsync(userId, sessionId, cancellationToken);

        if (bytes == null)
        {
            return NotFound();
        }

        var fileName = $"portfolio-session-{sessionId}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return File(bytes, "text/csv", fileName);
    }

    [HttpGet("sessions/{sessionId:long}/news")]
    [ProducesResponseType(typeof(List<PortfolioNewsItemDto>), 200)]
    public async Task<ActionResult<IReadOnlyList<PortfolioNewsItemDto>>> GetSessionNews(
        long sessionId,
        [FromQuery] int hoursBack = 24,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized("User context missing.");
        }

        var rows = await _portfolioManagerService.GetSessionNewsAsync(userId, sessionId, hoursBack, limit, cancellationToken);
        return Ok(rows);
    }

    [HttpPost("internal/news/ingest")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<object>> TriggerNewsIngestion(
        [FromQuery] string mode = "hourly",
        CancellationToken cancellationToken = default)
    {
        if (!IsInternalWorkerAuthorized())
        {
            return Unauthorized("Invalid worker key.");
        }

        var normalizedMode = mode?.Trim().ToLowerInvariant() ?? "hourly";
        var inserted = normalizedMode == "morning"
            ? await _newsIngestionService.IngestMorningNewsAsync(cancellationToken)
            : await _newsIngestionService.IngestHourlyNewsAsync(cancellationToken);

        return Ok(new
        {
            mode = normalizedMode,
            inserted,
            at = DateTime.UtcNow
        });
    }

    [HttpPost("internal/events/trigger")]
    [ProducesResponseType(typeof(PortfolioEventTriggerResultDto), 200)]
    [ProducesResponseType(401)]
    public async Task<ActionResult<PortfolioEventTriggerResultDto>> TriggerEventDrivenRuns(
        CancellationToken cancellationToken = default)
    {
        if (!IsInternalWorkerAuthorized())
        {
            return Unauthorized("Invalid worker key.");
        }

        var result = await _portfolioManagerService.TriggerEventDrivenRunsAsync(cancellationToken);
        return Ok(result);
    }

    private bool TryGetUserId(out string userId)
    {
        userId = string.Empty;

        var fromClaims = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue("user_id");

        if (!string.IsNullOrWhiteSpace(fromClaims))
        {
            userId = fromClaims;
            return true;
        }

        if (Request.Headers.TryGetValue("X-User-Id", out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue.ToString()))
        {
            userId = headerValue.ToString();
            return true;
        }

        var allowDemoFallback = bool.TryParse(_configuration["Auth:AllowDemoUserFallback"], out var parsedAllowDemo)
            ? parsedAllowDemo
            : false;

        if (allowDemoFallback)
        {
            userId = "demo-user";
            return true;
        }

        return false;
    }

    private bool IsInternalWorkerAuthorized()
    {
        var configuredKey = _configuration["InternalApi:WorkerKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            return false;
        }

        if (!Request.Headers.TryGetValue("X-Worker-Key", out var providedKey))
        {
            return false;
        }

        return string.Equals(configuredKey, providedKey.ToString(), StringComparison.Ordinal);
    }
}
