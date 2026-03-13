using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using TradingSystem.AI.Models;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.AI.Services;

/// <summary>
/// Automated ML model training pipeline with continuous learning
/// </summary>
public class ModelTrainingPipeline
{
    private readonly ITradeOutcomeRepository _outcomeRepository;
    private readonly IFeatureStoreRepository _featureStore;
    private readonly IAIModelVersionRepository _modelVersionRepository;
    private readonly MetaFactorService _metaFactorService;
    private readonly TrainingDatasetService _datasetService;
    private readonly ILogger<ModelTrainingPipeline> _logger;
    private readonly MLContext _mlContext;
    private readonly string _modelsPath;

    // Performance thresholds for retraining
    private const float MIN_WIN_RATE = 55f;
    private const float MIN_SHARPE_RATIO = 0.5f;
    private const float MAX_PREDICTION_ERROR = 5f;

    public ModelTrainingPipeline(
        ITradeOutcomeRepository outcomeRepository,
        IFeatureStoreRepository featureStore,
        IAIModelVersionRepository modelVersionRepository,
        MetaFactorService metaFactorService,
        TrainingDatasetService datasetService,
        ILogger<ModelTrainingPipeline> logger)
    {
        _outcomeRepository = outcomeRepository;
        _featureStore = featureStore;
        _modelVersionRepository = modelVersionRepository;
        _metaFactorService = metaFactorService;
        _datasetService = datasetService;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
        _modelsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
        
        Directory.CreateDirectory(_modelsPath);
    }

    /// <summary>
    /// Execute complete training pipeline
    /// </summary>
    public async Task<ModelTrainingResult> ExecuteTrainingPipelineAsync(
        bool forceRetrain = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting model training pipeline (Force={Force})", forceRetrain);
        
        var startTime = DateTimeOffset.UtcNow;

        try
        {
            // Step 1: Check if retraining is needed
            if (!forceRetrain && !await ShouldRetrainAsync(cancellationToken))
            {
                _logger.LogInformation("Model performance is acceptable. Skipping retraining.");
                return new ModelTrainingResult
                {
                    Success = false,
                    Message = "Retraining not required - model performance is acceptable"
                };
            }

            // Step 2: Collect training data from trade outcomes
            var trainingData = await CollectTrainingDataAsync(cancellationToken);
            
            if (trainingData.Count < 100)
            {
                _logger.LogWarning("Insufficient training data: {Count} samples. Need at least 100.", 
                    trainingData.Count);
                return new ModelTrainingResult
                {
                    Success = false,
                    Message = $"Insufficient training data: {trainingData.Count} samples"
                };
            }

            // Step 3: Train new model
            var (model, metrics) = TrainModel(trainingData);

            // Step 4: Create new model version
            var newVersion = await CreateModelVersionAsync(
                trainingData.Count,
                metrics,
                startTime,
                cancellationToken);

            // Step 5: Save model to disk
            SaveModel(model, newVersion.Version);

            // Step 6: Validate new model performance
            var isValid = ValidateModelPerformance(metrics);

            if (isValid)
            {
                // Activate new model
                await _modelVersionRepository.ActivateModelAsync(newVersion.Id, cancellationToken);
                
                _logger.LogInformation(
                    "Model training completed successfully. Version: {Version}, Accuracy: {Accuracy}%, Win Rate: {WinRate}%",
                    newVersion.Version, metrics.Accuracy * 100, metrics.WinRate);
            }
            else
            {
                newVersion.Status = "TESTING";
                newVersion.DeprecationReason = "Failed validation thresholds";
                await _modelVersionRepository.UpdateAsync(newVersion, cancellationToken);
                
                _logger.LogWarning("New model failed validation. Keeping previous model active.");
            }

            var duration = DateTimeOffset.UtcNow - startTime;

            return new ModelTrainingResult
            {
                Success = true,
                ModelVersion = newVersion.Version,
                TrainingDatasetSize = trainingData.Count,
                ValidationAccuracy = metrics.Accuracy,
                WinRate = metrics.WinRate,
                SharpeRatio = metrics.SharpeRatio,
                TrainingDuration = duration,
                IsActivated = isValid,
                Message = isValid 
                    ? "Model trained and activated successfully" 
                    : "Model trained but failed validation"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model training pipeline");
            return new ModelTrainingResult
            {
                Success = false,
                Message = $"Training failed: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Check if model needs retraining based on performance
    /// </summary>
    private async Task<bool> ShouldRetrainAsync(CancellationToken cancellationToken)
    {
        var activeModel = await _modelVersionRepository.GetActiveModelAsync("AlphaModel", cancellationToken);
        if (activeModel == null)
        {
            _logger.LogInformation("No active model found. Retraining required.");
            return true;
        }

        // Check last training date
        var daysSinceTraining = (DateTimeOffset.UtcNow - activeModel.TrainingDate).TotalDays;
        if (daysSinceTraining > 7)
        {
            _logger.LogInformation("Model is {Days} days old. Retraining required.", (int)daysSinceTraining);
            return true;
        }

        // Check performance degradation
        var recentStats = await _outcomeRepository.GetStatisticsAsync(
            activeModel.Version,
            DateTimeOffset.UtcNow.AddDays(-7),
            cancellationToken: cancellationToken);

        if (recentStats.TotalTrades < 10)
        {
            _logger.LogInformation("Insufficient recent trades for performance assessment.");
            return false;
        }

        var performanceDegraded = 
            recentStats.WinRate < MIN_WIN_RATE ||
            recentStats.SharpeRatio < MIN_SHARPE_RATIO ||
            recentStats.AveragePredictionError > MAX_PREDICTION_ERROR;

        if (performanceDegraded)
        {
            _logger.LogWarning(
                "Performance degradation detected: WinRate={WinRate}%, Sharpe={Sharpe}, AvgError={Error}",
                recentStats.WinRate, recentStats.SharpeRatio, recentStats.AveragePredictionError);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Collect and prepare training data from trade outcomes
    /// </summary>
    private async Task<List<MetaFactorTrainingData>> CollectTrainingDataAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Collecting training data from trade outcomes...");

        var outcomes = await _outcomeRepository.GetClosedTradesAsync(
            DateTimeOffset.UtcNow.AddDays(-180), // Last 6 months
            cancellationToken: cancellationToken);

        var trainingData = new List<MetaFactorTrainingData>();

        foreach (var outcome in outcomes)
        {
            if (!outcome.IsSuccessful.HasValue || string.IsNullOrEmpty(outcome.MetaFactorsJson))
                continue;

            try
            {
                var metaFactorsDict = JsonSerializer.Deserialize<Dictionary<string, float>>(
                    outcome.MetaFactorsJson) ?? new Dictionary<string, float>();

                var data = new MetaFactorTrainingData
                {
                    // Meta-factors
                    MomentumMetaFactor = metaFactorsDict.GetValueOrDefault("MomentumMetaFactor", 0f),
                    TrendMetaFactor = metaFactorsDict.GetValueOrDefault("TrendMetaFactor", 0f),
                    VolatilityMetaFactor = metaFactorsDict.GetValueOrDefault("VolatilityMetaFactor", 0f),
                    LiquidityMetaFactor = metaFactorsDict.GetValueOrDefault("LiquidityMetaFactor", 0f),
                    RelativeStrengthMetaFactor = metaFactorsDict.GetValueOrDefault("RelativeStrengthMetaFactor", 0f),
                    SentimentMetaFactor = metaFactorsDict.GetValueOrDefault("SentimentMetaFactor", 0f),
                    RiskMetaFactor = metaFactorsDict.GetValueOrDefault("RiskMetaFactor", 0f),
                    
                    // Market regime encoding
                    MarketRegime = EncodeMarketRegime(outcome.MarketRegimeAtEntry),
                    RegimeConfidence = outcome.RegimeConfidence,
                    
                    // Actual outcome
                    ActualReturn = outcome.ActualReturn.HasValue ? (float)outcome.ActualReturn.Value : 0f,
                    IsSuccessful = outcome.IsSuccessful.Value
                };

                trainingData.Add(data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing trade outcome {Id}", outcome.Id);
            }
        }

        _logger.LogInformation("Collected {Count} training samples", trainingData.Count);
        return trainingData;
    }

    /// <summary>
    /// Train ML model using meta-factors
    /// </summary>
    private (ITransformer Model, ModelMetrics Metrics) TrainModel(
        List<MetaFactorTrainingData> trainingData)
    {
        _logger.LogInformation("Training model with {Count} samples...", trainingData.Count);

        var dataView = _mlContext.Data.LoadFromEnumerable(trainingData);
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

        // Define training pipeline with meta-factors
        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(MetaFactorTrainingData.MomentumMetaFactor),
                nameof(MetaFactorTrainingData.TrendMetaFactor),
                nameof(MetaFactorTrainingData.VolatilityMetaFactor),
                nameof(MetaFactorTrainingData.LiquidityMetaFactor),
                nameof(MetaFactorTrainingData.RelativeStrengthMetaFactor),
                nameof(MetaFactorTrainingData.SentimentMetaFactor),
                nameof(MetaFactorTrainingData.RiskMetaFactor),
                nameof(MetaFactorTrainingData.MarketRegime),
                nameof(MetaFactorTrainingData.RegimeConfidence))
            .Append(_mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: nameof(MetaFactorTrainingData.IsSuccessful),
                featureColumnName: "Features",
                numberOfLeaves: 50,
                numberOfTrees: 150,
                minimumExampleCountPerLeaf: 5,
                learningRate: 0.1));

        // Train model
        var model = pipeline.Fit(split.TrainSet);

        // Evaluate model
        var predictions = model.Transform(split.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, 
            labelColumnName: nameof(MetaFactorTrainingData.IsSuccessful));

        // Calculate additional metrics
        var testData = _mlContext.Data.CreateEnumerable<MetaFactorTrainingData>(
            split.TestSet, reuseRowObject: false).ToList();
        var predictedData = _mlContext.Data.CreateEnumerable<MetaFactorPrediction>(
            predictions, reuseRowObject: false).ToList();

        var winRate = CalculateWinRate(testData, predictedData);
        var sharpeRatio = CalculateSharpeRatio(testData, predictedData);
        var avgPredictionError = CalculateAvgPredictionError(testData, predictedData);

        var modelMetrics = new ModelMetrics
        {
            Accuracy = (float)metrics.Accuracy,
            AUC = (float)metrics.AreaUnderRocCurve,
            F1Score = (float)metrics.F1Score,
            Precision = (float)metrics.PositivePrecision,
            Recall = (float)metrics.PositiveRecall,
            WinRate = winRate,
            SharpeRatio = sharpeRatio,
            AvgPredictionError = avgPredictionError
        };

        _logger.LogInformation(
            "Model training completed: Accuracy={Accuracy}%, AUC={AUC}, WinRate={WinRate}%",
            modelMetrics.Accuracy * 100, modelMetrics.AUC, modelMetrics.WinRate);

        return (model, modelMetrics);
    }

    /// <summary>
    /// Create new model version record
    /// </summary>
    private async Task<AIModelVersion> CreateModelVersionAsync(
        int datasetSize,
        ModelMetrics metrics,
        DateTimeOffset startTime,
        CancellationToken cancellationToken)
    {
        var version = GenerateVersionNumber();
        var duration = DateTimeOffset.UtcNow - startTime;

        var modelVersion = new AIModelVersion
        {
            Version = version,
            ModelType = "AlphaModel",
            TrainingDate = DateTimeOffset.UtcNow,
            TrainingDatasetSize = (int)(datasetSize * 0.8), // 80% train
            ValidationDatasetSize = (int)(datasetSize * 0.2), // 20% test
            TrainingDuration = $"{duration.TotalMinutes:F1} minutes",
            
            // Hyperparameters
            HyperparametersJson = JsonSerializer.Serialize(new
            {
                NumberOfTrees = 150,
                NumberOfLeaves = 50,
                LearningRate = 0.1,
                MinimumExampleCountPerLeaf = 5
            }),
            
            // Training metrics
            TrainingAccuracy = metrics.Accuracy,
            ValidationAccuracy = metrics.Accuracy,
            WinRate = metrics.WinRate,
            ProfitFactor = 0f, // Will be updated in production
            SharpeRatio = metrics.SharpeRatio,
            MaxDrawdown = 0f, // Will be updated in production
            AveragePredictionError = metrics.AvgPredictionError,
            
            // Status
            Status = "TESTING",
            IsActive = false,
            
            // Paths
            ModelFilePath = Path.Combine(_modelsPath, $"alpha_model_{version}.zip"),
            
            ChangeLog = $"Trained on {datasetSize} samples with {metrics.Accuracy * 100:F1}% accuracy",
            ImprovementNotes = new List<string>
            {
                $"Training accuracy: {metrics.Accuracy * 100:F1}%",
                $"AUC: {metrics.AUC:F3}",
                $"Win rate: {metrics.WinRate:F1}%",
                $"Sharpe ratio: {metrics.SharpeRatio:F2}"
            }
        };

        return await _modelVersionRepository.CreateAsync(modelVersion, cancellationToken);
    }

    /// <summary>
    /// Save trained model to disk
    /// </summary>
    private void SaveModel(ITransformer model, string version)
    {
        var modelPath = Path.Combine(_modelsPath, $"alpha_model_{version}.zip");
        _mlContext.Model.Save(model, null, modelPath);
        _logger.LogInformation("Model saved to {Path}", modelPath);
    }

    /// <summary>
    /// Validate if new model meets minimum performance standards
    /// </summary>
    private bool ValidateModelPerformance(ModelMetrics metrics)
    {
        var isValid = 
            metrics.Accuracy >= 0.60f &&
            metrics.AUC >= 0.65f &&
            metrics.WinRate >= MIN_WIN_RATE &&
            metrics.SharpeRatio >= MIN_SHARPE_RATIO;

        _logger.LogInformation(
            "Model validation: Accuracy={Acc} (min 60%), AUC={AUC} (min 65%), WinRate={Win}% (min {MinWin}%), Sharpe={Sharpe} (min {MinSharpe}) - Result: {Result}",
            metrics.Accuracy * 100, metrics.AUC, metrics.WinRate, MIN_WIN_RATE, 
            metrics.SharpeRatio, MIN_SHARPE_RATIO, isValid ? "PASS" : "FAIL");

        return isValid;
    }

    private string GenerateVersionNumber()
    {
        var now = DateTimeOffset.UtcNow;
        return $"v{now:yyyyMMdd}.{now:HHmm}";
    }

    private float EncodeMarketRegime(string regime)
    {
        return regime switch
        {
            "BULL_MARKET" => 1.0f,
            "BEAR_MARKET" => -1.0f,
            "SIDEWAYS_MARKET" => 0.0f,
            "HIGH_VOLATILITY_MARKET" => 0.5f,
            "LOW_LIQUIDITY_MARKET" => -0.5f,
            _ => 0.0f
        };
    }

    private float CalculateWinRate(
        List<MetaFactorTrainingData> actual,
        List<MetaFactorPrediction> predicted)
    {
        var correct = 0;
        for (int i = 0; i < actual.Count; i++)
        {
            if (actual[i].IsSuccessful == predicted[i].PredictedLabel)
                correct++;
        }
        return (float)correct / actual.Count * 100;
    }

    private float CalculateSharpeRatio(
        List<MetaFactorTrainingData> actual,
        List<MetaFactorPrediction> predicted)
    {
        var returns = new List<float>();
        
        for (int i = 0; i < actual.Count; i++)
        {
            // Simulated return: actual return if we took the trade based on prediction
            if (predicted[i].Probability > 0.6f)
            {
                returns.Add(actual[i].ActualReturn);
            }
        }

        if (!returns.Any()) return 0f;

        var avgReturn = returns.Average();
        var stdDev = (float)Math.Sqrt(returns.Select(r => Math.Pow(r - avgReturn, 2)).Average());

        return stdDev > 0 ? (avgReturn - 0.05f) / stdDev : 0f;
    }

    private float CalculateAvgPredictionError(
        List<MetaFactorTrainingData> actual,
        List<MetaFactorPrediction> predicted)
    {
        var errors = new List<float>();
        
        for (int i = 0; i < actual.Count; i++)
        {
            var predictedReturn = predicted[i].Probability > 0.5f 
                ? Math.Abs(actual[i].ActualReturn) 
                : -Math.Abs(actual[i].ActualReturn);
            
            var error = Math.Abs(predictedReturn - actual[i].ActualReturn);
            errors.Add(error);
        }

        return errors.Any() ? errors.Average() : 0f;
    }
}

/// <summary>
/// ML training data using meta-factors
/// </summary>
public class MetaFactorTrainingData
{
    public float MomentumMetaFactor { get; set; }
    public float TrendMetaFactor { get; set; }
    public float VolatilityMetaFactor { get; set; }
    public float LiquidityMetaFactor { get; set; }
    public float RelativeStrengthMetaFactor { get; set; }
    public float SentimentMetaFactor { get; set; }
    public float RiskMetaFactor { get; set; }
    public float MarketRegime { get; set; }
    public float RegimeConfidence { get; set; }
    public float ActualReturn { get; set; }
    public bool IsSuccessful { get; set; }
}

/// <summary>
/// ML prediction output
/// </summary>
public class MetaFactorPrediction
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }
}

public class ModelMetrics
{
    public float Accuracy { get; set; }
    public float AUC { get; set; }
    public float F1Score { get; set; }
    public float Precision { get; set; }
    public float Recall { get; set; }
    public float WinRate { get; set; }
    public float SharpeRatio { get; set; }
    public float AvgPredictionError { get; set; }
}

public class ModelTrainingResult
{
    public bool Success { get; set; }
    public string? ModelVersion { get; set; }
    public int TrainingDatasetSize { get; set; }
    public float ValidationAccuracy { get; set; }
    public float WinRate { get; set; }
    public float SharpeRatio { get; set; }
    public TimeSpan TrainingDuration { get; set; }
    public bool IsActivated { get; set; }
    public string Message { get; set; } = string.Empty;
}