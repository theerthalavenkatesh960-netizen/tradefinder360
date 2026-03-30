namespace TradingSystem.Core.Models;

/// <summary>
/// Tracks AI model versions and their performance
/// </summary>
public class AIModelVersion
{
    public int Id { get; set; }
    public string Version { get; set; } = string.Empty;  // e.g., "v1.2.5"
    public string ModelType { get; set; } = string.Empty;  // "AlphaModel", "TradePrediction"
    
    // Training metadata
    public DateTimeOffset TrainingDate { get; set; }
    public int TrainingDatasetSize { get; set; }
    public int ValidationDatasetSize { get; set; }
    public string TrainingDuration { get; set; } = string.Empty;
    
    // Model configuration
    public string HyperparametersJson { get; set; } = string.Empty;
    public string FeatureImportanceJson { get; set; } = string.Empty;
    
    // Performance metrics
    public float TrainingAccuracy { get; set; }
    public float ValidationAccuracy { get; set; }
    public float WinRate { get; set; }
    public float ProfitFactor { get; set; }
    public float SharpeRatio { get; set; }
    public float MaxDrawdown { get; set; }
    public float AveragePredictionError { get; set; }
    
    // Production metrics (updated as trades execute)
    public int TotalPredictions { get; set; }
    public int SuccessfulPredictions { get; set; }
    public float ProductionAccuracy { get; set; }
    public float ProductionSharpeRatio { get; set; }
    public decimal TotalPnL { get; set; }
    
    // Status
    public string Status { get; set; } = "TRAINING";  // TRAINING, TESTING, PRODUCTION, DEPRECATED
    public bool IsActive { get; set; }
    public string? DeprecationReason { get; set; }
    
    // File paths
    public string ModelFilePath { get; set; } = string.Empty;
    public string CheckpointPath { get; set; } = string.Empty;
    
    // Change log
    public string ChangeLog { get; set; } = string.Empty;
    public List<string> ImprovementNotes { get; set; } = new();
    
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
}