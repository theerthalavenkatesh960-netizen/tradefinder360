using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.Indicators;
using TradingSystem.Strategy.Models;

namespace TradingSystem.Strategy;

public enum StrategyMode
{
    Trade,
    Scan
}

public class StrategyEngine
{
    private readonly TradingLimitsConfig _limitsConfig;
    private readonly List<IndicatorValues> _indicatorHistory = new();

    public StrategyMode Mode { get; private set; } = StrategyMode.Trade;

    public StrategyEngine(TradingLimitsConfig limitsConfig, StrategyMode mode = StrategyMode.Trade)
    {
        _limitsConfig = limitsConfig;
        Mode = mode;
    }

    public void SetMode(StrategyMode mode) => Mode = mode;

    public void UpdateIndicatorHistory(IndicatorValues indicators)
    {
        _indicatorHistory.Add(indicators);

        if (_indicatorHistory.Count > 50)
            _indicatorHistory.RemoveAt(0);
    }

    public EntrySignal CheckForEntry(
        MarketStateInfo marketState,
        List<Candle> candles,
        IndicatorValues indicators)
    {
        var signal = new EntrySignal
        {
            IsValid = false,
            Timestamp = indicators.Timestamp,
            EntryPrice = candles[^1].Close
        };

        if (Mode == StrategyMode.Scan)
        {
            signal.ValidationDetails["Mode"] = "SCAN";
        }
        else if (!IsTradingHoursValid(indicators.Timestamp))
        {
            signal.Reason = "Outside trading hours";
            return signal;
        }

        if (marketState.State == Core.Models.MarketState.SIDEWAYS)
        {
            signal.Reason = "Market is sideways - no trades allowed";
            signal.ValidationDetails["MarketState"] = "SIDEWAYS";
            return signal;
        }

        if (marketState.State == Core.Models.MarketState.TRENDING_BULLISH)
        {
            return CheckBullishEntry(candles, indicators, signal);
        }

        if (marketState.State == Core.Models.MarketState.TRENDING_BEARISH)
        {
            return CheckBearishEntry(candles, indicators, signal);
        }

        signal.Reason = "No valid market state";
        return signal;
    }

    private EntrySignal CheckBullishEntry(
        List<Candle> candles,
        IndicatorValues indicators,
        EntrySignal signal)
    {
        var isPullback = PullbackDetector.IsBullishPullback(candles, _indicatorHistory);

        signal.ValidationDetails["IsPullback"] = isPullback.ToString();
        signal.ValidationDetails["RSI"] = indicators.RSI.ToString("F2");
        signal.ValidationDetails["ADX"] = indicators.ADX.ToString("F2");
        signal.ValidationDetails["MacdLine"] = indicators.MacdLine.ToString("F2");

        if (!isPullback)
        {
            signal.Reason = "No valid bullish pullback detected";
            return signal;
        }

        signal.IsValid = true;
        signal.Direction = TradeDirection.CALL;
        signal.Reason = "Bullish pullback entry: Strong trend with pullback to EMA/BB, strong entry candle confirmed";

        return signal;
    }

    private EntrySignal CheckBearishEntry(
        List<Candle> candles,
        IndicatorValues indicators,
        EntrySignal signal)
    {
        var isPullback = PullbackDetector.IsBearishPullback(candles, _indicatorHistory);

        signal.ValidationDetails["IsPullback"] = isPullback.ToString();
        signal.ValidationDetails["RSI"] = indicators.RSI.ToString("F2");
        signal.ValidationDetails["ADX"] = indicators.ADX.ToString("F2");
        signal.ValidationDetails["MacdLine"] = indicators.MacdLine.ToString("F2");

        if (!isPullback)
        {
            signal.Reason = "No valid bearish pullback detected";
            return signal;
        }

        signal.IsValid = true;
        signal.Direction = TradeDirection.PUT;
        signal.Reason = "Bearish pullback entry: Strong downtrend with pullback to EMA/BB, strong entry candle confirmed";

        return signal;
    }

    private bool IsTradingHoursValid(DateTime timestamp)
    {
        var time = TimeOnly.FromDateTime(timestamp);
        return time >= _limitsConfig.TradingStartTime &&
               time <= _limitsConfig.TradingEndTime;
    }

    public bool ShouldAllowNewTrade(DateTime timestamp)
    {
        var time = TimeOnly.FromDateTime(timestamp);
        return time <= _limitsConfig.NoNewTradesAfter;
    }
}
