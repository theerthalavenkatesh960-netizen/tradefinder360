using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSystem.Data;

namespace TradingSystem.WorkerService.HostedServices;

public class PortfolioManagerSchedulerHostedService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PortfolioManagerSchedulerHostedService> _logger;

    public PortfolioManagerSchedulerHostedService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PortfolioManagerSchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = bool.TryParse(_configuration["PortfolioManager:EnableScheduler"], out var parsed)
            ? parsed
            : true;

        if (!enabled)
        {
            _logger.LogInformation("Portfolio manager scheduler is disabled by configuration.");
            return;
        }

        _logger.LogInformation("Portfolio manager scheduler started in WorkerService.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                if (!IsWithinMarketWindow(DateTime.UtcNow))
                {
                    continue;
                }

                await RunDueSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Portfolio scheduler tick failed.");
            }
        }

        _logger.LogInformation("Portfolio manager scheduler stopped.");
    }

    private async Task RunDueSessionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        var now = DateTime.UtcNow;
        var dueSessions = await dbContext.PortfolioManagerSessions
            .AsNoTracking()
            .Where(x => x.AutoRebalanceEnabled)
            .Where(x => x.NextRunAt == null || x.NextRunAt <= now)
            .OrderBy(x => x.NextRunAt)
            .Take(25)
            .Select(x => new { x.Id, x.UserId })
            .ToListAsync(cancellationToken);

        if (dueSessions.Count == 0)
        {
            return;
        }

        var baseUrl = (_configuration["InternalApi:BaseUrl"] ?? "http://localhost:61578").TrimEnd('/');
        var workerKey = _configuration["InternalApi:WorkerKey"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(workerKey))
        {
            _logger.LogWarning("InternalApi:WorkerKey is missing. Scheduler will skip execution.");
            return;
        }

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Remove("X-Worker-Key");
        client.DefaultRequestHeaders.Add("X-Worker-Key", workerKey);

        foreach (var session in dueSessions)
        {
            try
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{baseUrl}/api/portfolio-manager/sessions/{session.Id}/run");
                request.Headers.Add("X-User-Id", session.UserId);
                request.Content = JsonContent.Create(new { useLatestPreferences = true }, options: JsonOptions);

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Scheduled run failed for session {SessionId}, user {UserId}. Status: {Status}. Body: {Body}",
                        session.Id,
                        session.UserId,
                        (int)response.StatusCode,
                        body);
                    continue;
                }

                _logger.LogInformation(
                    "Executed scheduled run for session {SessionId} user {UserId}.",
                    session.Id,
                    session.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed scheduled run for session {SessionId} user {UserId}.", session.Id, session.UserId);
            }
        }
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
