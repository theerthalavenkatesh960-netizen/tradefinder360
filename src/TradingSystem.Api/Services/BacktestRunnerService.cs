using TradingSystem.Api.DTOs;
using TradingSystem.Core.Models;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;

namespace TradingSystem.Api.Services;

public class BacktestRunnerService
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    private static readonly TimeOnly MarketOpen = new(9, 15);
    private static readonly TimeOnly MarketClose = new(15, 30);
    private static readonly TimeOnly NoCutoff = new(14, 0);

    // ── Realistic cost model for NSE intraday ──
    private const double SlippageFraction = 0.0005;
    private const double CommissionFraction = 0.0003;

    // ── Professional risk management constants ──
    private const int MaxTradesPerDay = 3;
    private const int MaxConsecutiveLossesBeforeHalt = 2;
    private const double MaxDailyLossPct = 3.0;
    private const double DrawdownScaleThreshold = 5.0;
    private const double DrawdownHaltThreshold = 10.0;
    private const int CooldownBarsAfterLoss = 5;
    private const double MinRRForEntry = 1.8;
    private const int MinWarmupBars = 50;

    private readonly ICandleService _candleService;
    private readonly IInstrumentService _instrumentService;
    private readonly ILogger<BacktestRunnerService> _logger;

    public BacktestRunnerService(
        ICandleService candleService,
        IInstrumentService instrumentService,
        ILogger<BacktestRunnerService> logger)
    {
        _candleService = candleService;
        _instrumentService = instrumentService;
        _logger = logger;
    }

    /// <summary>
    /// Converts a candle's UTC DateTimeOffset timestamp to an IST DateTime.
    /// All trade entry/exit times in the backtest response should be IST
    /// since the trading system operates on NSE (IST 09:15–15:30).
    ///
    /// EF/Npgsql normalizes TIMESTAMPTZ to UTC on read. Without this conversion,
    /// a 09:15 IST candle would show as 03:45 in trade results.
    /// </summary>
    private static DateTime ToIstDateTime(DateTimeOffset utcTimestamp) =>
        TimeZoneInfo.ConvertTime(utcTimestamp, Ist).DateTime;

    public async Task<BacktestResponse> RunAsync(BacktestRunRequest request, double initialCapital = 100000)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(request.Symbol);
        if (instrument == null)
            throw new KeyNotFoundException("Symbol not found");

        var candles = await _candleService.GetCandlesAsync(
            instrument.Id,
            request.Strategy.Params.Timeframe,
            request.From,
            request.To);

        if (candles.Count == 0)
            return new BacktestResponse([], EmptyMetrics(initialCapital, request.From));

        var orderedCandles = candles.OrderBy(c => c.Timestamp).ToList();
        var indicators = ComputeIndicators(orderedCandles, request.Strategy.Params);

        // Candle timestamps are UTC DateTimeOffset after EF fetch.
        // Convert to IST DateTimeOffset for time-of-day checks (market hours, NoCutoff)
        // and for day-grouping (ORB groups by IST trading date).
        var istTimes = orderedCandles
            .Select(c => TimeZoneInfo.ConvertTime(c.Timestamp, Ist))
            .ToArray();

        var trades = request.Strategy.Name.ToUpperInvariant() switch
        {
            "ORB" => RunORB(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
            "RSI_REVERSAL" => RunRsiReversal(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
            "EMA_CROSSOVER" => RunEmaCrossover(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
            _ => throw new ArgumentException($"Unknown strategy: {request.Strategy.Name}")
        };

        var tradeList = trades.ToList();
        var metrics = CalculateMetrics(tradeList, initialCapital, orderedCandles);

        return new BacktestResponse(tradeList, metrics);
    }

    // ─────────────────────────────────────────────────────────
    // PER-DAY RISK STATE
    // ─────────────────────────────────────────────────────────
    private sealed class DayRiskState
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

    private static double DrawdownAdjustedRisk(double riskPct, double currentCapital, double peakCapital)
    {
        if (peakCapital <= 0) return riskPct;
        var ddPct = (peakCapital - currentCapital) / peakCapital * 100.0;
        if (ddPct >= DrawdownHaltThreshold) return 0;
        if (ddPct >= DrawdownScaleThreshold) return riskPct * 0.5;
        return riskPct;
    }

    // ─────────────────────────────────────────────────────────
    // CONFLUENCE FILTER
    // ─────────────────────────────────────────────────────────
    private static bool PassesConfluence(IndicatorValues ind, bool isLong)
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

    private static bool HasVolumeConfirmation(List<Candle> candles, int currentIdx)
    {
        if (currentIdx < 20) return true;
        double avg = 0;
        for (int k = currentIdx - 20; k < currentIdx; k++)
            avg += candles[k].Volume;
        avg /= 20.0;
        return avg > 0 && candles[currentIdx].Volume >= avg * 1.2;
    }

    // ─────────────────────────────────────────────────────────
    // PARTIAL PROFIT + BREAKEVEN STOP
    // ─────────────────────────────────────────────────────────
    private static BacktestTradeResult? ManageOpenPosition(
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

        if (favourableExcursion >= riskDistance)
        {
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

        return null;
    }

    // ─────────────────────────────────────────────────────────
    // INDICATORS
    // ─────────────────────────────────────────────────────────
    private static IndicatorValues[] ComputeIndicators(List<Candle> candles, StrategyParams p)
    {
        var engine = new IndicatorEngine(
            emaFastPeriod: p.FastEMA ?? 9,
            emaSlowPeriod: p.SlowEMA ?? 21,
            rsiPeriod: 14, macdFast: 12, macdSlow: 26, macdSignal: 9,
            adxPeriod: 14, atrPeriod: 14,
            bollingerPeriod: 20, bollingerStdDev: 2.0m);

        return candles.Select(c => engine.Calculate(c)).ToArray();
    }

    // ─────────────────────────────────────────────────────────
    // STRATEGY A: Opening Range Breakout
    // ─────────────────────────────────────────────────────────
    private List<BacktestTradeResult> RunORB(
        List<Candle> candles, IndicatorValues[] indicators, DateTimeOffset[] istTimes,
        StrategyParams p, double initialCapital)
    {
        var trades = new List<BacktestTradeResult>();
        int orbCandleCount = p.Timeframe switch { 1 => 5, _ => 1 };

        var dayGroups = candles
            .Select((c, i) => (Candle: c, Index: i, Indicator: indicators[i], IstTime: istTimes[i]))
            .GroupBy(x => x.IstTime.Date)
            .OrderBy(g => g.Key);

        double runningCapital = initialCapital;
        double peakCapital = initialCapital;

        foreach (var dayGroup in dayGroups)
        {
            var day = dayGroup.OrderBy(x => x.Candle.Timestamp).ToList();
            if (day.Count <= orbCandleCount) continue;

            var orbCandles = day.Take(orbCandleCount).ToList();
            var openingHigh = (double)orbCandles.Max(x => x.Candle.High);
            var openingLow = (double)orbCandles.Min(x => x.Candle.Low);
            var openingRange = openingHigh - openingLow;

            var firstAtr = (double)orbCandles[0].Indicator.ATR;
            if (firstAtr > 0 && (openingRange < firstAtr * 0.3 || openingRange > firstAtr * 3.0))
                continue;

            var risk = new DayRiskState { DayStartCapital = runningCapital };
            BacktestTradeResult? openTrade = null;
            double trailStop = 0;
            int remainingQty = 0;
            bool movedToBE = false;

            for (int j = orbCandleCount; j < day.Count; j++)
            {
                var (candle, globalIdx, ind, istTime) = day[j];

                if (j == day.Count - 1 && openTrade != null)
                {
                    var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(candle.Timestamp), (double)candle.Close, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, j);
                    openTrade = null;
                    break;
                }

                if (openTrade != null)
                {
                    var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                    if (partial != null)
                    {
                        runningCapital += partial.Pnl;
                        if (runningCapital > peakCapital) peakCapital = runningCapital;
                        trades.Add(partial);
                    }

                    var exitResult = CheckExit(openTrade, candle, (double)ind.ATR, p, ref trailStop);
                    if (exitResult != null)
                    {
                        var closed = ApplyCostsWithQty(exitResult, remainingQty);
                        runningCapital += closed.Pnl;
                        if (runningCapital > peakCapital) peakCapital = runningCapital;
                        trades.Add(closed);
                        risk.RecordTrade(closed.Pnl, closed.TradeType, j);
                        openTrade = null;
                    }
                    continue;
                }

                // Time-of-day check uses pre-computed IST time
                if (TimeOnly.FromDateTime(istTime.DateTime) >= NoCutoff) continue;
                if (!risk.CanTrade(j, "")) continue;

                var atr = (double)ind.ATR;
                if (atr <= 0) continue;

                bool longSignal = (double)candle.Close > openingHigh;
                bool shortSignal = (double)candle.Close < openingLow;
                if (!longSignal && !shortSignal) continue;

                bool isLong = longSignal;
                if (!PassesConfluence(ind, isLong)) continue;
                if (!HasVolumeConfirmation(candles, globalIdx)) continue;

                if (j + 1 >= day.Count) continue;

                var nextCandle = day[j + 1].Candle;
                var rawEntry = (double)nextCandle.Open;
                var entryPrice = ApplySlippage(rawEntry, isLong);
                var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, isLong);
                var sl = isLong ? entryPrice - slDistance : entryPrice + slDistance;
                var target = CalcTarget(p, entryPrice, slDistance, atr, isLong);

                var rrRatio = slDistance > 0 ? Math.Abs(target - entryPrice) / slDistance : 0;
                if (rrRatio < MinRRForEntry) { j++; continue; }

                var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                if (effectiveRisk <= 0) { j++; continue; }
                var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                if (qty <= 0) { j++; continue; }

                var notional = entryPrice * qty;
                if (notional > runningCapital * 0.20)
                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                if (qty <= 0) { j++; continue; }

                openTrade = new BacktestTradeResult(
                    Guid.NewGuid().ToString(),
                    ToIstDateTime(nextCandle.Timestamp), entryPrice,
                    default, 0, sl, target, qty, 0, 0, isLong ? "LONG" : "SHORT");
                trailStop = sl;
                remainingQty = qty;
                movedToBE = false;

                var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
                if (entryBarExit != null)
                {
                    var closed = ApplyCostsWithQty(entryBarExit, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, j);
                    openTrade = null;
                }
                j++;
            }

            if (openTrade != null)
            {
                var lastCandle = day[^1].Candle;
                var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(lastCandle.Timestamp), (double)lastCandle.Close, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
            }
        }

        return trades;
    }

    // ─────────────────────────────────────────────────────────
    // STRATEGY B: RSI Reversal
    // ─────────────────────────────────────────────────────────
    private List<BacktestTradeResult> RunRsiReversal(
        List<Candle> candles, IndicatorValues[] indicators, DateTimeOffset[] istTimes,
        StrategyParams p, double initialCapital)
    {
        var trades = new List<BacktestTradeResult>();
        var oversold = p.RsiOversold ?? 30;
        var overbought = p.RsiOverbought ?? 70;

        double runningCapital = initialCapital;
        double peakCapital = initialCapital;

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;
        int remainingQty = 0;
        bool movedToBE = false;
        var risk = new DayRiskState { DayStartCapital = initialCapital };
        DateTime lastDayDate = DateTime.MinValue;

        for (int i = MinWarmupBars; i < candles.Count - 1; i++)
        {
            var candle = candles[i];
            var ind = indicators[i];
            var istTime = istTimes[i];

            // Day boundary uses IST date — correct for session reset
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
                }
                risk = new DayRiskState { DayStartCapital = runningCapital };
                lastDayDate = istTime.Date;
            }

            if (openTrade != null)
            {
                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

                var exitResult = CheckExit(openTrade, candle, (double)ind.ATR, p, ref trailStop);
                if (exitResult != null)
                {
                    var closed = ApplyCostsWithQty(exitResult, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                    openTrade = null;
                }
                else
                {
                    continue;
                }
            }

            if (i == candles.Count - 2 && openTrade != null)
            {
                var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(candle.Timestamp), (double)candle.Close, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
                openTrade = null;
                continue;
            }

            if (openTrade != null) continue;

            var atr = (double)ind.ATR;
            if (atr <= 0) continue;

            var rsiNow = (double)ind.RSI;
            var rsiNext = (double)indicators[i + 1].RSI;

            bool longSignal = rsiNow < oversold && rsiNext > oversold;
            bool shortSignal = rsiNow > overbought && rsiNext < overbought;
            if (!longSignal && !shortSignal) continue;

            bool isLong = longSignal;
            string direction = isLong ? "LONG" : "SHORT";

            if (!risk.CanTrade(i, direction)) continue;
            if (i + 2 >= candles.Count) continue;

            var entryCandle = candles[i + 2];
            var entryInd = indicators[i + 2];

            if (entryInd.VWAP > 0)
            {
                if (isLong && (double)entryCandle.Open <= (double)entryInd.VWAP) { i += 2; continue; }
                if (!isLong && (double)entryCandle.Open >= (double)entryInd.VWAP) { i += 2; continue; }
            }

            if (!PassesConfluence(entryInd, isLong)) { i += 2; continue; }

            var rawEntry = (double)entryCandle.Open;
            var entryPrice = ApplySlippage(rawEntry, isLong);
            var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candles[i].Low, (double)candles[i].High, isLong);
            var sl = isLong ? entryPrice - slDistance : entryPrice + slDistance;
            var target = CalcTarget(p, entryPrice, slDistance, atr, isLong);

            var rrRatio = slDistance > 0 ? Math.Abs(target - entryPrice) / slDistance : 0;
            if (rrRatio < MinRRForEntry) { i += 2; continue; }

            var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
            if (effectiveRisk <= 0) { i += 2; continue; }
            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
            if (qty <= 0) { i += 2; continue; }

            var notional = entryPrice * qty;
            if (notional > runningCapital * 0.20)
                qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
            if (qty <= 0) { i += 2; continue; }

            openTrade = new BacktestTradeResult(
                Guid.NewGuid().ToString(),
                ToIstDateTime(entryCandle.Timestamp), entryPrice,
                default, 0, sl, target, qty, 0, 0, direction);
            trailStop = sl;
            remainingQty = qty;
            movedToBE = false;

            var entryBarExit = CheckExit(openTrade, entryCandle, atr, p, ref trailStop);
            if (entryBarExit != null)
            {
                var closed = ApplyCostsWithQty(entryBarExit, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
                risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                openTrade = null;
            }
            i += 2;
        }

        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(last.Timestamp), (double)last.Close, remainingQty);
            runningCapital += closed.Pnl;
            trades.Add(closed);
        }

        return trades;
    }

    // ─────────────────────────────────────────────────────────
    // STRATEGY C: EMA Crossover
    // ─────────────────────────────────────────────────────────
    private List<BacktestTradeResult> RunEmaCrossover(
        List<Candle> candles, IndicatorValues[] indicators, DateTimeOffset[] istTimes,
        StrategyParams p, double initialCapital)
    {
        var trades = new List<BacktestTradeResult>();
        double runningCapital = initialCapital;
        double peakCapital = initialCapital;

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;
        int remainingQty = 0;
        bool movedToBE = false;
        var risk = new DayRiskState { DayStartCapital = initialCapital };
        DateTime lastDayDate = DateTime.MinValue;

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
                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

                bool oppositeSignal = (openTrade.TradeType == "LONG" && bearishCross)
                                   || (openTrade.TradeType == "SHORT" && bullishCross);

                if (oppositeSignal)
                {
                    var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(candle.Timestamp), (double)candle.Close, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                    openTrade = null;
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
                    }
                    continue;
                }
            }

            if (openTrade != null) continue;
            if (atr <= 0) continue;
            if ((double)curInd.ADX <= 20) continue;

            bool isLong = bullishCross;
            bool hasSignal = bullishCross || bearishCross;
            if (!hasSignal) continue;

            if (!PassesConfluence(curInd, isLong)) continue;
            if (!HasVolumeConfirmation(candles, i)) continue;

            string direction = isLong ? "LONG" : "SHORT";
            if (!risk.CanTrade(i, direction)) continue;

            if (i + 1 >= candles.Count) continue;

            var nextCandle = candles[i + 1];
            var rawEntry = (double)nextCandle.Open;
            var entryPrice = ApplySlippage(rawEntry, isLong);
            var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, isLong);
            var sl = isLong ? entryPrice - slDistance : entryPrice + slDistance;
            var target = CalcTarget(p, entryPrice, slDistance, atr, isLong);

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

            var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
            if (entryBarExit != null)
            {
                var closed = ApplyCostsWithQty(entryBarExit, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
                risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                openTrade = null;
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

        return trades;
    }

    // ─────────────────────────────────────────────────────────
    // SHARED HELPERS
    // ─────────────────────────────────────────────────────────

    private static double ApplySlippage(double price, bool isLong) =>
        isLong ? price * (1 + SlippageFraction) : price * (1 - SlippageFraction);

    private static double CalcStopLossDistance(
        StrategyParams p, double entryPrice, double atr, double candleLow, double candleHigh, bool isLong)
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

    private static double CalcTarget(StrategyParams p, double entryPrice, double slDistance, double atr, bool isLong)
    {
        if (p.TargetType.Equals("RR_RATIO", StringComparison.OrdinalIgnoreCase))
        {
            var targetDistance = slDistance * (p.RrRatio ?? 2.0);
            return isLong ? entryPrice + targetDistance : entryPrice - targetDistance;
        }
        var farDistance = slDistance * 10;
        return isLong ? entryPrice + farDistance : entryPrice - farDistance;
    }

    private static int CalcQuantity(double capital, double riskPercent, double slDistance)
    {
        if (slDistance <= 0) return 0;
        var riskAmount = capital * (riskPercent / 100.0);
        return Math.Max((int)Math.Floor(riskAmount / slDistance), 1);
    }

    /// <summary>
    /// Exit check. Uses candle OHLC (price data — timezone-irrelevant).
    /// Exit times are converted to IST via CloseTrade → ToIstDateTime.
    /// </summary>
    private static BacktestTradeResult? CheckExit(
        BacktestTradeResult trade, Candle candle, double atr, StrategyParams p, ref double trailStop)
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

    private static BacktestTradeResult CloseTrade(BacktestTradeResult open, DateTime exitTime, double exitPrice)
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

    private static BacktestTradeResult ApplyCosts(BacktestTradeResult closed)
    {
        var turnover = (closed.EntryPrice + closed.ExitPrice) * closed.Quantity;
        var totalCommission = turnover * CommissionFraction;
        var adjustedPnl = closed.Pnl - totalCommission;
        var adjustedPct = closed.EntryPrice != 0 && closed.Quantity > 0
            ? (adjustedPnl / (closed.EntryPrice * closed.Quantity)) * 100.0
            : 0;
        return closed with { Pnl = Math.Round(adjustedPnl, 2), PnlPercent = Math.Round(adjustedPct, 2) };
    }

    private static BacktestTradeResult ApplyCostsWithQty(BacktestTradeResult closed, int qty)
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

    private static BacktestTradeResult CloseRemainingWithCosts(
        BacktestTradeResult open, DateTime exitTime, double exitPrice, int qty)
    {
        var closed = CloseTrade(open, exitTime, exitPrice);
        return ApplyCostsWithQty(closed, qty);
    }

    // ─────────────────────────────────────────────────────────
    // METRICS
    // ─────────────────────────────────────────────────────────

    private static BacktestMetrics CalculateMetrics(
        List<BacktestTradeResult> trades, double initialCapital, List<Candle> candles)
    {
        if (trades.Count == 0)
            return EmptyMetrics(initialCapital, candles.Count > 0 ? candles[0].Timestamp.UtcDateTime : DateTime.UtcNow);

        int totalTrades = trades.Count;
        int winningTrades = trades.Count(t => t.Pnl > 0);
        int losingTrades = trades.Count(t => t.Pnl < 0);
        double winRate = (double)winningTrades / totalTrades;
        double totalPnl = trades.Sum(t => t.Pnl);
        double totalReturn = (totalPnl / initialCapital) * 100.0;

        double grossProfit = trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        double grossLoss = Math.Abs(trades.Where(t => t.Pnl < 0).Sum(t => t.Pnl));
        double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 9999.0 : 0;

        double avgRR = trades.Average(t =>
        {
            var risk = Math.Abs(t.EntryPrice - t.StopLoss);
            if (risk <= 0) return 0;
            bool isLong = t.TradeType == "LONG";
            var signedMove = isLong ? t.ExitPrice - t.EntryPrice : t.EntryPrice - t.ExitPrice;
            return signedMove / risk;
        });

        // Equity curve uses IST times for display consistency.
        // Internally, candle ordering by UTC is fine (IST = UTC+5:30, order is preserved).
        var equityCurve = new List<EquityPoint>();
        double equity = initialCapital;
        double peak = initialCapital;
        double maxDrawdown = 0;

        if (candles.Count > 0)
            equityCurve.Add(new EquityPoint(ToIstDateTime(candles[0].Timestamp), initialCapital));

        var orderedTrades = trades.OrderBy(t => t.EntryTime).ToList();
        int tradeIdx = 0;
        BacktestTradeResult? activeTrade = null;

        foreach (var candle in candles)
        {
            // Trade times are already IST (from ToIstDateTime), so equity curve
            // timestamps must also be IST for correct comparison.
            var candleIstTime = ToIstDateTime(candle.Timestamp);

            while (tradeIdx < orderedTrades.Count && orderedTrades[tradeIdx].EntryTime <= candleIstTime)
            {
                var t = orderedTrades[tradeIdx];
                if (t.ExitTime <= candleIstTime)
                {
                    equity += t.Pnl;
                    tradeIdx++;
                }
                else
                {
                    activeTrade = t;
                    tradeIdx++;
                    break;
                }
            }

            if (activeTrade != null && activeTrade.ExitTime <= candleIstTime)
            {
                equity += activeTrade.Pnl;
                activeTrade = null;
            }

            double mtm = equity;
            if (activeTrade != null)
            {
                double cp = (double)candle.Close;
                mtm += activeTrade.TradeType == "LONG"
                    ? (cp - activeTrade.EntryPrice) * activeTrade.Quantity
                    : (activeTrade.EntryPrice - cp) * activeTrade.Quantity;
            }

            equityCurve.Add(new EquityPoint(candleIstTime, Math.Round(mtm, 2)));
            if (mtm > peak) peak = mtm;
            var dd = peak - mtm;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }

        return new BacktestMetrics(
            TotalTrades: totalTrades,
            WinRate: Math.Round(winRate, 4),
            TotalPnl: Math.Round(totalPnl, 2),
            MaxDrawdown: Math.Round(maxDrawdown, 2),
            AvgRR: Math.Round(avgRR, 2),
            WinningTrades: winningTrades,
            LosingTrades: losingTrades,
            TotalReturn: Math.Round(totalReturn, 2),
            ProfitFactor: Math.Round(profitFactor, 2),
            EquityCurve: equityCurve);
    }

    private static BacktestMetrics EmptyMetrics(double initialCapital, DateTime timestamp) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, [new EquityPoint(timestamp, initialCapital)]);
}