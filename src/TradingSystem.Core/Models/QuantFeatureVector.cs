namespace TradingSystem.Core.Models;

/// <summary>
/// Comprehensive feature vector with 120+ quantitative factors
/// </summary>
public class QuantFeatureVector
{
    // Metadata
    public int InstrumentId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }

    // ========== MOMENTUM FACTORS (20) ==========
    public float Momentum_5D { get; set; }
    public float Momentum_10D { get; set; }
    public float Momentum_20D { get; set; }
    public float Momentum_30D { get; set; }
    public float Momentum_60D { get; set; }
    public float Momentum_90D { get; set; }
    public float Momentum_ROC_5 { get; set; }  // Rate of Change
    public float Momentum_ROC_10 { get; set; }
    public float Momentum_ROC_20 { get; set; }
    public float RSI_14 { get; set; }
    public float RSI_7 { get; set; }
    public float RSI_21 { get; set; }
    public float Stochastic_K { get; set; }
    public float Stochastic_D { get; set; }
    public float Williams_R { get; set; }
    public float CCI_20 { get; set; }  // Commodity Channel Index
    public float UltimateOscillator { get; set; }
    public float MACD_12_26 { get; set; }
    public float MACD_Signal { get; set; }
    public float MACD_Histogram { get; set; }

    // ========== TREND FACTORS (15) ==========
    public float Trend_Strength_ADX { get; set; }
    public float Trend_Direction_DI_Plus { get; set; }
    public float Trend_Direction_DI_Minus { get; set; }
    public float EMA_9 { get; set; }
    public float EMA_21 { get; set; }
    public float EMA_50 { get; set; }
    public float EMA_200 { get; set; }
    public float SMA_20 { get; set; }
    public float SMA_50 { get; set; }
    public float SMA_200 { get; set; }
    public float Price_To_SMA20_Ratio { get; set; }
    public float Price_To_SMA50_Ratio { get; set; }
    public float Price_To_SMA200_Ratio { get; set; }
    public float EMA_Crossover_Signal { get; set; }  // Fast/Slow crossover strength
    public float Trend_Consistency { get; set; }  // % of days in trend direction

    // ========== VOLATILITY FACTORS (15) ==========
    public float ATR_14 { get; set; }
    public float ATR_Percent { get; set; }  // ATR as % of price
    public float Bollinger_Width { get; set; }
    public float Bollinger_Position { get; set; }  // Where price is in bands
    public float Bollinger_BandWidth_Ratio { get; set; }
    public float Historical_Volatility_10D { get; set; }
    public float Historical_Volatility_20D { get; set; }
    public float Historical_Volatility_30D { get; set; }
    public float Parkinson_Volatility { get; set; }  // High-Low range volatility
    public float Garman_Klass_Volatility { get; set; }
    public float Volatility_Ratio_10_30 { get; set; }
    public float Keltner_Channel_Position { get; set; }
    public float Donchian_Channel_Position { get; set; }
    public float True_Range_Average { get; set; }
    public float Volatility_Trend { get; set; }  // Is volatility increasing?

    // ========== VOLUME & LIQUIDITY FACTORS (15) ==========
    public float Volume_Ratio_20D { get; set; }  // Current vs 20-day avg
    public float Volume_Ratio_5D { get; set; }
    public float Volume_SMA_20 { get; set; }
    public float Volume_Trend { get; set; }  // Volume increasing/decreasing
    public float VWAP { get; set; }
    public float Price_To_VWAP_Ratio { get; set; }
    public float OBV { get; set; }  // On-Balance Volume
    public float OBV_Trend { get; set; }
    public float Chaikin_Money_Flow { get; set; }
    public float Money_Flow_Index { get; set; }
    public float Volume_Weighted_Price_Change { get; set; }
    public float Accumulation_Distribution { get; set; }
    public float Ease_Of_Movement { get; set; }
    public float Volume_Price_Trend { get; set; }
    public float Dollar_Volume { get; set; }  // Price * Volume

    // ========== MEAN REVERSION FACTORS (10) ==========
    public float Z_Score_Price { get; set; }
    public float Z_Score_Returns { get; set; }
    public float Deviation_From_Mean_20D { get; set; }
    public float Deviation_From_Mean_50D { get; set; }
    public float Mean_Reversion_Speed { get; set; }
    public float Bollinger_Squeeze { get; set; }  // Low volatility indicator
    public float Price_Distance_To_VWAP { get; set; }
    public float Overbought_Oversold_Score { get; set; }
    public float Reversal_Probability { get; set; }
    public float Support_Resistance_Distance { get; set; }

    // ========== MARKET & MACRO FACTORS (10) ==========
    public float Market_Beta { get; set; }  // vs benchmark index
    public float Market_Correlation { get; set; }
    public float Sector_Relative_Strength { get; set; }
    public float Index_Correlation { get; set; }
    public float VIX_Level { get; set; }  // Volatility Index
    public float Market_Breadth { get; set; }
    public float Advance_Decline_Ratio { get; set; }
    public float Sector_Performance { get; set; }
    public float Market_Regime { get; set; }  // Bull/Bear/Neutral
    public float Macro_Sentiment_Score { get; set; }

    // ========== RELATIVE STRENGTH FACTORS (10) ==========
    public float RS_vs_Market { get; set; }
    public float RS_vs_Sector { get; set; }
    public float RS_Rank_Percentile { get; set; }
    public float RS_Momentum_3M { get; set; }
    public float RS_Momentum_6M { get; set; }
    public float RS_Momentum_12M { get; set; }
    public float Outperformance_Streak { get; set; }
    public float RS_Slope { get; set; }
    public float RS_Volatility { get; set; }
    public float Relative_Volume_Strength { get; set; }

    // ========== STATISTICAL FACTORS (10) ==========
    public float Skewness_Returns { get; set; }
    public float Kurtosis_Returns { get; set; }
    public float Sharpe_Ratio_30D { get; set; }
    public float Sortino_Ratio { get; set; }
    public float Max_Drawdown_30D { get; set; }
    public float Win_Rate_Historical { get; set; }
    public float Average_Win_Loss_Ratio { get; set; }
    public float Consecutive_Up_Days { get; set; }
    public float Consecutive_Down_Days { get; set; }
    public float Returns_Autocorrelation { get; set; }

    // ========== SENTIMENT FACTORS (10) ==========
    public float News_Sentiment_Score { get; set; }  // From your existing detector
    public float Social_Media_Sentiment { get; set; }  // If available
    public float Sentiment_Momentum { get; set; }
    public float Sentiment_Volatility { get; set; }
    public float Put_Call_Ratio { get; set; }  // If options data available
    public float Short_Interest_Ratio { get; set; }  // If available
    public float Analyst_Sentiment { get; set; }
    public float Insider_Trading_Signal { get; set; }  // If available
    public float Smart_Money_Flow { get; set; }
    public float Retail_Sentiment_Indicator { get; set; }

    // ========== RISK FACTORS (10) ==========
    public float Downside_Deviation { get; set; }
    public float Value_At_Risk_95 { get; set; }
    public float Conditional_VaR { get; set; }
    public float Tail_Risk_Indicator { get; set; }
    public float Liquidity_Risk_Score { get; set; }
    public float Concentration_Risk { get; set; }
    public float Idiosyncratic_Volatility { get; set; }
    public float Systematic_Risk_Beta { get; set; }
    public float Risk_Adjusted_Return { get; set; }
    public float Volatility_Of_Volatility { get; set; }

    /// <summary>
    /// Convert to dictionary for storage
    /// </summary>
    public Dictionary<string, float> ToDictionary()
    {
        var properties = GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(float));

        return properties.ToDictionary(
            p => p.Name,
            p => (float)(p.GetValue(this) ?? 0f));
    }

    /// <summary>
    /// Get total feature count
    /// </summary>
    public int GetFeatureCount() => ToDictionary().Count;
}