using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Services.Strategies;

public abstract class BacktestStrategyBase : IBacktestStrategy
{
    protected static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    protected static readonly TimeOnly NoCutoff = new(14, 0);

    protected const double SlippageFraction = 0.0005;
    protected const double CommissionFraction = 0.0003;

    protected const int MaxTradesPerDay = 3;
    protected const int MaxConsecutiveLossesBeforeHalt = 2;
    protected const double MaxDailyLossPct = 3.0;
    protected const double DrawdownScaleThreshold = 5.0;
    protected const double DrawdownHaltThreshold = 10.0;
    protected const int CooldownBarsAfterLoss = 5;
    protected const double MinRRForEntry = 1.8;
    protected const int MinWarmupBars = 50;

    public abstract string StrategyName { get; }
    public abstract BacktestStrategyResult Execute(BacktestRunContext ctx);

    protected sealed class DayRiskState
    {
        public int TradesTaken;
        public int ConsecutiveLosses;
        public double DayStartCapital;
        public double DayPnl;
        public int CooldownUntilIdx = -1;
        public string? CooldownDirection;
        public bool Halted;

        public void RecordTrade(double pnl, string tradeType, int currentIdx)
        {
            TradesTaken++;
            DayPnl += pnl;

            if (pnl < 0)
            {
                ConsecutiveLosses++;
                CooldownDirection = tradeType;
                CooldownUntilIdx = currentIdx + CooldownBarsAfterLoss;
            }
            else
            {
                ConsecutiveLosses = 0;
                CooldownDirection = null;
            }

            if (ConsecutiveLosses >= MaxConsecutiveLossesBeforeHalt)
                Halted = true;
            if (DayStartCapital > 0 && -DayPnl / DayStartCapital * 100.0 >= MaxDailyLossPct)
                Halted = true;
        }

        public bool CanTrade(int currentIdx, string direction)
        {
            if (Halted) return false;
            if (TradesTaken >= MaxTradesPerDay) return false;
            if (CooldownDirection == direction && currentIdx <= CooldownUntilIdx) return false;
            return true;
        }
    }

    protected static DateTime ToIstDateTime(DateTimeOffset utcTimestamp) =>
        TimeZoneInfo.ConvertTime(utcTimestamp, Ist).DateTime;

    protected static double DrawdownAdjustedRisk(double riskPct, double currentCapital, double peakCapital)
    {
        if (peakCapital <= 0) return riskPct;
        var ddPct = (peakCapital - currentCapital) / peakCapital * 100.0;
        if (ddPct >= DrawdownHaltThreshold) return 0;
        if (ddPct >= DrawdownScaleThreshold) return riskPct * 0.5;
        return riskPct;
    }

    protected static bool PassesConfluence(IndicatorValues ind, bool isLong)
    {
        int score = 0;

        if (isLong && ind.EMAFast > ind.EMASlow) score++;
        if (!isLong && ind.EMAFast < ind.EMASlow) score++;

        if (isLong && ind.MacdHistogram > 0) score++;
        if (!isLong && ind.MacdHistogram < 0) score++;

        if (isLong && ind.RSI > 40 && ind.RSI < 75) score++;
        if (!isLong && ind.RSI < 60 && ind.RSI > 25) score++;

        if (ind.ADX > 20) score++;

        if (isLong && ind.PlusDI > ind.MinusDI) score++;
        if (!isLong && ind.MinusDI > ind.PlusDI) score++;

        return score >= 3;
    }

    protected static bool HasVolumeConfirmation(List<Candle> candles, int currentIdx)
    {
        if (currentIdx < 20) return true;
        double avg = 0;
        for (int k = currentIdx - 20; k < currentIdx; k++)
            avg += candles[k].Volume;
        avg /= 20.0;
        return avg > 0 && candles[currentIdx].Volume >= avg * 1.2;
    }

    protected static BacktestTradeResult? ManageOpenPosition(
        BacktestTradeResult trade,
        Candle candle,
        ref double trailStop,
        ref int remainingQty,
        ref bool movedToBreakeven)
    {
        if (movedToBreakeven || remainingQty <= 1) return null;

        bool isLong = trade.TradeType == "LONG";
        double riskDistance = Math.Abs(trade.EntryPrice - trade.StopLoss);
        if (riskDistance <= 0) return null;

        double favourableExcursion = isLong
            ? (double)candle.High - trade.EntryPrice
            : trade.EntryPrice - (double)candle.Low;

        if (favourableExcursion < riskDistance)
            return null;

        movedToBreakeven = true;
        trailStop = isLong
            ? trade.EntryPrice + riskDistance * 0.1
            : trade.EntryPrice - riskDistance * 0.1;

        int closeQty = remainingQty / 2;
        if (closeQty < 1) return null;

        remainingQty -= closeQty;
        double exitPrice = isLong
            ? trade.EntryPrice + riskDistance
            : trade.EntryPrice - riskDistance;

        double partialPnl = isLong
            ? (exitPrice - trade.EntryPrice) * closeQty
            : (trade.EntryPrice - exitPrice) * closeQty;

        var turnover = (trade.EntryPrice + exitPrice) * closeQty;
        partialPnl -= turnover * CommissionFraction;

        return trade with
        {
            Id = Guid.NewGuid().ToString(),
            ExitTime = ToIstDateTime(candle.Timestamp),
            ExitPrice = Math.Round(exitPrice, 2),
            Quantity = closeQty,
            Pnl = Math.Round(partialPnl, 2),
            PnlPercent = trade.EntryPrice != 0
                ? Math.Round(partialPnl / (trade.EntryPrice * closeQty) * 100.0, 2)
                : 0
        };
    }

    protected static double ApplySlippage(double price, bool isLong) =>
        isLong ? price * (1 + SlippageFraction) : price * (1 - SlippageFraction);

    protected static double CalcStopLossDistance(
        StrategyParams p,
        double entryPrice,
        double atr,
        double candleLow,
        double candleHigh,
        bool isLong)
    {
        var distance = p.StopLossType.ToUpperInvariant() switch
        {
            "ATR" => atr * 1.5,
            "FIXED_PERCENT" => entryPrice * ((p.SlPercent ?? 1.0) / 100.0),
            "CANDLE" => isLong
                ? Math.Max(entryPrice - candleLow, atr * 0.5)
                : Math.Max(candleHigh - entryPrice, atr * 0.5),
            _ => atr * 1.5
        };
        return Math.Max(distance, atr * 0.25);
    }

    protected static double CalcTarget(StrategyParams p, double entryPrice, double slDistance, double atr, bool isLong)
    {
        if (p.TargetType.Equals("RR_RATIO", StringComparison.OrdinalIgnoreCase))
        {
            var targetDistance = slDistance * (p.RrRatio ?? 2.0);
            return isLong ? entryPrice + targetDistance : entryPrice - targetDistance;
        }
        var farDistance = slDistance * 10;
        return isLong ? entryPrice + farDistance : entryPrice - farDistance;
    }

    protected static int CalcQuantity(double capital, double riskPercent, double slDistance)
    {
        if (slDistance <= 0) return 0;
        var riskAmount = capital * (riskPercent / 100.0);
        return Math.Max((int)Math.Floor(riskAmount / slDistance), 1);
    }

    protected static BacktestTradeResult? CheckExit(
        BacktestTradeResult trade,
        Candle candle,
        double atr,
        StrategyParams p,
        ref double trailStop)
    {
        bool isLong = trade.TradeType == "LONG";
        double high = (double)candle.High;
        double low = (double)candle.Low;
        double open = (double)candle.Open;

        if (p.TargetType.Equals("TRAILING", StringComparison.OrdinalIgnoreCase))
        {
            if (isLong)
            {
                if (low <= trailStop)
                    return CloseTrade(trade, ToIstDateTime(candle.Timestamp), trailStop);
                trailStop = Math.Max(trailStop, high - atr);
            }
            else
            {
                if (high >= trailStop)
                    return CloseTrade(trade, ToIstDateTime(candle.Timestamp), trailStop);
                trailStop = Math.Min(trailStop, low + atr);
            }
            return null;
        }

        bool slHit = isLong ? low <= trade.StopLoss : high >= trade.StopLoss;
        bool tgtHit = isLong ? high >= trade.Target : low <= trade.Target;

        if (slHit && tgtHit)
        {
            var distToSl = Math.Abs(open - trade.StopLoss);
            var distToTgt = Math.Abs(open - trade.Target);
            bool stopFirst = isLong
                ? (open <= trade.StopLoss || distToSl < distToTgt)
                : (open >= trade.StopLoss || distToSl < distToTgt);
            return stopFirst
                ? CloseTrade(trade, ToIstDateTime(candle.Timestamp), trade.StopLoss)
                : CloseTrade(trade, ToIstDateTime(candle.Timestamp), trade.Target);
        }

        if (slHit) return CloseTrade(trade, ToIstDateTime(candle.Timestamp), trade.StopLoss);
        if (tgtHit) return CloseTrade(trade, ToIstDateTime(candle.Timestamp), trade.Target);

        return null;
    }

    protected static BacktestTradeResult CloseTrade(BacktestTradeResult open, DateTime exitTime, double exitPrice)
    {
        bool isLong = open.TradeType == "LONG";
        double pnl = isLong
            ? (exitPrice - open.EntryPrice) * open.Quantity
            : (open.EntryPrice - exitPrice) * open.Quantity;
        double pnlPct = open.EntryPrice != 0
            ? ((exitPrice - open.EntryPrice) / open.EntryPrice) * 100.0 * (isLong ? 1 : -1)
            : 0;
        return open with
        {
            ExitTime = exitTime,
            ExitPrice = exitPrice,
            Pnl = Math.Round(pnl, 2),
            PnlPercent = Math.Round(pnlPct, 2)
        };
    }

    protected static BacktestTradeResult ApplyCostsWithQty(BacktestTradeResult closed, int qty)
    {
        var adjusted = closed with { Quantity = qty };
        bool isLong = adjusted.TradeType == "LONG";
        double rawPnl = isLong
            ? (adjusted.ExitPrice - adjusted.EntryPrice) * qty
            : (adjusted.EntryPrice - adjusted.ExitPrice) * qty;
        var turnover = (adjusted.EntryPrice + adjusted.ExitPrice) * qty;
        var commission = turnover * CommissionFraction;
        var finalPnl = rawPnl - commission;
        var pct = adjusted.EntryPrice != 0 && qty > 0
            ? (finalPnl / (adjusted.EntryPrice * qty)) * 100.0
            : 0;
        return adjusted with { Pnl = Math.Round(finalPnl, 2), PnlPercent = Math.Round(pct, 2) };
    }

    protected static BacktestTradeResult CloseRemainingWithCosts(
        BacktestTradeResult open,
        DateTime exitTime,
        double exitPrice,
        int qty)
    {
        var closed = CloseTrade(open, exitTime, exitPrice);
        return ApplyCostsWithQty(closed, qty);
    }

    protected static double FindPreviousBullRunHigh(List<Candle> candles, IndicatorValues[] indicators, int crossoverIdx)
    {
        double high = 0;
        for (int k = crossoverIdx - 1; k >= 0; k--)
        {
            if (indicators[k].EMAFast >= indicators[k].EMASlow) break;
            var h = (double)candles[k].High;
            if (h > high) high = h;
        }
        return high;
    }

    protected static double FindPreviousBearRunLow(List<Candle> candles, IndicatorValues[] indicators, int crossoverIdx)
    {
        double low = double.MaxValue;
        for (int k = crossoverIdx - 1; k >= 0; k--)
        {
            if (indicators[k].EMAFast <= indicators[k].EMASlow) break;
            var l = (double)candles[k].Low;
            if (l < low) low = l;
        }
        return low == double.MaxValue ? 0 : low;
    }

    protected static double FindRecentSwingLow(List<Candle> candles, int fromIdx, int lookback = 10)
    {
        int start = Math.Max(0, fromIdx - lookback);
        double low = double.MaxValue;
        for (int k = start; k < fromIdx; k++)
        {
            var l = (double)candles[k].Low;
            if (l < low) low = l;
        }
        return low == double.MaxValue ? (double)candles[fromIdx].Low : low;
    }

    protected static double FindRecentSwingHigh(List<Candle> candles, int fromIdx, int lookback = 10)
    {
        int start = Math.Max(0, fromIdx - lookback);
        double high = 0;
        for (int k = start; k < fromIdx; k++)
        {
            var h = (double)candles[k].High;
            if (h > high) high = h;
        }
        return high == 0 ? (double)candles[fromIdx].High : high;
    }
}
