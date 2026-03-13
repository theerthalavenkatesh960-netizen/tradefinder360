using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.Data.Services.Interfaces;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Background job to periodically update market sentiment analysis
/// Runs every 15 minutes during market hours
/// </summary>
[DisallowConcurrentExecution]
public class MarketSentimentUpdateJob : IJob
{
    private readonly IMarketSentimentService _sentimentService;
    private readonly ILogger<MarketSentimentUpdateJob> _logger;

    public MarketSentimentUpdateJob(
        IMarketSentimentService sentimentService,
        ILogger<MarketSentimentUpdateJob> logger)
    {
        _sentimentService = sentimentService;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("Starting market sentiment update job at {Time}", DateTime.UtcNow);

        try
        {
            // Check if market is open (9:15 AM to 3:30 PM IST)
            var istNow = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, 
                TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

            var marketOpenTime = new TimeSpan(9, 15, 0);
            var marketCloseTime = new TimeSpan(15, 30, 0);
            var currentTime = istNow.TimeOfDay;

            if (currentTime < marketOpenTime || currentTime > marketCloseTime)
            {
                _logger.LogInformation("Market is closed. Skipping sentiment update.");
                return;
            }

            // Perform sentiment analysis
            var marketContext = await _sentimentService.AnalyzeAndUpdateMarketSentimentAsync(
                context.CancellationToken);

            _logger.LogInformation(
                "Market sentiment updated: {Sentiment} (Score: {Score}, VIX: {VIX})",
                marketContext.Sentiment,
                marketContext.SentimentScore,
                marketContext.VolatilityIndex);

            _logger.LogInformation("Key factors: {Factors}", string.Join("; ", marketContext.KeyFactors));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating market sentiment");
            throw;
        }
    }
}