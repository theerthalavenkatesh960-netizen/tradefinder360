using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class PortfolioEventTriggerJob : IJob
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortfolioEventTriggerJob> _logger;

    public PortfolioEventTriggerJob(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PortfolioEventTriggerJob> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var startedAt = DateTime.UtcNow;
        if (!IsWithinMarketWindow(startedAt))
        {
            _logger.LogInformation("PortfolioEventTriggerJob skipped: outside market window.");
            return;
        }

        var baseUrl = (_configuration["InternalApi:BaseUrl"] ?? "http://localhost:61578").TrimEnd('/');
        var workerKey = _configuration["InternalApi:WorkerKey"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(workerKey))
        {
            _logger.LogWarning("PortfolioEventTriggerJob skipped: InternalApi:WorkerKey is missing.");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var timeoutSeconds = int.TryParse(_configuration["InternalApi:RequestTimeoutSeconds"], out var parsedTimeoutSeconds)
                ? Math.Clamp(parsedTimeoutSeconds, 5, 300)
                : 45;
            client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
            client.DefaultRequestHeaders.Remove("X-Worker-Key");
            client.DefaultRequestHeaders.Add("X-Worker-Key", workerKey);

            using var response = await client.PostAsync(
                $"{baseUrl}/api/portfolio-manager/internal/events/trigger",
                content: null,
                cancellationToken: context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
                _logger.LogWarning(
                    "PortfolioEventTriggerJob API call failed. Status: {Status}. Body: {Body}",
                    (int)response.StatusCode,
                    body);
                return;
            }

            var payload = await response.Content.ReadAsStringAsync(context.CancellationToken);
            _logger.LogInformation(
                "PortfolioEventTriggerJob completed. DurationMs: {Duration}. Payload: {Payload}",
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PortfolioEventTriggerJob failed.");
        }
    }

    private bool IsWithinMarketWindow(DateTime utcNow)
    {
        var timezoneId = _configuration["PortfolioManager:MarketTimeZone"] ?? "Asia/Kolkata";

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

        var current = TimeOnly.FromDateTime(localNow);
        return current >= new TimeOnly(9, 0) && current <= new TimeOnly(15, 30);
    }
}
