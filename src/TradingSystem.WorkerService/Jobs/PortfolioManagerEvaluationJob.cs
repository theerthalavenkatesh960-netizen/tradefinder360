using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Data;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class PortfolioManagerEvaluationJob : IJob
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TradingDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortfolioManagerEvaluationJob> _logger;

    public PortfolioManagerEvaluationJob(
        TradingDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PortfolioManagerEvaluationJob> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var startedAt = DateTime.UtcNow;
        if (!IsWithinMarketWindow(startedAt))
        {
            _logger.LogInformation("PortfolioManagerEvaluationJob skipped: outside market window.");
            return;
        }

        var dueSessions = await _dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .Where(x => x.AutoRebalanceEnabled)
            .Where(x => x.NextRunAt == null || x.NextRunAt <= startedAt)
            .OrderBy(x => x.NextRunAt)
            .Take(50)
            .Select(x => new { x.Id, x.UserId })
            .ToListAsync(context.CancellationToken);

        if (dueSessions.Count == 0)
        {
            _logger.LogInformation("PortfolioManagerEvaluationJob: no due sessions.");
            return;
        }

        var baseUrl = (_configuration["InternalApi:BaseUrl"] ?? "http://localhost:61578").TrimEnd('/');
        var workerKey = _configuration["InternalApi:WorkerKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workerKey))
        {
            _logger.LogWarning("PortfolioManagerEvaluationJob skipped: InternalApi:WorkerKey is missing.");
            return;
        }

        var client = _httpClientFactory.CreateClient();
        var timeoutSeconds = int.TryParse(_configuration["InternalApi:RequestTimeoutSeconds"], out var parsedTimeoutSeconds)
            ? Math.Clamp(parsedTimeoutSeconds, 5, 300)
            : 45;
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.Remove("X-Worker-Key");
        client.DefaultRequestHeaders.Add("X-Worker-Key", workerKey);

        var successCount = 0;
        foreach (var session in dueSessions)
        {
            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{baseUrl}/api/portfolio-manager/sessions/{session.Id}/run");

                request.Headers.Add("X-User-Id", session.UserId);
                request.Content = JsonContent.Create(new { useLatestPreferences = true }, options: JsonOptions);

                using var response = await client.SendAsync(request, context.CancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
                    _logger.LogWarning(
                        "PortfolioManagerEvaluationJob failed for session {SessionId}. Status: {Status}. Body: {Body}",
                        session.Id,
                        (int)response.StatusCode,
                        body);
                    continue;
                }

                successCount += 1;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PortfolioManagerEvaluationJob failed for session {SessionId}.", session.Id);
            }
        }

        _logger.LogInformation(
            "PortfolioManagerEvaluationJob completed. Success: {Success}/{Total}. DurationMs: {Duration}",
            successCount,
            dueSessions.Count,
            (DateTime.UtcNow - startedAt).TotalMilliseconds);
    }

    private bool IsWithinMarketWindow(DateTime utcNow)
    {
        var timezoneId = _configuration["PortfolioManager:MarketTimeZone"] ?? "Asia/Kolkata";
        var startConfig = _configuration["PortfolioManager:HourlyCheckStart"] ?? "09:00";
        var endConfig = _configuration["PortfolioManager:HourlyCheckEnd"] ?? "15:30";

        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);
        if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        if (!TimeOnly.TryParse(startConfig, out var start))
        {
            start = new TimeOnly(9, 0);
        }

        if (!TimeOnly.TryParse(endConfig, out var end))
        {
            end = new TimeOnly(15, 30);
        }

        var current = TimeOnly.FromDateTime(localNow);
        return current >= start && current <= end;
    }
}
