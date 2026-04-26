using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingSystem.WorkerService.HostedServices;

public class PortfolioNewsIngestionHostedService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortfolioNewsIngestionHostedService> _logger;

    private DateOnly? _lastMorningRunDate;
    private string _lastHourlySlot = string.Empty;

    public PortfolioNewsIngestionHostedService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PortfolioNewsIngestionHostedService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = bool.TryParse(_configuration["NewsIngestion:Enabled"], out var parsedEnabled)
            ? parsedEnabled
            : true;

        if (!enabled)
        {
            _logger.LogInformation("News ingestion worker is disabled by configuration.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        _logger.LogInformation("News ingestion worker started in WorkerService.");

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ExecuteTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "News ingestion tick failed.");
            }
        }

        _logger.LogInformation("News ingestion worker stopped.");
    }

    private async Task ExecuteTickAsync(CancellationToken cancellationToken)
    {
        var timezone = ResolveTimeZone(_configuration["NewsIngestion:TimeZone"] ?? "Asia/Kolkata");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);

        if (ShouldRunMorning(localNow))
        {
            var count = await TriggerIngestionAsync("morning", cancellationToken);
            _lastMorningRunDate = DateOnly.FromDateTime(localNow);
            _logger.LogInformation("Morning news ingestion completed with {Count} records.", count);
        }

        if (ShouldRunHourly(localNow))
        {
            var count = await TriggerIngestionAsync("hourly", cancellationToken);
            _lastHourlySlot = BuildHourlySlot(localNow);
            _logger.LogInformation("Hourly news ingestion completed with {Count} records.", count);
        }
    }

    private async Task<int> TriggerIngestionAsync(string mode, CancellationToken cancellationToken)
    {
        var baseUrl = (_configuration["InternalApi:BaseUrl"] ?? "http://localhost:61578").TrimEnd('/');
        var workerKey = _configuration["InternalApi:WorkerKey"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workerKey))
        {
            _logger.LogWarning("InternalApi:WorkerKey is missing. News ingestion trigger is skipped.");
            return 0;
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Worker-Key");
        client.DefaultRequestHeaders.Add("X-Worker-Key", workerKey);

        using var response = await client.PostAsync(
            $"{baseUrl}/api/portfolio-manager/internal/news/ingest?mode={mode}",
            JsonContent.Create(new { }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "News ingestion API call failed for mode {Mode}. Status: {Status}. Body: {Body}",
                mode,
                (int)response.StatusCode,
                body);
            return 0;
        }

        var payload = await response.Content.ReadFromJsonAsync<IngestionResponse>(cancellationToken: cancellationToken);
        return payload?.Inserted ?? 0;
    }

    private bool ShouldRunMorning(DateTime localNow)
    {
        var targetText = _configuration["NewsIngestion:MorningRunLocalTime"] ?? "08:45";
        if (!TimeOnly.TryParse(targetText, out var target))
        {
            target = new TimeOnly(8, 45);
        }

        var today = DateOnly.FromDateTime(localNow);
        if (_lastMorningRunDate == today)
        {
            return false;
        }

        return localNow.DayOfWeek is not DayOfWeek.Saturday
            and not DayOfWeek.Sunday
            && TimeOnly.FromDateTime(localNow) >= target;
    }

    private bool ShouldRunHourly(DateTime localNow)
    {
        if (localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        var startText = _configuration["NewsIngestion:MarketWindowStart"] ?? "09:00";
        var endText = _configuration["NewsIngestion:MarketWindowEnd"] ?? "15:30";

        if (!TimeOnly.TryParse(startText, out var start))
        {
            start = new TimeOnly(9, 0);
        }

        if (!TimeOnly.TryParse(endText, out var end))
        {
            end = new TimeOnly(15, 30);
        }

        var nowTime = TimeOnly.FromDateTime(localNow);
        if (nowTime < start || nowTime > end)
        {
            return false;
        }

        var currentSlot = BuildHourlySlot(localNow);
        return !string.Equals(currentSlot, _lastHourlySlot, StringComparison.Ordinal);
    }

    private static string BuildHourlySlot(DateTime localNow)
    {
        return $"{localNow:yyyy-MM-dd-HH}";
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

    private sealed class IngestionResponse
    {
        public string Mode { get; set; } = string.Empty;
        public int Inserted { get; set; }
        public DateTime At { get; set; }
    }
}
