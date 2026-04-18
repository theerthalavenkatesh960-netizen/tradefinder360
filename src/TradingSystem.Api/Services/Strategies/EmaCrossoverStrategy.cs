using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Services.Strategies;

public sealed class EmaCrossoverStrategy : BacktestStrategyBase
{
    private readonly BacktestRunnerService _runner;

    public EmaCrossoverStrategy(BacktestRunnerService runner)
    {
        _runner = runner;
    }

    public override string StrategyName => "EMA";

    public override BacktestStrategyResult Execute(BacktestRunContext ctx)
    {
        var mode = (ctx.Params.EmaMode ?? "CROSSOVER").Trim().ToUpperInvariant();
        if (mode == "PULLBACK")
        {
            var pullbackTrades = _runner.RunEmaPullbackInternal(ctx.Candles, ctx.Indicators, ctx.IstTimes, ctx.Params, ctx.InitialCapital);
            return new BacktestStrategyResult(pullbackTrades);
        }
        if (mode == "SPEED")
        {
            var speedTrades = _runner.RunEmaSpeedInternal(ctx.Candles, ctx.Indicators, ctx.IstTimes, ctx.Params, ctx.InitialCapital);
            return new BacktestStrategyResult(speedTrades);
        }
        if (mode == "PULLBACK_SPEED")
        {
            var combinedTrades = _runner.RunEmaPullbackSpeedInternal(ctx.Candles, ctx.Indicators, ctx.IstTimes, ctx.Params, ctx.InitialCapital);
            return new BacktestStrategyResult(combinedTrades);
        }

        var candles = ctx.Candles;
        var indicators = ctx.Indicators;
        var istTimes = ctx.IstTimes;
        var p = ctx.Params;
        var initialCapital = ctx.InitialCapital;

        var trades = new List<BacktestTradeResult>();
        double runningCapital = initialCapital;
        double peakCapital = initialCapital;

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;
        int remainingQty = 0;
        bool movedToBE = false;
        int holdingBars = 0;
        var risk = new DayRiskState { DayStartCapital = initialCapital };
        DateTime lastDayDate = DateTime.MinValue;

        int rsiPeriod = p.EmaRsiPeriod ?? 14;
        int rsiMidline = p.EmaRsiMidline ?? 50;
        double rsiOverbought = p.RsiOverbought ?? 70;
        double rsiOversold = p.RsiOversold ?? 30;
        int volumeAvgPeriod = p.VolumeAvgPeriod ?? 20;
        double volumeMultiplier = p.VolumeMultiplier ?? 1.5;
        int srLookback = p.SRLookbackPeriod ?? 20;
        double srBufferPct = p.SRBuffer ?? 0.5;
        int candleLookback = Math.Max(1, p.CandleLookback ?? 1);
        bool useTriple = p.UseTripleEma == true;
        int maxHold = Math.Max(0, p.MaxHoldingPeriods ?? 0);

        string tradeDirection = (p.TradeDirection ?? "BOTH").Trim().ToUpperInvariant();
        bool allowLong = tradeDirection is "BOTH" or "LONG_ONLY";
        bool allowShort = tradeDirection is "BOTH" or "SHORT_ONLY";

        string filterType = (p.EmaFilterType ?? "RSI").Trim().ToUpperInvariant();

        var customRsi = CalculateRsi(candles, rsiPeriod);
        var middleEma = CalculateEma(candles, p.MiddleEma ?? 21);

        for (int i = Math.Max(1, MinWarmupBars); i < candles.Count; i++)
        {
            var prevInd = indicators[i - 1];
            var curInd = indicators[i];
            var candle = candles[i];
            var atr = (double)curInd.ATR;
            var istTime = istTimes[i];

            if (istTime.Date != lastDayDate)
            {
                if (openTrade != null)
                {
                    var prevCandle = candles[i - 1];
                    var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(prevCandle.Timestamp), (double)prevCandle.Close, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    openTrade = null;
                    holdingBars = 0;
                }
                risk = new DayRiskState { DayStartCapital = runningCapital };
                lastDayDate = istTime.Date;
            }

            bool fastAboveSlow = curInd.EMAFast > curInd.EMASlow;
            bool prevFastAboveSlow = prevInd.EMAFast > prevInd.EMASlow;
            bool bullishCross = !prevFastAboveSlow && fastAboveSlow;
            bool bearishCross = prevFastAboveSlow && !fastAboveSlow;

            if (openTrade != null)
            {
                holdingBars++;

                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

                bool oppositeSignal = (openTrade.TradeType == "LONG" && bearishCross)
                                   || (openTrade.TradeType == "SHORT" && bullishCross);

                bool maxHoldingExceeded = maxHold > 0 && holdingBars >= maxHold;

                if (oppositeSignal || maxHoldingExceeded)
                {
                    var exitPrice = maxHoldingExceeded ? (double)candle.Close : (double)candle.Close;
                    var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(candle.Timestamp), exitPrice, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                    openTrade = null;
                    holdingBars = 0;
                }
                else
                {
                    var exitResult = CheckExit(openTrade, candle, atr, p, ref trailStop);
                    if (exitResult != null)
                    {
                        var closed = ApplyCostsWithQty(exitResult, remainingQty);
                        runningCapital += closed.Pnl;
                        if (runningCapital > peakCapital) peakCapital = runningCapital;
                        trades.Add(closed);
                        risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                        openTrade = null;
                        holdingBars = 0;
                    }
                    continue;
                }
            }

            if (openTrade != null) continue;
            if (atr <= 0) continue;

            bool hasSignal = bullishCross || bearishCross;
            if (!hasSignal) continue;

            bool isLong = bullishCross;
            if (isLong && !allowLong) continue;
            if (!isLong && !allowShort) continue;

            if (useTriple && i < middleEma.Length)
            {
                var m = middleEma[i];
                if (isLong && !((double)curInd.EMAFast > m && m > (double)curInd.EMASlow)) continue;
                if (!isLong && !((double)curInd.EMAFast < m && m < (double)curInd.EMASlow)) continue;
            }

            if (!PassesSelectedFilter(filterType, candles, indicators, customRsi, i, isLong,
                                      rsiMidline, rsiOverbought, rsiOversold,
                                      volumeAvgPeriod, volumeMultiplier,
                                      srLookback, srBufferPct,
                                      candleLookback, p.AllowedPatterns))
            {
                continue;
            }

            string direction = isLong ? "LONG" : "SHORT";
            if (!risk.CanTrade(i, direction)) continue;
            if (i + 1 >= candles.Count) continue;

            var nextCandle = candles[i + 1];
            var rawEntry = (double)nextCandle.Open;
            var entryPrice = ApplySlippage(rawEntry, isLong);

            var slDistance = CalcEnhancedStopLossDistance(p, entryPrice, atr, (double)curInd.EMASlow, isLong);
            if (slDistance <= 0) continue;

            var sl = isLong ? entryPrice - slDistance : entryPrice + slDistance;
            var rrr = p.TargetRRR ?? p.RrRatio ?? 2.0;
            var target = isLong ? entryPrice + (slDistance * rrr) : entryPrice - (slDistance * rrr);

            var rrRatio = slDistance > 0 ? Math.Abs(target - entryPrice) / slDistance : 0;
            if (rrRatio < MinRRForEntry) { i++; continue; }

            var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
            if (effectiveRisk <= 0) { i++; continue; }
            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
            if (qty <= 0) { i++; continue; }

            var notional = entryPrice * qty;
            if (notional > runningCapital * 0.20)
                qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
            if (qty <= 0) { i++; continue; }

            openTrade = new BacktestTradeResult(
                Guid.NewGuid().ToString(),
                ToIstDateTime(nextCandle.Timestamp), entryPrice,
                default, 0, sl, target, qty, 0, 0, direction);
            trailStop = sl;
            remainingQty = qty;
            movedToBE = false;
            holdingBars = 0;

            var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
            if (entryBarExit != null)
            {
                var closed = ApplyCostsWithQty(entryBarExit, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
                risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                openTrade = null;
                holdingBars = 0;
            }
            i++;
        }

        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(last.Timestamp), (double)last.Close, remainingQty);
            runningCapital += closed.Pnl;
            trades.Add(closed);
        }

        return new BacktestStrategyResult(trades);
    }

    private static double CalcEnhancedStopLossDistance(StrategyParams p, double entryPrice, double atr, double slowEma, bool isLong)
    {
        string slType = (p.EmaSlType ?? p.StopLossType ?? "ATR").Trim().ToUpperInvariant();
        double slValue = p.EmaSlValue ?? p.SlPercent ?? 1.0;

        return slType switch
        {
            "FIXED_PERCENT" => Math.Max(entryPrice * (slValue / 100.0), atr * 0.25),
            "BELOW_EMA" => Math.Max(Math.Abs(entryPrice - slowEma), atr * 0.25),
            "ATR_BASED" => Math.Max(atr * slValue, atr * 0.25),
            "ATR" => Math.Max(atr * 1.5, atr * 0.25),
            _ => Math.Max(atr * 1.5, atr * 0.25)
        };
    }

    private static bool PassesSelectedFilter(
        string filterType,
        List<Candle> candles,
        IndicatorValues[] indicators,
        double[] rsi,
        int i,
        bool isLong,
        int rsiMidline,
        double rsiOverbought,
        double rsiOversold,
        int volumeAvgPeriod,
        double volumeMultiplier,
        int srLookback,
        double srBufferPct,
        int candleLookback,
        string[]? allowedPatterns)
    {
        switch (filterType)
        {
            case "RSI":
                if (i >= rsi.Length) return false;
                var r = rsi[i];
                return isLong
                    ? r > rsiMidline && r < rsiOverbought
                    : r < rsiMidline && r > rsiOversold;

            case "VOLUME":
                if (i < volumeAvgPeriod) return false;
                double avgVol = 0;
                for (int k = i - volumeAvgPeriod; k < i; k++) avgVol += candles[k].Volume;
                avgVol /= volumeAvgPeriod;
                return avgVol > 0 && candles[i].Volume > avgVol * volumeMultiplier;

            case "SUPPORT_RESISTANCE":
                if (i < srLookback) return false;
                double close = (double)candles[i].Close;
                double low = double.MaxValue;
                double high = double.MinValue;
                for (int k = i - srLookback; k <= i; k++)
                {
                    low = Math.Min(low, (double)candles[k].Low);
                    high = Math.Max(high, (double)candles[k].High);
                }
                if (low <= 0 || high <= 0) return false;
                if (isLong)
                {
                    var supportDistancePct = Math.Abs((close - low) / low) * 100.0;
                    return supportDistancePct <= srBufferPct;
                }
                else
                {
                    var resistanceDistancePct = Math.Abs((high - close) / high) * 100.0;
                    return resistanceDistancePct <= srBufferPct;
                }

            case "PRICE_ACTION":
                return HasPriceActionPattern(candles, i, isLong, candleLookback, allowedPatterns);

            default:
                return PassesConfluence(indicators[i], isLong);
        }
    }

    private static bool HasPriceActionPattern(List<Candle> candles, int i, bool isLong, int lookback, string[]? allowedPatterns)
    {
        var patterns = (allowedPatterns == null || allowedPatterns.Length == 0)
            ? new[] { "ENGULFING", "HAMMER", "DOJI", "MORNINGSTAR" }
            : allowedPatterns.Select(p => p.Trim().ToUpperInvariant()).ToArray();

        int start = Math.Max(1, i - lookback);
        for (int idx = start; idx <= i; idx++)
        {
            foreach (var p in patterns)
            {
                if (p == "ENGULFING" && IsEngulfing(candles, idx, isLong)) return true;
                if (p == "HAMMER" && isLong && IsHammer(candles[idx])) return true;
                if (p == "DOJI" && IsDoji(candles[idx])) return true;
                if (p == "MORNINGSTAR" && isLong && IsMorningStar(candles, idx)) return true;
                if (p == "ENGULFING" && !isLong && IsEngulfing(candles, idx, false)) return true;
            }
        }
        return false;
    }

    private static bool IsEngulfing(List<Candle> candles, int i, bool bullish)
    {
        if (i < 1) return false;
        var prev = candles[i - 1];
        var cur = candles[i];

        if (bullish)
        {
            return prev.IsBearish && cur.IsBullish
                && cur.Open <= prev.Close
                && cur.Close >= prev.Open;
        }

        return prev.IsBullish && cur.IsBearish
            && cur.Open >= prev.Close
            && cur.Close <= prev.Open;
    }

    private static bool IsHammer(Candle c)
    {
        var body = Math.Abs((double)c.Close - (double)c.Open);
        if (body <= 0) return false;
        var lowerWick = (double)Math.Min(c.Open, c.Close) - (double)c.Low;
        var upperWick = (double)c.High - (double)Math.Max(c.Open, c.Close);
        return lowerWick >= body * 2 && upperWick <= body;
    }

    private static bool IsDoji(Candle c)
    {
        var range = (double)(c.High - c.Low);
        if (range <= 0) return false;
        var body = Math.Abs((double)c.Close - (double)c.Open);
        return body / range <= 0.1;
    }

    private static bool IsMorningStar(List<Candle> candles, int i)
    {
        if (i < 2) return false;
        var c1 = candles[i - 2];
        var c2 = candles[i - 1];
        var c3 = candles[i];

        var c1Body = Math.Abs((double)c1.Close - (double)c1.Open);
        var c2Body = Math.Abs((double)c2.Close - (double)c2.Open);

        return c1.IsBearish
            && c3.IsBullish
            && c2Body < c1Body * 0.5
            && (double)c3.Close > ((double)c1.Open + (double)c1.Close) / 2.0;
    }

    private static double[] CalculateRsi(List<Candle> candles, int period)
    {
        var values = candles.Select(c => (double)c.Close).ToArray();
        var result = new double[values.Length];
        if (values.Length == 0 || period <= 0) return result;

        double gain = 0;
        double loss = 0;

        for (int i = 1; i <= period && i < values.Length; i++)
        {
            var diff = values[i] - values[i - 1];
            if (diff >= 0) gain += diff;
            else loss -= diff;
        }

        if (values.Length <= period) return result;

        double avgGain = gain / period;
        double avgLoss = loss / period;
        result[period] = avgLoss == 0 ? 100 : 100 - (100 / (1 + (avgGain / avgLoss)));

        for (int i = period + 1; i < values.Length; i++)
        {
            var diff = values[i] - values[i - 1];
            var g = diff > 0 ? diff : 0;
            var l = diff < 0 ? -diff : 0;

            avgGain = ((avgGain * (period - 1)) + g) / period;
            avgLoss = ((avgLoss * (period - 1)) + l) / period;

            result[i] = avgLoss == 0 ? 100 : 100 - (100 / (1 + (avgGain / avgLoss)));
        }

        return result;
    }

    private static double[] CalculateEma(List<Candle> candles, int period)
    {
        var closes = candles.Select(c => (double)c.Close).ToArray();
        var ema = new double[closes.Length];
        if (closes.Length == 0 || period <= 1)
            return closes;

        double multiplier = 2.0 / (period + 1);
        ema[0] = closes[0];

        for (int i = 1; i < closes.Length; i++)
            ema[i] = ((closes[i] - ema[i - 1]) * multiplier) + ema[i - 1];

        return ema;
    }
}
