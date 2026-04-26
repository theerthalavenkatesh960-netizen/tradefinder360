using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Quartz;

namespace TradingSystem.WorkerService.Jobs;

[DisallowConcurrentExecution]
public class NewsIngestionJob : IJob
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NewsIngestionJob> _logger;

    public NewsIngestionJob(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<NewsIngestionJob> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var enabled = bool.TryParse(_configuration["NewsIngestion:Enabled"], out var parsedEnabled)
            ? parsedEnabled
            : true;

        if (!enabled)
        {
            return;
        }

        var timezone = ResolveTimeZone(_configuration["NewsIngestion:TimeZone"] ?? "Asia/Kolkata");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);

        if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return;
        }

        string? mode = null;
        // Morning baseline at 08:45 local time.
        if (localNow.Hour == 8 && localNow.Minute == 45)
        {
            mode = "morning";
        }

        // Hourly refresh on market hours at HH:00.
        if (localNow.Hour >= 9 && localNow.Hour <= 15 && localNow.Minute == 0)
        {
            mode = "hourly";
        }

        if (mode == null)
        {
            return;
        }

        var baseUrl = (_configuration["InternalApi:BaseUrl"] ?? "http://localhost:61578").TrimEnd('/');
        var workerKey = _configuration["InternalApi:WorkerKey"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workerKey))
        {
            _logger.LogWarning("NewsIngestionJob skipped: InternalApi:WorkerKey is missing.");
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
                $"{baseUrl}/api/portfolio-manager/internal/news/ingest?mode={mode}",
                JsonContent.Create(new { }),
                context.CancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(context.CancellationToken);
                _logger.LogWarning(
                    "NewsIngestionJob failed for mode {Mode}. Status: {Status}. Body: {Body}",
                    mode,
                    (int)response.StatusCode,
                    body);
                return;
            }

            _logger.LogInformation("NewsIngestionJob completed for mode {Mode} at {LocalTime}.", mode, localNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NewsIngestionJob failed for mode {Mode}.", mode);
        }
    }

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
