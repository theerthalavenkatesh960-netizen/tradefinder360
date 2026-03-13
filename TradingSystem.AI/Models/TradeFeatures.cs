using Microsoft.ML.Data;

namespace TradingSystem.AI.Models;

/// <summary>
/// Features used for ML model training and prediction
/// </summary>
public class TradeFeatures
{
    // Technical Indicators
    [LoadColumn(0)]
    public float RSI { get; set; }

    [LoadColumn(1)]
    public float MACD { get; set; }

    [LoadColumn(2)]
    public float MACDSignal { get; set; }

    [LoadColumn(3)]
    public float MACDHistogram { get; set; }

    [LoadColumn(4)]
    public float EMAFast { get; set; }

    [LoadColumn(5)]
    public float EMASlow { get; set; }

    [LoadColumn(6)]
    public float ADX { get; set; }

    [LoadColumn(7)]
    public float ATR { get; set; }

    // Volume Indicators
    [LoadColumn(8)]
    public float VolumeRatio { get; set; } // Current volume / Average volume

    [LoadColumn(9)]
    public float VolumeMA { get; set; }

    [LoadColumn(10)]
    public float VWAP { get; set; }

    // Volatility Indicators
    [LoadColumn(11)]
    public float BollingerWidth { get; set; }

    [LoadColumn(12)]
    public float BollingerPosition { get; set; } // Where price is relative to bands

    [LoadColumn(13)]
    public float HistoricalVolatility { get; set; }

    // Price Action
    [LoadColumn(14)]
    public float PriceChangePercent { get; set; }

    [LoadColumn(15)]
    public float PriceToEMAFast { get; set; }

    [LoadColumn(16)]
    public float PriceToEMASlow { get; set; }

    // Market Sentiment
    [LoadColumn(17)]
    public float MarketSentimentScore { get; set; } // -100 to 100

    [LoadColumn(18)]
    public float MarketVolatilityIndex { get; set; } // VIX

    [LoadColumn(19)]
    public float MarketBreadth { get; set; }

    // Risk Metrics
    [LoadColumn(20)]
    public float RiskRewardRatio { get; set; }

    [LoadColumn(21)]
    public float StopLossDistance { get; set; } // Percentage from entry

    // Strategy Signals
    [LoadColumn(22)]
    public float StrategyScore { get; set; } // 0-100

    [LoadColumn(23)]
    public float StrategyConfidence { get; set; } // 0-100

    // Label (for training)
    [LoadColumn(24)]
    public bool IsSuccessful { get; set; } // True if trade was profitable
}

/// <summary>
/// Prediction output from ML model
/// </summary>
public class TradePrediction
{
    [ColumnName("PredictedLabel")]
    public bool IsSuccessful { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }
}

/// <summary>
/// AI-enhanced trade recommendation
/// </summary>
public class AITradeRecommendation
{
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string InstrumentName { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;

    // Signal details
    public string Direction { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal Target { get; set; }
    public decimal RiskRewardRatio { get; set; }

    // AI Predictions
    public float SuccessProbability { get; set; } // 0-1 (0-100%)
    public float AIScore { get; set; } // ML model confidence score
    public string PredictionConfidence { get; set; } = string.Empty; // HIGH, MEDIUM, LOW

    // Combined Scoring
    public decimal StrategyScore { get; set; }
    public decimal StrategyConfidence { get; set; }
    public decimal CompositeScore { get; set; } // Weighted combination of AI + Strategy

    // Feature Importance
    public Dictionary<string, float> TopFeatures { get; set; } = new();

    // Trade Details
    public string Strategy { get; set; } = string.Empty;
    public List<string> Signals { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;

    // Market Context
    public decimal MarketSentiment { get; set; }
    public string MarketCondition { get; set; } = string.Empty;

    // Risk Assessment
    public string RiskLevel { get; set; } = string.Empty; // LOW, MEDIUM, HIGH
    public List<string> RiskFactors { get; set; } = new();
    public List<string> OpportunityFactors { get; set; } = new();
}