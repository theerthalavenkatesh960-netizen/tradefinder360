namespace TradingSystem.Configuration.Models;

public class TradingConfig
{
    public InstrumentConfig Instrument { get; set; } = new();
    public TimeframeConfig Timeframe { get; set; } = new();
    public IndicatorConfig Indicators { get; set; } = new();
    public RiskConfig Risk { get; set; } = new();
    public TradingLimitsConfig Limits { get; set; } = new();
    public MarketStateConfig MarketState { get; set; } = new();
    public ExecutionConfig Execution { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    public UpstoxConfig Upstox { get; set; } = new();
}

public class InstrumentConfig
{
    public string ActiveInstrumentKey { get; set; } = "NSE:NIFTY";
    public string TradingMode { get; set; } = "OPTIONS";
    public Dictionary<string, InstrumentRiskOverride> InstrumentOverrides { get; set; } = new();
}

public class InstrumentRiskOverride
{
    public decimal? StopLossATRMultiplier { get; set; }
    public decimal? TargetATRMultiplier { get; set; }
    public int? MaxTradesPerDay { get; set; }
    public decimal? MaxDailyLossAmount { get; set; }
}

public class TimeframeConfig
{
    public int ActiveTimeframeMinutes { get; set; } = 15;
    public int BaseTimeframeMinutes { get; set; } = 15;
    public int MaxCandleHistory { get; set; } = 200;

    public decimal GetTimeframeMultiplier()
    {
        return (decimal)BaseTimeframeMinutes / ActiveTimeframeMinutes;
    }
}

public class IndicatorConfig
{
    public int BaseEmaFastLength { get; set; } = 20;
    public int BaseEmaSlowLength { get; set; } = 50;
    public int BaseRsiLength { get; set; } = 14;
    public int BaseMacdFast { get; set; } = 12;
    public int BaseMacdSlow { get; set; } = 26;
    public int BaseMacdSignal { get; set; } = 9;
    public int BaseAdxLength { get; set; } = 14;
    public int BaseAtrLength { get; set; } = 14;
    public int BaseBollingerLength { get; set; } = 20;
    public decimal BollingerStdDev { get; set; } = 2.0m;

    public int GetScaledLength(int baseLength, decimal multiplier)
    {
        return Math.Max(2, (int)Math.Round(baseLength * multiplier));
    }
}

public class RiskConfig
{
    public decimal StopLossATRMultiplier { get; set; } = 1.5m;
    public decimal TargetATRMultiplier { get; set; } = 2.0m;
    public decimal MaxDailyLossAmount { get; set; } = 10000m;
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public int CooldownMinutesAfterLoss { get; set; } = 30;
    public decimal MaxPositionSizePercent { get; set; } = 20m;
}

public class TradingLimitsConfig
{
    public int MaxTradesPerDay { get; set; } = 3;
    public int MaxConsecutiveLosses { get; set; } = 2;
    public TimeOnly TradingStartTime { get; set; } = new(9, 30);
    public TimeOnly TradingEndTime { get; set; } = new(15, 15);
    public TimeOnly NoNewTradesAfter { get; set; } = new(14, 30);
}

public class MarketStateConfig
{
    public decimal SidewaysAdxThreshold { get; set; } = 20m;
    public decimal TrendingAdxThreshold { get; set; } = 25m;
    public decimal BullishRsiThreshold { get; set; } = 55m;
    public decimal BearishRsiThreshold { get; set; } = 45m;
    public decimal SidewaysRsiLower { get; set; } = 40m;
    public decimal SidewaysRsiUpper { get; set; } = 60m;
    public decimal BollingerNarrowThreshold { get; set; } = 0.02m;
    public int MinCandlesForTrend { get; set; } = 3;
}

public class ExecutionConfig
{
    public decimal MaxSlippagePercent { get; set; } = 0.5m;
    public int OrderTimeoutSeconds { get; set; } = 30;
    public bool UseWeeklyOptions { get; set; } = true;
}

public class DatabaseConfig
{
    public string ConnectionString { get; set; } = string.Empty;
    public bool EnablePersistence { get; set; } = true;
}

public class UpstoxConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.upstox.com/v2";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 1000;
    public int RateLimitPerSecond { get; set; } = 10;
}
