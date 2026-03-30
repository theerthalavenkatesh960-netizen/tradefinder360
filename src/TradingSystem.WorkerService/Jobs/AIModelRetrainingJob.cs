using Microsoft.Extensions.Logging;
using Quartz;
using TradingSystem.AI.Services;

namespace TradingSystem.WorkerService.Jobs;

/// <summary>
/// Background job for automated AI model retraining
/// Runs daily at 6 PM IST and weekly on Sunday
/// </summary>
[DisallowConcurrentExecution]
public class AIModelRetrainingJob : IJob
{
    private readonly ModelPerformanceMonitor _performanceMonitor;
    private readonly ReinforcementLearningService _reinforcementLearning;
    private readonly ILogger<AIModelRetrainingJob> _logger;

    public AIModelRetrainingJob(
        ModelPerformanceMonitor performanceMonitor,
        ReinforcementLearningService reinforcementLearning,
        ILogger<AIModelRetrainingJob> logger)
    {
        _performanceMonitor = performanceMonitor;
        _reinforcementLearning = reinforcementLearning;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        _logger.LogInformation("AI Model Retraining Job started");

        try
        {
            // Step 1: Monitor performance and retrain if needed
            var monitorResult = await _performanceMonitor.MonitorAndActAsync(
                context.CancellationToken);

            _logger.LogInformation(
                "Performance monitoring completed: Status={Status}, Retraining={Retrain}",
                monitorResult.Status, monitorResult.RetrainingTriggered);

            // Step 2: Optimize factor weights (runs weekly)
            var triggerTime = context.ScheduledFireTimeUtc?.DateTime ?? DateTime.UtcNow;
            if (triggerTime.DayOfWeek == DayOfWeek.Sunday)
            {
                _logger.LogInformation("Running weekly factor optimization...");
                
                var optimizationResult = await _reinforcementLearning.OptimizeFactorWeightsAsync(
                    context.CancellationToken);

                _logger.LogInformation(
                    "Factor optimization completed: {Trades} trades analyzed",
                    optimizationResult.AnalyzedTrades);
            }

            _logger.LogInformation("AI Model Retraining Job completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AI Model Retraining Job");
            throw;
        }
    }
}