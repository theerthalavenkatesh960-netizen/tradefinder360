using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.Indicators;
using TradingSystem.Risk.Models;

namespace TradingSystem.Risk;

public class RiskEngine
{
    private readonly RiskConfig _config;
    private decimal _dailyLoss = 0;
    private int _dailyTradeCount = 0;
    private int _consecutiveLosses = 0;
    private DateTime? _lastTradeDate;
    private DateTime? _lastLossTime;

    public RiskEngine(RiskConfig config)
    {
        _config = config;
    }

    public RiskParameters CalculateRiskParameters(
        decimal entryPrice,
        decimal atr,
        TradeDirection direction,
        int lotSize)
    {
        var stopLossDistance = atr * _config.StopLossATRMultiplier;
        var targetDistance = atr * _config.TargetATRMultiplier;

        decimal stopLossPrice;
        decimal targetPrice;

        if (direction == TradeDirection.CALL)
        {
            stopLossPrice = entryPrice - stopLossDistance;
            targetPrice = entryPrice + targetDistance;
        }
        else
        {
            stopLossPrice = entryPrice + stopLossDistance;
            targetPrice = entryPrice - targetDistance;
        }

        return new RiskParameters
        {
            StopLossPrice = stopLossPrice,
            TargetPrice = targetPrice,
            StopLossDistance = stopLossDistance,
            TargetDistance = targetDistance,
            RiskRewardRatio = targetDistance / stopLossDistance,
            PositionSize = lotSize,
            MaxLossAmount = stopLossDistance * lotSize
        };
    }

    public ExitSignal CheckForExit(
        Trade trade,
        decimal currentSpotPrice,
        IndicatorValues indicators,
        DateTime timestamp)
    {
        var signal = new ExitSignal
        {
            ShouldExit = false,
            Timestamp = timestamp,
            CurrentPrice = currentSpotPrice
        };

        if (trade.Direction == TradeDirection.CALL)
        {
            if (currentSpotPrice <= trade.StopLoss)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Stop Loss Hit: Price {currentSpotPrice} <= SL {trade.StopLoss}";
                signal.Details["Type"] = "StopLoss";
                return signal;
            }

            if (currentSpotPrice >= trade.Target)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Target Hit: Price {currentSpotPrice} >= Target {trade.Target}";
                signal.Details["Type"] = "Target";
                return signal;
            }

            if (indicators.RSI < 50)
            {
                signal.ShouldExit = true;
                signal.Reason = $"RSI crossed below 50: {indicators.RSI:F2}";
                signal.Details["Type"] = "RSIExit";
                return signal;
            }

            if (currentSpotPrice < indicators.EMASlow)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Price closed below EMA Slow: {currentSpotPrice} < {indicators.EMASlow}";
                signal.Details["Type"] = "EMAExit";
                return signal;
            }

            if (indicators.MacdLine < 0)
            {
                signal.ShouldExit = true;
                signal.Reason = "MACD crossed below zero line";
                signal.Details["Type"] = "MACDExit";
                return signal;
            }
        }
        else
        {
            if (currentSpotPrice >= trade.StopLoss)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Stop Loss Hit: Price {currentSpotPrice} >= SL {trade.StopLoss}";
                signal.Details["Type"] = "StopLoss";
                return signal;
            }

            if (currentSpotPrice <= trade.Target)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Target Hit: Price {currentSpotPrice} <= Target {trade.Target}";
                signal.Details["Type"] = "Target";
                return signal;
            }

            if (indicators.RSI > 50)
            {
                signal.ShouldExit = true;
                signal.Reason = $"RSI crossed above 50: {indicators.RSI:F2}";
                signal.Details["Type"] = "RSIExit";
                return signal;
            }

            if (currentSpotPrice > indicators.EMASlow)
            {
                signal.ShouldExit = true;
                signal.Reason = $"Price closed above EMA Slow: {currentSpotPrice} > {indicators.EMASlow}";
                signal.Details["Type"] = "EMAExit";
                return signal;
            }

            if (indicators.MacdLine > 0)
            {
                signal.ShouldExit = true;
                signal.Reason = "MACD crossed above zero line";
                signal.Details["Type"] = "MACDExit";
                return signal;
            }
        }

        return signal;
    }

    public bool CanTakeTrade(DateTime timestamp, int maxTradesPerDay)
    {
        var currentDate = timestamp.Date;

        if (_lastTradeDate != currentDate)
        {
            ResetDailyCounters(currentDate);
        }

        if (_dailyTradeCount >= maxTradesPerDay)
            return false;

        if (_dailyLoss >= _config.MaxDailyLossAmount)
            return false;

        if (_consecutiveLosses >= 2 && _lastLossTime.HasValue)
        {
            var cooldownPeriod = TimeSpan.FromMinutes(_config.CooldownMinutesAfterLoss);
            if (timestamp - _lastLossTime.Value < cooldownPeriod)
                return false;
        }

        return true;
    }

    public void RecordTrade(decimal pnl, DateTime timestamp)
    {
        _dailyTradeCount++;
        _dailyLoss += Math.Abs(pnl);

        if (pnl < 0)
        {
            _consecutiveLosses++;
            _lastLossTime = timestamp;
        }
        else
        {
            _consecutiveLosses = 0;
        }
    }

    private void ResetDailyCounters(DateTime currentDate)
    {
        _dailyLoss = 0;
        _dailyTradeCount = 0;
        _lastTradeDate = currentDate;
    }

    public int GetDailyTradeCount() => _dailyTradeCount;
    public decimal GetDailyLoss() => _dailyLoss;
    public int GetConsecutiveLosses() => _consecutiveLosses;
}
