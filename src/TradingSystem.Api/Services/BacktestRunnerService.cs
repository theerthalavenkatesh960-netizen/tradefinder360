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

        BacktestAnnotations? annotations = null;
        List<BacktestTradeResult> tradeList;

        if (request.Strategy.Name.Equals("ORB_FVG_RETEST", StringComparison.OrdinalIgnoreCase))
        {
            var (trades, annot) = RunOrbFvgRetest(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital);
            tradeList = trades;
            annotations = annot;
        }
        else
        {
            var trades = request.Strategy.Name.ToUpperInvariant() switch
            {
                "ORB" => RunORB(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "RSI_REVERSAL" => RunRsiReversal(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "EMA_CROSSOVER" => RunEmaCrossover(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "EMA_PULLBACK" => RunEmaPullback(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "EMA_SPEED" => RunEmaSpeed(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "EMA_PULLBACK_SPEED" => RunEmaPullbackSpeed(orderedCandles, indicators, istTimes, request.Strategy.Params, initialCapital),
                "SMC_FVG" => RunSmcFvg(instrument.Id, request.From, request.To, request.Strategy.Params, initialCapital),
                _ => throw new ArgumentException($"Unknown strategy: {request.Strategy.Name}")
            };
            tradeList = trades.ToList();
        }

        var metrics = CalculateMetrics(tradeList, initialCapital, orderedCandles);
        return new BacktestResponse(tradeList, metrics, annotations);
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
    // STRATEGY A.5: Opening Range Breakout + FVG Retest
    // ─────────────────────────────────────────────────────────
    private (List<BacktestTradeResult> trades, BacktestAnnotations annotations) RunOrbFvgRetest(
        List<Candle> candles, IndicatorValues[] indicators, DateTimeOffset[] istTimes,
        StrategyParams p, double initialCapital)
    {
        var trades          = new List<BacktestTradeResult>();
        var orbAnnotations  = new List<OrbAnnotation>();
        var fvgAnnotations  = new List<FvgAnnotation>();
        var obAnnotations   = new List<OrderBlockAnnotation>();
        var eventAnnotations = new List<SignalEventAnnotation>();

        // Index-based zones built directly — no post-loop timestamp lookup needed
        var orbZones = new List<OrbZone>();
        var fvgZones = new List<FvgZone>();
        var obZones = new List<OrderBlockZone>();

        // Prevent duplicate context zones when both context-scan and setup-detection hit the same area
        var seenFvgKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenObKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddFvgZone(int startIdx, int endIdx, double high, double low, string direction, DateTimeOffset formedAt)
        {
            var key = $"{startIdx}:{direction}:{Math.Round(high, 4)}:{Math.Round(low, 4)}";
            if (!seenFvgKeys.Add(key)) return;

            fvgZones.Add(new FvgZone(startIdx, endIdx, high, low, direction));
            fvgAnnotations.Add(new FvgAnnotation(ToIstDateTime(formedAt), low, high, direction));
        }

        void AddOrderBlockZone(int startIdx, int endIdx, double high, double low, string direction, DateTimeOffset formedAt)
        {
            var key = $"{startIdx}:{direction}:{Math.Round(high, 4)}:{Math.Round(low, 4)}";
            if (!seenObKeys.Add(key)) return;

            obZones.Add(new OrderBlockZone(startIdx, endIdx, high, low));
            obAnnotations.Add(new OrderBlockAnnotation(ToIstDateTime(formedAt), high, low, direction));
        }

        int orbCandleCount   = p.Timeframe switch { 1 => 5, _ => 1 };
        double runningCapital = initialCapital;
        double peakCapital   = initialCapital;

        var dayGroups = candles
            .Select((c, i) => (Candle: c, Index: i, Indicator: indicators[i], IstTime: istTimes[i]))
            .GroupBy(x => x.IstTime.Date)
            .OrderBy(g => g.Key);

        foreach (var dayGroup in dayGroups)
        {
            var day = dayGroup.OrderBy(x => x.Candle.Timestamp).ToList();
            if (day.Count <= orbCandleCount + 3) continue;

            var orbCandles  = day.Take(orbCandleCount).ToList();
            double openingHigh  = (double)orbCandles.Max(x => x.Candle.High);
            double openingLow   = (double)orbCandles.Min(x => x.Candle.Low);
            double openingRange = openingHigh - openingLow;
            int dayStartGlobalIdx = day[0].Index;
            int dayEndGlobalIdx   = day[^1].Index;

            // Context scan: show FVG/OB structure for the day even if no entry triggers.
            for (int j = orbCandleCount + 2; j < day.Count; j++)
            {
                var c0 = day[j - 2].Candle;
                var c2 = day[j].Candle;

                if ((double)c0.High < (double)c2.Low)
                {
                    // Bullish FVG
                    var gapLow = (double)c0.High;
                    var gapHigh = (double)c2.Low;
                    AddFvgZone(day[j].Index, dayEndGlobalIdx, gapHigh, gapLow, "BULLISH", c2.Timestamp);

                    if (p.IncludeOrderBlocks == true)
                    {
                        for (int b = j - 1; b >= Math.Max(orbCandleCount, j - 20); b--)
                        {
                            var obCandle = day[b].Candle;
                            if ((double)obCandle.Close < (double)obCandle.Open)
                            {
                                AddOrderBlockZone(day[b].Index, dayEndGlobalIdx,
                                    (double)obCandle.High, (double)obCandle.Low,
                                    "BULLISH", obCandle.Timestamp);
                                break;
                            }
                        }
                    }
                }
                else if ((double)c0.Low > (double)c2.High)
                {
                    // Bearish FVG
                    var gapLow = (double)c2.High;
                    var gapHigh = (double)c0.Low;
                    AddFvgZone(day[j].Index, dayEndGlobalIdx, gapHigh, gapLow, "BEARISH", c2.Timestamp);

                    if (p.IncludeOrderBlocks == true)
                    {
                        for (int b = j - 1; b >= Math.Max(orbCandleCount, j - 20); b--)
                        {
                            var obCandle = day[b].Candle;
                            if ((double)obCandle.Close > (double)obCandle.Open)
                            {
                                AddOrderBlockZone(day[b].Index, dayEndGlobalIdx,
                                    (double)obCandle.High, (double)obCandle.Low,
                                    "BEARISH", obCandle.Timestamp);
                                break;
                            }
                        }
                    }
                }
            }

            // ── ALWAYS record ORB zone so replay shows the range every day ────────
            var orbAnnotation = new OrbAnnotation(
                ToIstDateTime(orbCandles[^1].Candle.Timestamp), openingHigh, openingLow);
            orbAnnotations.Add(orbAnnotation);

            double firstAtr = (double)orbCandles[0].Indicator.ATR;
            if (firstAtr <= 0 || openingRange < firstAtr * 0.3 || openingRange > firstAtr * 3.0)
            {
                // Invalid ORB — add zone with reason so replay still shows range
                string skipReason = firstAtr <= 0
                    ? "ATR not ready (warmup)"
                    : "ORB range outside ATR bounds — skipped";
                orbZones.Add(new OrbZone(dayStartGlobalIdx, dayEndGlobalIdx,
                    openingHigh, openingLow, skipReason));
                eventAnnotations.Add(new SignalEventAnnotation(
                    ToIstDateTime(day[^1].Candle.Timestamp), "TRADE_NOT_TAKEN", skipReason));
                continue;
            }

            // Valid ORB — add zone without reason initially; patch at end-of-day if no trade
            orbZones.Add(new OrbZone(dayStartGlobalIdx, dayEndGlobalIdx, openingHigh, openingLow));
            int thisOrbZoneIdx = orbZones.Count - 1;

            var risk = new DayRiskState { DayStartCapital = runningCapital };
            BacktestTradeResult? openTrade = null;
            double trailStop    = 0;
            int remainingQty    = 0;
            bool movedToBE      = false;
            bool tradeTakenToday = false;

            // ── State machine phases ──────────────────────────────────────────────
            // 0 = waiting for ORB breakout
            // 1 = breakout seen, collecting post-breakout candles, waiting for FVG
            // 2 = FVG formed, waiting for close inside gap (retest)
            // 3 = retest confirmed, waiting for engulfing on NEXT candle
            int phase = 0;
            bool? breakoutDirection = null;
            var postBreakoutCandles = new List<(Candle Candle, int GlobalIdx)>();
            (double GapLow, double GapHigh)? fvgRange = null;
            int fvgGlobalIdx = 0;
            Candle? retestCandle = null;

            string dayPhaseReason = "No breakout from ORB range";

            for (int j = orbCandleCount; j < day.Count; j++)
            {
                var (candle, globalIdx, ind, istTime) = day[j];

                // ── Force-close at end of day ─────────────────────────────────────
                if (j == day.Count - 1 && openTrade != null)
                {
                    var closed = CloseRemainingWithCosts(openTrade,
                        ToIstDateTime(candle.Timestamp), (double)candle.Close, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, j);
                    openTrade = null;
                    break;
                }

                // ── Manage open position ──────────────────────────────────────────
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

                if (TimeOnly.FromDateTime(istTime.DateTime) >= NoCutoff) continue;
                if (!risk.CanTrade(j, "")) continue;

                double atr = (double)ind.ATR;
                if (atr <= 0) continue;

                // ── PHASE 0: Wait for ORB breakout ────────────────────────────────
                if (phase == 0)
                {
                    bool longBreak  = (double)candle.Close > openingHigh;
                    bool shortBreak = (double)candle.Close < openingLow;
                    if (!longBreak && !shortBreak) continue;

                    bool isLong = longBreak;
                    if (!PassesConfluence(indicators[globalIdx], isLong))
                    {
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "CONFLUENCE_FAIL",
                            $"Breakout failed confluence check — {(isLong ? "RSI too high" : "RSI too low")}"));
                        continue;
                    }
                    if (!HasVolumeConfirmation(candles, globalIdx))
                    {
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "VOLUME_FAIL",
                            "Breakout failed volume confirmation check"));
                        continue;
                    }

                    breakoutDirection = isLong;
                    postBreakoutCandles.Clear();
                    phase = 1;
                    dayPhaseReason = "Breakout confirmed — waiting for FVG to form";
                    eventAnnotations.Add(new SignalEventAnnotation(
                        ToIstDateTime(candle.Timestamp), "BREAKOUT",
                        $"{(isLong ? "Bullish" : "Bearish")} breakout confirmed"));
                    continue;
                }

                // ── PHASE 1: Accumulate post-breakout bars and scan for FVG ──────
                if (phase == 1)
                {
                    postBreakoutCandles.Add((candle, globalIdx));
                    if (postBreakoutCandles.Count < 3) continue;

                    bool isBullish = breakoutDirection == true;
                    for (int k = 2; k < postBreakoutCandles.Count; k++)
                    {
                        var c0 = postBreakoutCandles[k - 2].Candle;
                        var c2 = postBreakoutCandles[k].Candle;

                        if (isBullish && (double)c0.High < (double)c2.Low)
                        {
                            double gapLow  = (double)c0.High;
                            double gapHigh = (double)c2.Low;
                            fvgRange     = (gapLow, gapHigh);
                            fvgGlobalIdx = postBreakoutCandles[k].GlobalIdx;

                            AddFvgZone(fvgGlobalIdx, dayEndGlobalIdx, gapHigh, gapLow, "BULLISH", c2.Timestamp);
                            eventAnnotations.Add(new SignalEventAnnotation(
                                ToIstDateTime(c2.Timestamp), "FVG_FORMED", "Bullish FVG detected"));
                            phase = 2;
                            dayPhaseReason = "FVG formed — waiting for price to retest gap";
                            break;
                        }
                        else if (!isBullish && (double)c0.Low > (double)c2.High)
                        {
                            double gapLow  = (double)c2.High;
                            double gapHigh = (double)c0.Low;
                            fvgRange     = (gapLow, gapHigh);
                            fvgGlobalIdx = postBreakoutCandles[k].GlobalIdx;

                            AddFvgZone(fvgGlobalIdx, dayEndGlobalIdx, gapHigh, gapLow, "BEARISH", c2.Timestamp);
                            eventAnnotations.Add(new SignalEventAnnotation(
                                ToIstDateTime(c2.Timestamp), "FVG_FORMED", "Bearish FVG detected"));
                            phase = 2;
                            dayPhaseReason = "FVG formed — waiting for price to retest gap";
                            break;
                        }
                    }
                    continue;
                }

                // ── PHASE 2: Wait for candle CLOSE inside the FVG ────────────────
                // Matches live StrategyOrchestrator: fvg.Contains(candle.Close)
                if (phase == 2 && fvgRange.HasValue)
                {
                    double close      = (double)candle.Close;
                    bool closeInGap   = close >= fvgRange.Value.GapLow && close <= fvgRange.Value.GapHigh;
                    if (!closeInGap) continue;

                    retestCandle   = candle;
                    phase          = 3;
                    dayPhaseReason = "FVG retested — waiting for engulfing candle";
                    eventAnnotations.Add(new SignalEventAnnotation(
                        ToIstDateTime(candle.Timestamp), "RETEST",
                        $"Close inside {(breakoutDirection == true ? "bullish" : "bearish")} FVG gap"));
                    continue;
                }

                // ── PHASE 3: NEXT candle must engulf the retest candle ────────────
                // Matches live EngulfingConfirmation.IsEngulfing(retestCandle, currentCandle)
                if (phase == 3 && retestCandle != null)
                {
                    bool isBullish = breakoutDirection == true;

                    bool engulfing = isBullish
                        ? (double)candle.Open  < (double)retestCandle.Low
                       && (double)candle.Close > (double)retestCandle.High
                        : (double)candle.Open  > (double)retestCandle.High
                       && (double)candle.Close < (double)retestCandle.Low;

                    if (!engulfing)
                    {
                        double retestLow  = (double)retestCandle.Low;
                        double retestHigh = (double)retestCandle.High;
                        double candleOpen  = (double)candle.Open;
                        double candleClose = (double)candle.Close;
                        
                        // If candle's close is still inside the gap it becomes the new retest candle
                        double close = (double)candle.Close;
                        bool stillInGap = fvgRange.HasValue &&
                            close >= fvgRange.Value.GapLow && close <= fvgRange.Value.GapHigh;

                        string engulfFailReason = isBullish
                            ? $"Engulfing failed: Open {candleOpen:F2} >= Retest Low {retestLow:F2} OR Close {candleClose:F2} <= Retest High {retestHigh:F2}"
                            : $"Engulfing failed: Open {candleOpen:F2} <= Retest High {retestHigh:F2} OR Close {candleClose:F2} >= Retest Low {retestLow:F2}";

                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "ENGULF_FAIL",
                            engulfFailReason));

                        if (stillInGap)
                        {
                            retestCandle = candle;  // roll the retest forward
                            eventAnnotations.Add(new SignalEventAnnotation(
                                ToIstDateTime(candle.Timestamp), "RETEST_CONTINUED",
                                $"Close {candleClose:F2} still inside FVG [{fvgRange.Value.GapLow:F2}, {fvgRange.Value.GapHigh:F2}] — watching for engulfing"));
                        }
                        else
                        {
                            // Exited gap without engulfing — go back to watching for another retest
                            dayPhaseReason = "No engulfing after FVG retest — watching for re-entry";
                            phase = 2;
                            retestCandle = null;
                            eventAnnotations.Add(new SignalEventAnnotation(
                                ToIstDateTime(candle.Timestamp), "PHASE_BACK_TO_RETEST",
                                $"Price exited FVG [{fvgRange.Value.GapLow:F2}, {fvgRange.Value.GapHigh:F2}] without engulfing — watching for new retest"));
                        }
                        continue;
                    }

                    // Engulfing confirmed — entry on NEXT candle's open
                    if (j + 1 >= day.Count) continue;

                    eventAnnotations.Add(new SignalEventAnnotation(
                        ToIstDateTime(candle.Timestamp), "ENGULF_CONFIRMED",
                        $"{(isBullish ? "Bullish" : "Bearish")} engulfing confirmation: Open {(double)candle.Open:F2}, Close {(double)candle.Close:F2}"));

                    var entryCandle = day[j + 1].Candle;
                    double rawEntry  = (double)entryCandle.Open;
                    double entryPrice = ApplySlippage(rawEntry, isBullish);
                    double slDist     = CalcStopLossDistance(p, entryPrice, atr,
                                            (double)retestCandle.Low, (double)retestCandle.High, isBullish);
                    double sl         = isBullish ? entryPrice - slDist : entryPrice + slDist;
                    double target     = CalcTarget(p, entryPrice, slDist, atr, isBullish);

                    double rrRatio = slDist > 0 ? Math.Abs(target - entryPrice) / slDist : 0;
                    if (rrRatio < MinRRForEntry)
                    {
                        dayPhaseReason = $"R/R too low ({rrRatio:F2}) — minimum required {MinRRForEntry}";
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "RR_FAILED",
                            $"Risk/Reward ratio {rrRatio:F2} < {MinRRForEntry} — entry rejected"));
                        phase = 0; breakoutDirection = null; fvgRange = null; retestCandle = null;
                        postBreakoutCandles.Clear();
                        continue;
                    }

                    double effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                    if (effectiveRisk <= 0)
                    {
                        dayPhaseReason = "Drawdown halt — no new entries";
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "DRAWDOWN_HALT",
                            $"Drawdown > {DrawdownHaltThreshold}% — no new entries"));
                        phase = 0; breakoutDirection = null; fvgRange = null; retestCandle = null;
                        postBreakoutCandles.Clear();
                        continue;
                    }

                    int qty = CalcQuantity(runningCapital, effectiveRisk, slDist);
                    if (qty <= 0)
                    {
                        dayPhaseReason = "Quantity too small — position sizing failed";
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "QTY_FAILED",
                            $"Calculated quantity {qty} — position sizing failed"));
                        phase = 0; breakoutDirection = null; fvgRange = null; retestCandle = null;
                        postBreakoutCandles.Clear();
                        continue;
                    }

                    double notional = entryPrice * qty;
                    if (notional > runningCapital * 0.20)
                    {
                        qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "QTY_CAPPED",
                            $"Notional {notional:F2} exceeds 20% limit — qty capped to {qty}"));
                    }
                    if (qty <= 0)
                    {
                        dayPhaseReason = "Notional exceeds 20% capital limit";
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp), "QTY_FINAL_FAIL",
                            "Adjusted quantity still <= 0 — entry rejected"));
                        phase = 0; breakoutDirection = null; fvgRange = null; retestCandle = null;
                        postBreakoutCandles.Clear();
                        continue;
                    }

                    openTrade = new BacktestTradeResult(
                        Guid.NewGuid().ToString(),
                        ToIstDateTime(entryCandle.Timestamp), entryPrice,
                        default, 0, sl, target, qty, 0, 0, isBullish ? "LONG" : "SHORT");
                    trailStop    = sl;
                    remainingQty = qty;
                    movedToBE    = false;
                    tradeTakenToday = true;

                    eventAnnotations.Add(new SignalEventAnnotation(
                        ToIstDateTime(entryCandle.Timestamp), "ENTRY",
                        $"{(isBullish ? "LONG" : "SHORT")} entry at {entryPrice:F2}  SL={sl:F2}  Target={target:F2}"));

                    var entryBarExit = CheckExit(openTrade, entryCandle, atr, p, ref trailStop);
                    if (entryBarExit != null)
                    {
                        var closed = ApplyCostsWithQty(entryBarExit, remainingQty);
                        runningCapital += closed.Pnl;
                        if (runningCapital > peakCapital) peakCapital = runningCapital;
                        trades.Add(closed);
                        risk.RecordTrade(closed.Pnl, closed.TradeType, j);
                        openTrade = null;
                    }

                    // Reset phase state for potential second setup on the same day
                    phase = 0; breakoutDirection = null; fvgRange = null; retestCandle = null;
                    postBreakoutCandles.Clear();
                    j += 1; // skip the entry candle (already processed above)
                }
            } // end bar loop

            // Force-close any still-open position at day end
            if (openTrade != null)
            {
                var lastCandle = day[^1].Candle;
                var closed = CloseRemainingWithCosts(openTrade,
                    ToIstDateTime(lastCandle.Timestamp), (double)lastCandle.Close, remainingQty);
                runningCapital += closed.Pnl;
                if (runningCapital > peakCapital) peakCapital = runningCapital;
                trades.Add(closed);
            }

            // ── Per-day TRADE_NOT_TAKEN event + patch ORB zone reason ─────────────
            if (!tradeTakenToday)
            {
                eventAnnotations.Add(new SignalEventAnnotation(
                    ToIstDateTime(day[^1].Candle.Timestamp),
                    "TRADE_NOT_TAKEN",
                    dayPhaseReason));

                // Patch the ORB zone entry for this day to show the reason on replay
                var existing = orbZones[thisOrbZoneIdx];
                orbZones[thisOrbZoneIdx] = existing with { TradeNotTakenReason = dayPhaseReason };
            }
        } // end day loop

        // ── Post-process: Mark ALL retests into ALL FVG zones ──────────────────
        // This ensures replay shows retest activity for context FVGs even if no trade
        var retestEventKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var fvgZone in fvgZones)
        {
            // Scan all candles within this FVG zone's time range
            for (int idx = fvgZone.FvgStartIdx; idx <= fvgZone.FvgEndIdx && idx < candles.Count; idx++)
            {
                var candle = candles[idx];
                double close = (double)candle.Close;

                // Check if close falls inside the gap
                if (close >= fvgZone.FvgLow && close <= fvgZone.FvgHigh)
                {
                    // Create a unique key to avoid duplicate retest events
                    var retestKey = $"{idx}:{fvgZone.Direction}:{Math.Round(fvgZone.FvgLow, 4)}:{Math.Round(fvgZone.FvgHigh, 4)}";
                    if (retestEventKeys.Add(retestKey))
                    {
                        eventAnnotations.Add(new SignalEventAnnotation(
                            ToIstDateTime(candle.Timestamp),
                            "RETEST",
                            $"{fvgZone.Direction} FVG retested at {close:F2}"));
                    }
                }
            }
        }

        var annotations = new BacktestAnnotations(
            OrbZones: orbZones,
            FvgZones: fvgZones,
            ObZones: obZones,
            RetraceEvent: null,
            EngulfingEvent: null,
            Orbs: orbAnnotations,
            Fvgs: fvgAnnotations,
            OrderBlocks: obAnnotations,
            Events: eventAnnotations
        );

        return (trades, annotations);
    }

    // Helper: convert OB annotation from timestamp to index-based for chart rendering
    private OrderBlockZone ConvertOrderBlockAnnotation(List<Candle> candles, OrderBlockAnnotation ob)
    {
        var obIdx = candles.FindIndex(c => c.Timestamp == ob.Timestamp);
        return new OrderBlockZone(
            ObStartIdx: Math.Max(0, obIdx),
            ObEndIdx: Math.Max(0, obIdx + 2),
            ObHigh: ob.High,
            ObLow: ob.Low
        );
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
    // STRATEGY D: EMA Pullback / Retest
    // ─────────────────────────────────────────────────────────
    private static readonly TimeOnly EmaPullbackCutoff = new(13, 30);

    private static List<BacktestTradeResult> RunEmaPullback(
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

        // Pullback-wait state — at most one pending setup at a time
        bool waitingForLong = false;
        bool waitingForShort = false;
        double longSwingTarget = 0;
        double shortSwingTarget = 0;

        for (int i = Math.Max(1, MinWarmupBars); i < candles.Count; i++)
        {
            var prevInd = indicators[i - 1];
            var curInd = indicators[i];
            var candle = candles[i];
            var atr = (double)curInd.ATR;
            var istTime = istTimes[i];

            // ── Day boundary reset ──
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
                // Abandon any pending pullback setup at day boundary
                waitingForLong = false;
                waitingForShort = false;
                risk = new DayRiskState { DayStartCapital = runningCapital };
                lastDayDate = istTime.Date;
            }

            // ── Manage open position ──
            if (openTrade != null)
            {
                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

                var exitResult = CheckExit(openTrade, candle, atr, p, ref trailStop);
                if (exitResult != null)
                {
                    var closed = ApplyCostsWithQty(exitResult, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    risk.RecordTrade(closed.Pnl, closed.TradeType, i);
                    openTrade = null;
                    // Don't start tracking new setups while position just closed on same bar
                }
                else
                {
                    continue;
                }
            }

            if (atr <= 0) continue;

            var timeOfDay = TimeOnly.FromDateTime(istTime.DateTime);
            bool pastCutoff = timeOfDay >= EmaPullbackCutoff;

            // ── Detect new crossovers ──
            bool bullishCross = prevInd.EMAFast <= prevInd.EMASlow && curInd.EMAFast > curInd.EMASlow;
            bool bearishCross = prevInd.EMAFast >= prevInd.EMASlow && curInd.EMAFast < curInd.EMASlow;

            // A new opposite crossover cancels any pending setup
            if (bearishCross) waitingForLong = false;
            if (bullishCross) waitingForShort = false;

            // ── Register new crossover setups (only before cutoff) ──
            if (!pastCutoff && bullishCross && !waitingForLong)
            {
                // Target: max High of the previous bullish EMA run (before the bearish phase that just ended)
                double bullRunHigh = FindPreviousBullRunHigh(candles, indicators, i);
                if (bullRunHigh > 0)
                {
                    if ((double)candle.Close < (double)curInd.EMAFast)
                    {
                        // Case A: price already below FastEMA — immediate long entry on next bar
                        if (i + 1 < candles.Count && risk.CanTrade(i, "LONG"))
                        {
                            var nextCandle = candles[i + 1];
                            var rawEntry = (double)nextCandle.Open;
                            var entryPrice = ApplySlippage(rawEntry, true);
                            var swingLow = FindRecentSwingLow(candles, i);
                            var slDistance = Math.Max(entryPrice - (swingLow - atr * 0.1), atr * 0.25);
                            var sl = entryPrice - slDistance;
                            var target = bullRunHigh;

                            var rrRatio = slDistance > 0 ? (target - entryPrice) / slDistance : 0;
                            if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, true) && HasVolumeConfirmation(candles, i))
                            {
                                var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                                if (effectiveRisk > 0)
                                {
                                    var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                                    if (qty > 0)
                                    {
                                        var notional = entryPrice * qty;
                                        if (notional > runningCapital * 0.20)
                                            qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                        if (qty > 0)
                                        {
                                            openTrade = new BacktestTradeResult(
                                                Guid.NewGuid().ToString(),
                                                ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                                default, 0, sl, target, qty, 0, 0, "LONG");
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
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Case B: price above FastEMA — wait for pullback
                        waitingForLong = true;
                        longSwingTarget = bullRunHigh;
                    }
                }
            }

            if (!pastCutoff && bearishCross && !waitingForShort)
            {
                // Target: min Low of the previous bearish EMA run (before the bullish phase that just ended)
                double bearRunLow = FindPreviousBearRunLow(candles, indicators, i);
                if (bearRunLow > 0)
                {
                    if ((double)candle.Close > (double)curInd.EMAFast)
                    {
                        // Case A: price already above FastEMA — immediate short entry on next bar
                        if (i + 1 < candles.Count && risk.CanTrade(i, "SHORT"))
                        {
                            var nextCandle = candles[i + 1];
                            var rawEntry = (double)nextCandle.Open;
                            var entryPrice = ApplySlippage(rawEntry, false);
                            var swingHigh = FindRecentSwingHigh(candles, i);
                            var slDistance = Math.Max((swingHigh + atr * 0.1) - entryPrice, atr * 0.25);
                            var sl = entryPrice + slDistance;
                            var target = bearRunLow;

                            var rrRatio = slDistance > 0 ? (entryPrice - target) / slDistance : 0;
                            if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, false) && HasVolumeConfirmation(candles, i))
                            {
                                var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                                if (effectiveRisk > 0)
                                {
                                    var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                                    if (qty > 0)
                                    {
                                        var notional = entryPrice * qty;
                                        if (notional > runningCapital * 0.20)
                                            qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                        if (qty > 0)
                                        {
                                            openTrade = new BacktestTradeResult(
                                                Guid.NewGuid().ToString(),
                                                ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                                default, 0, sl, target, qty, 0, 0, "SHORT");
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
                                            continue;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Case B: price below FastEMA — wait for pullback
                        waitingForShort = true;
                        shortSwingTarget = bearRunLow;
                    }
                }
            }

            // ── Pullback wait: check if current bar qualifies as retest entry ──
            if (waitingForLong && openTrade == null && !pastCutoff && i + 1 < candles.Count)
            {
                // Wick touches FastEMA and candle closes green
                bool wickTouchesEma = (double)candle.Low <= (double)curInd.EMAFast;
                bool closesGreen = candle.IsBullish;
                if (wickTouchesEma && closesGreen && risk.CanTrade(i, "LONG"))
                {
                    var nextCandle = candles[i + 1];
                    var rawEntry = (double)nextCandle.Open;
                    var entryPrice = ApplySlippage(rawEntry, true);
                    var slDistance = Math.Max(entryPrice - ((double)candle.Low - atr * 0.1), atr * 0.25);
                    var sl = entryPrice - slDistance;
                    var target = longSwingTarget;

                    var rrRatio = slDistance > 0 ? (target - entryPrice) / slDistance : 0;
                    if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, true) && HasVolumeConfirmation(candles, i))
                    {
                        var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                        if (effectiveRisk > 0)
                        {
                            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                            if (qty > 0)
                            {
                                var notional = entryPrice * qty;
                                if (notional > runningCapital * 0.20)
                                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                if (qty > 0)
                                {
                                    openTrade = new BacktestTradeResult(
                                        Guid.NewGuid().ToString(),
                                        ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                        default, 0, sl, target, qty, 0, 0, "LONG");
                                    trailStop = sl;
                                    remainingQty = qty;
                                    movedToBE = false;
                                    waitingForLong = false;

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
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            if (waitingForShort && openTrade == null && !pastCutoff && i + 1 < candles.Count)
            {
                // Wick touches FastEMA and candle closes red
                bool wickTouchesEma = (double)candle.High >= (double)curInd.EMAFast;
                bool closesRed = candle.IsBearish;
                if (wickTouchesEma && closesRed && risk.CanTrade(i, "SHORT"))
                {
                    var nextCandle = candles[i + 1];
                    var rawEntry = (double)nextCandle.Open;
                    var entryPrice = ApplySlippage(rawEntry, false);
                    var slDistance = Math.Max(((double)candle.High + atr * 0.1) - entryPrice, atr * 0.25);
                    var sl = entryPrice + slDistance;
                    var target = shortSwingTarget;

                    var rrRatio = slDistance > 0 ? (entryPrice - target) / slDistance : 0;
                    if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, false) && HasVolumeConfirmation(candles, i))
                    {
                        var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                        if (effectiveRisk > 0)
                        {
                            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                            if (qty > 0)
                            {
                                var notional = entryPrice * qty;
                                if (notional > runningCapital * 0.20)
                                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                if (qty > 0)
                                {
                                    openTrade = new BacktestTradeResult(
                                        Guid.NewGuid().ToString(),
                                        ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                        default, 0, sl, target, qty, 0, 0, "SHORT");
                                    trailStop = sl;
                                    remainingQty = qty;
                                    movedToBE = false;
                                    waitingForShort = false;

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
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            // Abandon pending setups once past the cutoff
            if (pastCutoff)
            {
                waitingForLong = false;
                waitingForShort = false;
            }
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
    // STRATEGY: EMA Speed (Pullback Speed Strategy)
    // ─────────────────────────────────────────────────────────
    // Adapted from: TradingView "EMA Pullback Speed Strategy"
    // Concept: Enter on shallow pullbacks toward the FastEMA in trending conditions,
    // confirmed by a strong momentum candle (body ≥ speedThreshold ticks).
    // Pullback must not exceed maxPullbackPct% from FastEMA.
    // SL: ATR-based (4× ATR). Target: CalcTarget (R:R or trailing per user params).
    // Cutoff: no new setups after 14:00 IST.
    private static readonly TimeOnly EmaSpeedCutoff = new(14, 0);

    private static List<BacktestTradeResult> RunEmaSpeed(
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

        const double MaxPullbackPct = 5.0;   // max % price can deviate above/below FastEMA for pullback entry
        const double SpeedThresholdPct = 0.5; // candle body must be ≥ 0.5% of price (approximates the "tick speed" filter)
        const double AtrSlMultiplier = 4.0;

        for (int i = Math.Max(1, MinWarmupBars); i < candles.Count; i++)
        {
            var curInd = indicators[i];
            var candle = candles[i];
            var atr = (double)curInd.ATR;
            var istTime = istTimes[i];

            // ── Day boundary reset ──
            if (istTime.Date != lastDayDate)
            {
                if (openTrade != null)
                {
                    var prev = candles[i - 1];
                    var closed = CloseRemainingWithCosts(openTrade, ToIstDateTime(prev.Timestamp), (double)prev.Close, remainingQty);
                    runningCapital += closed.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(closed);
                    openTrade = null;
                }
                risk = new DayRiskState { DayStartCapital = runningCapital };
                lastDayDate = istTime.Date;
            }

            // ── Manage open position ──
            if (openTrade != null)
            {
                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

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

            if (atr <= 0) continue;

            var timeOfDay = TimeOnly.FromDateTime(istTime.DateTime);
            if (timeOfDay >= EmaSpeedCutoff) continue;

            // ── Trend gate: FastEMA vs SlowEMA ──
            bool uptrend = curInd.EMAFast > curInd.EMASlow;
            bool downtrend = curInd.EMAFast < curInd.EMASlow;
            if (!uptrend && !downtrend) continue;

            double fastEma = (double)curInd.EMAFast;
            double close = (double)candle.Close;
            double open = (double)candle.Open;
            double candleBody = Math.Abs(close - open);
            double speedThreshold = close * SpeedThresholdPct / 100.0;

            // ── Momentum candle filter: body must be large enough (speed confirmation) ──
            if (candleBody < speedThreshold) continue;

            bool isLong = uptrend && candle.IsBullish;
            bool isShort = downtrend && candle.IsBearish;
            if (!isLong && !isShort) continue;

            // ── Pullback filter: price must be within MaxPullbackPct% of FastEMA ──
            // LONG: price pulled back toward FastEMA from above (close ≥ FastEMA, within 5%)
            // SHORT: price pulled back toward FastEMA from below (close ≤ FastEMA, within 5%)
            if (isLong)
            {
                if (close < fastEma) continue; // price below FastEMA — not a valid pullback in uptrend
                double pullbackPct = (close - fastEma) / fastEma * 100.0;
                if (pullbackPct > MaxPullbackPct) continue; // too far from EMA — not a shallow pullback
            }
            else
            {
                if (close > fastEma) continue; // price above FastEMA — not a valid pullback in downtrend
                double pullbackPct = (fastEma - close) / fastEma * 100.0;
                if (pullbackPct > MaxPullbackPct) continue;
            }

            if (!PassesConfluence(curInd, isLong)) continue;
            if (!HasVolumeConfirmation(candles, i)) continue;
            if (!risk.CanTrade(i, isLong ? "LONG" : "SHORT")) continue;
            if (i + 1 >= candles.Count) continue;

            var nextCandle = candles[i + 1];
            var rawEntry = (double)nextCandle.Open;
            var entryPrice = ApplySlippage(rawEntry, isLong);

            // SL: ATR × 4 (as per the original strategy)
            var slDistance = Math.Max(atr * AtrSlMultiplier, atr * 0.25);
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
                default, 0, sl, target, qty, 0, 0,
                isLong ? "LONG" : "SHORT");
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
    // STRATEGY: EMA Pullback + Speed (Combined)
    // ─────────────────────────────────────────────────────────
    // Merges EMA_PULLBACK (crossover-triggered entries) with EMA_SPEED (trend-continuation
    // momentum entries). Crossover setups take priority; speed entries fire only when
    // no crossover setup is pending. One open position at a time.
    private static readonly TimeOnly EmaPullbackSpeedCutoff = new(14, 0);

    private static List<BacktestTradeResult> RunEmaPullbackSpeed(
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

        // Pullback-wait state (from EMA_PULLBACK)
        bool waitingForLong = false;
        bool waitingForShort = false;
        double longSwingTarget = 0;
        double shortSwingTarget = 0;

        // Speed entry constants (from EMA_SPEED)
        const double MaxPullbackPct = 5.0;
        const double SpeedThresholdPct = 0.5;
        const double AtrSlMultiplier = 4.0;

        for (int i = Math.Max(1, MinWarmupBars); i < candles.Count; i++)
        {
            var prevInd = indicators[i - 1];
            var curInd = indicators[i];
            var candle = candles[i];
            var atr = (double)curInd.ATR;
            var istTime = istTimes[i];

            // ── Day boundary reset ──
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
                waitingForLong = false;
                waitingForShort = false;
                risk = new DayRiskState { DayStartCapital = runningCapital };
                lastDayDate = istTime.Date;
            }

            // ── Manage open position ──
            if (openTrade != null)
            {
                var partial = ManageOpenPosition(openTrade, candle, ref trailStop, ref remainingQty, ref movedToBE);
                if (partial != null)
                {
                    runningCapital += partial.Pnl;
                    if (runningCapital > peakCapital) peakCapital = runningCapital;
                    trades.Add(partial);
                }

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
                else
                {
                    continue;
                }
            }

            if (atr <= 0) continue;

            var timeOfDay = TimeOnly.FromDateTime(istTime.DateTime);
            bool pastCutoff = timeOfDay >= EmaPullbackSpeedCutoff;

            // ── Detect crossovers ──
            bool bullishCross = prevInd.EMAFast <= prevInd.EMASlow && curInd.EMAFast > curInd.EMASlow;
            bool bearishCross = prevInd.EMAFast >= prevInd.EMASlow && curInd.EMAFast < curInd.EMASlow;

            if (bearishCross) waitingForLong = false;
            if (bullishCross) waitingForShort = false;

            bool enteredThisBar = false;

            // ════════════════════════════════════════════════════════
            // PHASE 1: CROSSOVER ENTRIES (EMA_PULLBACK logic)
            // ════════════════════════════════════════════════════════

            // ── Bullish crossover ──
            if (!pastCutoff && bullishCross && !waitingForLong)
            {
                double bullRunHigh = FindPreviousBullRunHigh(candles, indicators, i);
                if (bullRunHigh > 0)
                {
                    if ((double)candle.Close < (double)curInd.EMAFast)
                    {
                        // Case A: immediate LONG
                        if (i + 1 < candles.Count && risk.CanTrade(i, "LONG"))
                        {
                            var nextCandle = candles[i + 1];
                            var entryPrice = ApplySlippage((double)nextCandle.Open, true);
                            var swingLow = FindRecentSwingLow(candles, i);
                            var slDistance = Math.Max(entryPrice - (swingLow - atr * 0.1), atr * 0.25);
                            var sl = entryPrice - slDistance;
                            var target = bullRunHigh;

                            var rrRatio = slDistance > 0 ? (target - entryPrice) / slDistance : 0;
                            if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, true) && HasVolumeConfirmation(candles, i))
                            {
                                var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                                if (effectiveRisk > 0)
                                {
                                    var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                                    if (qty > 0)
                                    {
                                        var notional = entryPrice * qty;
                                        if (notional > runningCapital * 0.20)
                                            qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                        if (qty > 0)
                                        {
                                            openTrade = new BacktestTradeResult(
                                                Guid.NewGuid().ToString(),
                                                ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                                default, 0, sl, target, qty, 0, 0, "LONG");
                                            trailStop = sl; remainingQty = qty; movedToBE = false;

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
                                            enteredThisBar = true;
                                            i++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Case B: wait for pullback
                        waitingForLong = true;
                        longSwingTarget = bullRunHigh;
                    }
                }
            }

            // ── Bearish crossover ──
            if (!enteredThisBar && !pastCutoff && bearishCross && !waitingForShort)
            {
                double bearRunLow = FindPreviousBearRunLow(candles, indicators, i);
                if (bearRunLow > 0)
                {
                    if ((double)candle.Close > (double)curInd.EMAFast)
                    {
                        // Case A: immediate SHORT
                        if (i + 1 < candles.Count && risk.CanTrade(i, "SHORT"))
                        {
                            var nextCandle = candles[i + 1];
                            var entryPrice = ApplySlippage((double)nextCandle.Open, false);
                            var swingHigh = FindRecentSwingHigh(candles, i);
                            var slDistance = Math.Max((swingHigh + atr * 0.1) - entryPrice, atr * 0.25);
                            var sl = entryPrice + slDistance;
                            var target = bearRunLow;

                            var rrRatio = slDistance > 0 ? (entryPrice - target) / slDistance : 0;
                            if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, false) && HasVolumeConfirmation(candles, i))
                            {
                                var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                                if (effectiveRisk > 0)
                                {
                                    var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                                    if (qty > 0)
                                    {
                                        var notional = entryPrice * qty;
                                        if (notional > runningCapital * 0.20)
                                            qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                        if (qty > 0)
                                        {
                                            openTrade = new BacktestTradeResult(
                                                Guid.NewGuid().ToString(),
                                                ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                                default, 0, sl, target, qty, 0, 0, "SHORT");
                                            trailStop = sl; remainingQty = qty; movedToBE = false;

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
                                            enteredThisBar = true;
                                            i++;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Case B: wait for pullback
                        waitingForShort = true;
                        shortSwingTarget = bearRunLow;
                    }
                }
            }

            if (enteredThisBar) continue;

            // ════════════════════════════════════════════════════════
            // PHASE 2: PULLBACK RETEST ENTRIES (EMA_PULLBACK Case B)
            // ════════════════════════════════════════════════════════

            if (waitingForLong && openTrade == null && !pastCutoff && i + 1 < candles.Count)
            {
                bool wickTouchesEma = (double)candle.Low <= (double)curInd.EMAFast;
                bool closesGreen = candle.IsBullish;
                if (wickTouchesEma && closesGreen && risk.CanTrade(i, "LONG"))
                {
                    var nextCandle = candles[i + 1];
                    var entryPrice = ApplySlippage((double)nextCandle.Open, true);
                    var slDistance = Math.Max(entryPrice - ((double)candle.Low - atr * 0.1), atr * 0.25);
                    var sl = entryPrice - slDistance;
                    var target = longSwingTarget;

                    var rrRatio = slDistance > 0 ? (target - entryPrice) / slDistance : 0;
                    if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, true) && HasVolumeConfirmation(candles, i))
                    {
                        var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                        if (effectiveRisk > 0)
                        {
                            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                            if (qty > 0)
                            {
                                var notional = entryPrice * qty;
                                if (notional > runningCapital * 0.20)
                                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                if (qty > 0)
                                {
                                    openTrade = new BacktestTradeResult(
                                        Guid.NewGuid().ToString(),
                                        ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                        default, 0, sl, target, qty, 0, 0, "LONG");
                                    trailStop = sl; remainingQty = qty; movedToBE = false;
                                    waitingForLong = false;

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
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            if (waitingForShort && openTrade == null && !pastCutoff && i + 1 < candles.Count)
            {
                bool wickTouchesEma = (double)candle.High >= (double)curInd.EMAFast;
                bool closesRed = candle.IsBearish;
                if (wickTouchesEma && closesRed && risk.CanTrade(i, "SHORT"))
                {
                    var nextCandle = candles[i + 1];
                    var entryPrice = ApplySlippage((double)nextCandle.Open, false);
                    var slDistance = Math.Max(((double)candle.High + atr * 0.1) - entryPrice, atr * 0.25);
                    var sl = entryPrice + slDistance;
                    var target = shortSwingTarget;

                    var rrRatio = slDistance > 0 ? (entryPrice - target) / slDistance : 0;
                    if (rrRatio >= MinRRForEntry && PassesConfluence(curInd, false) && HasVolumeConfirmation(candles, i))
                    {
                        var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                        if (effectiveRisk > 0)
                        {
                            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                            if (qty > 0)
                            {
                                var notional = entryPrice * qty;
                                if (notional > runningCapital * 0.20)
                                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                if (qty > 0)
                                {
                                    openTrade = new BacktestTradeResult(
                                        Guid.NewGuid().ToString(),
                                        ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                        default, 0, sl, target, qty, 0, 0, "SHORT");
                                    trailStop = sl; remainingQty = qty; movedToBE = false;
                                    waitingForShort = false;

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
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            // ════════════════════════════════════════════════════════
            // PHASE 3: SPEED ENTRIES (only when no crossover setup pending)
            // ════════════════════════════════════════════════════════

            if (!waitingForLong && !waitingForShort && openTrade == null && !pastCutoff && i + 1 < candles.Count)
            {
                bool uptrend = curInd.EMAFast > curInd.EMASlow;
                bool downtrend = curInd.EMAFast < curInd.EMASlow;
                double close = (double)candle.Close;
                double fastEma = (double)curInd.EMAFast;
                double candleBody = Math.Abs(close - (double)candle.Open);
                double speedThreshold = close * SpeedThresholdPct / 100.0;
                bool speedOk = candleBody >= speedThreshold;

                bool isLong = uptrend && candle.IsBullish && speedOk
                              && close >= fastEma
                              && (close - fastEma) / fastEma * 100.0 <= MaxPullbackPct;

                bool isShort = !isLong && downtrend && candle.IsBearish && speedOk
                               && close <= fastEma
                               && (fastEma - close) / fastEma * 100.0 <= MaxPullbackPct;

                if ((isLong || isShort) && PassesConfluence(curInd, isLong) && HasVolumeConfirmation(candles, i)
                    && risk.CanTrade(i, isLong ? "LONG" : "SHORT"))
                {
                    var nextCandle = candles[i + 1];
                    var entryPrice = ApplySlippage((double)nextCandle.Open, isLong);
                    var slDistance = Math.Max(atr * AtrSlMultiplier, atr * 0.25);
                    var sl = isLong ? entryPrice - slDistance : entryPrice + slDistance;
                    var target = CalcTarget(p, entryPrice, slDistance, atr, isLong);

                    var rrRatio = slDistance > 0 ? Math.Abs(target - entryPrice) / slDistance : 0;
                    if (rrRatio >= MinRRForEntry)
                    {
                        var effectiveRisk = DrawdownAdjustedRisk(p.RiskPercent, runningCapital, peakCapital);
                        if (effectiveRisk > 0)
                        {
                            var qty = CalcQuantity(runningCapital, effectiveRisk, slDistance);
                            if (qty > 0)
                            {
                                var notional = entryPrice * qty;
                                if (notional > runningCapital * 0.20)
                                    qty = (int)Math.Floor(runningCapital * 0.20 / entryPrice);
                                if (qty > 0)
                                {
                                    openTrade = new BacktestTradeResult(
                                        Guid.NewGuid().ToString(),
                                        ToIstDateTime(nextCandle.Timestamp), entryPrice,
                                        default, 0, sl, target, qty, 0, 0,
                                        isLong ? "LONG" : "SHORT");
                                    trailStop = sl; remainingQty = qty; movedToBE = false;

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
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            // Abandon pending setups past cutoff
            if (pastCutoff)
            {
                waitingForLong = false;
                waitingForShort = false;
            }
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
    // STRATEGY: SMC FVG + Order Block (Intraday)
    // ─────────────────────────────────────────────────────────
    // Sessions: Morning 09:15–11:30 IST, Afternoon 13:00–15:30 IST
    // Max 2 completed trades per day, one open position at a time
    // Bias: 15m EMA-based gate, FVG+OB on 5m, Entry on 1m rejection
    private List<BacktestTradeResult> RunSmcFvg(
        int instrumentId, DateTime from, DateTime to, StrategyParams p, double initialCapital)
    {
        // Fetch 15m, 5m, 1m candles (sequential; process as live/streaming)
        var candles15m = _candleService.GetCandlesAsync(instrumentId, 15, from, to).GetAwaiter().GetResult();
        var candles5m = _candleService.GetCandlesAsync(instrumentId, 5, from, to).GetAwaiter().GetResult();
        var candles1m = _candleService.GetCandlesAsync(instrumentId, 1, from, to).GetAwaiter().GetResult();

        if (candles15m.Count == 0 || candles5m.Count == 0 || candles1m.Count == 0)
            return [];

        var ordered15m = candles15m.OrderBy(c => c.Timestamp).ToList();
        var ordered5m = candles5m.OrderBy(c => c.Timestamp).ToList();
        var ordered1m = candles1m.OrderBy(c => c.Timestamp).ToList();

        var indicators15m = ComputeIndicators(ordered15m, p);
        var indicators5m = ComputeIndicators(ordered5m, p);
        var indicators1m = ComputeIndicators(ordered1m, p);

        var istTimes15m = ordered15m.Select(c => TimeZoneInfo.ConvertTime(c.Timestamp, Ist)).ToArray();
        var istTimes5m = ordered5m.Select(c => TimeZoneInfo.ConvertTime(c.Timestamp, Ist)).ToArray();
        var istTimes1m = ordered1m.Select(c => TimeZoneInfo.ConvertTime(c.Timestamp, Ist)).ToArray();

        var trades = new List<BacktestTradeResult>();
        var dayStates = new Dictionary<DateTime, DaySmcState>();
        var sessionStates = new Dictionary<(DateTime date, string session), SessionSmcState>();
        double runningCapital = initialCapital;

        // Session boundaries (IST)
        var morningStart = new TimeSpan(9, 15, 0);
        var morningEnd = new TimeSpan(11, 30, 0);
        var afternoonStart = new TimeSpan(13, 0, 0);
        var afternoonEnd = new TimeSpan(15, 30, 0);
        var entryWindowStart = new TimeSpan(9, 20, 0);
        var entryWindowEnd = new TimeSpan(10, 30, 0);
        var noEntryFinalMinute = new TimeSpan(15, 29, 0);

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;
        bool movedToBreakeven = false;
        FvgState? activeFvg = null;

        // ─── Main Sequential Loop (1m Candles) ───
        for (int idx1m = 0; idx1m < ordered1m.Count; idx1m++)
        {
            var candle1m = ordered1m[idx1m];
            var time1m = istTimes1m[idx1m];
            var timeOfDay = time1m.TimeOfDay;

            // Session determination
            bool inMorning = timeOfDay >= morningStart && timeOfDay < morningEnd;
            bool inAfternoon = timeOfDay >= afternoonStart && timeOfDay < afternoonEnd;
            string? currentSession = inMorning ? "MORNING" : inAfternoon ? "AFTERNOON" : null;

            // Close position at session boundaries
            if (!inMorning && !inAfternoon && openTrade != null)
            {
                var sessionClosed = CloseTrade(openTrade, ToIstDateTime(candle1m.Timestamp), (double)candle1m.Close);
                if (sessionClosed != null)
                {
                    trades.Add(sessionClosed);
                    runningCapital += sessionClosed.Pnl;
                }
                openTrade = null;
                movedToBreakeven = false;
                activeFvg = null;
                continue;
            }

            // Skip if outside trading hours
            if (currentSession == null)
                continue;

            // Initialize day state
            var dayKey = time1m.Date;
            if (!dayStates.TryGetValue(dayKey, out var dayState))
                dayStates[dayKey] = dayState = new DaySmcState();

            // Initialize session state
            var sessionKey = (dayKey, currentSession);
            if (!sessionStates.TryGetValue(sessionKey, out var sessState))
            {
                sessState = new SessionSmcState();
                sessionStates[sessionKey] = sessState;

                // Determine session bias on first candle of session using 15m EMA
                var idx15mAtSessionStart = FindClosest5MinuteCandleIndex(ordered5m, istTimes5m, time1m);
                if (idx15mAtSessionStart >= 0)
                {
                    var ind15m = indicators15m[Math.Min(idx15mAtSessionStart, indicators15m.Length - 1)];
                    // Simple bias: if close > 9-EMA, bullish; else bearish
                    sessState.IsBullishBias = ind15m.EMAFast > 0 && ordered15m[idx15mAtSessionStart].Close > ind15m.EMAFast;
                }
            }

            // If already 2 trades completed this day, no new entries
            if (dayState.CompletedTrades >= 2)
            {
                // Still manage open position
                if (openTrade != null)
                {
                    var fullExit = CheckExit(openTrade, candle1m, (double)indicators1m[idx1m].ATR, p, ref trailStop);
                    if (fullExit != null)
                    {
                        trades.Add(fullExit);
                        runningCapital += fullExit.Pnl;
                        dayState.CompletedTrades++;
                        openTrade = null;
                        movedToBreakeven = false;
                        activeFvg = null;
                    }
                }
                continue;
            }

            // Manage existing open position
            if (openTrade != null)
            {
                // Partial exit management (50% at TP1, move SL to breakeven)
                int remainingQty = 1; // For SMC, we track as whole position, not fractional
                var partialExit = ManageOpenPosition(openTrade, candle1m, ref trailStop, ref remainingQty, ref movedToBreakeven);
                if (partialExit != null)
                {
                    trades.Add(partialExit);
                    movedToBreakeven = true;
                }

                // Check for full exit (SL or target hit)
                var fullExit = CheckExit(openTrade, candle1m, (double)indicators1m[idx1m].ATR, p, ref trailStop);
                if (fullExit != null)
                {
                    trades.Add(fullExit);
                    runningCapital += fullExit.Pnl;
                    dayState.CompletedTrades++;
                    openTrade = null;
                    movedToBreakeven = false;
                    activeFvg = null;
                }
                continue;
            }

            // Entry logic (only in trade windows, no position open)
            bool inEntryWindow = (timeOfDay >= entryWindowStart && timeOfDay <= entryWindowEnd) ||
                                 (currentSession == "AFTERNOON" && timeOfDay < noEntryFinalMinute);

            if (!inEntryWindow || openTrade != null)
                continue;

            // Find or create FVG target on 5m (simplified: just check if there's been a recent gap)
            // For now, check if 1m candle closes inside a potential FVG zone and next candle rejects
            if (activeFvg == null && idx1m > 0)
            {
                // Look back 3 candles on 5m to detect a potential FVG
                var idx5m = FindClosestIndexInList(ordered5m, istTimes5m, time1m);
                if (idx5m >= 2)
                {
                    var c5m_i2 = ordered5m[idx5m - 2];
                    var c5m_i1 = ordered5m[idx5m - 1];
                    var c5m_i0 = ordered5m[idx5m];

                    // Check for bullish FVG (i-2 high < i high)
                    bool hasBullishFvg = c5m_i2.High < c5m_i0.Low && sessState.IsBullishBias;
                    // Check for bearish FVG (i-2 low > i low)
                    bool hasBearishFvg = c5m_i2.Low > c5m_i0.High && !sessState.IsBullishBias;

                    if (hasBullishFvg)
                    {
                        activeFvg = new FvgState
                        {
                            IsBullish = true,
                            ZoneLow = c5m_i2.High,
                            ZoneHigh = c5m_i0.Low,
                            ObHigh = c5m_i1.High,
                            ObLow = c5m_i1.Low,
                            CreatedAt = c5m_i0.Timestamp
                        };
                    }
                    else if (hasBearishFvg)
                    {
                        activeFvg = new FvgState
                        {
                            IsBullish = false,
                            ZoneLow = c5m_i0.High,
                            ZoneHigh = c5m_i2.Low,
                            ObHigh = c5m_i1.High,
                            ObLow = c5m_i1.Low,
                            CreatedAt = c5m_i0.Timestamp
                        };
                    }
                }
            }

            // Entry trigger: 1m candle closes inside FVG, then next candle rejects outside
            if (activeFvg != null && idx1m > 0)
            {
                var prevCandle1m = ordered1m[idx1m - 1];

                bool prevClosedInsideFvg = prevCandle1m.Close >= (decimal)activeFvg.ZoneLow && 
                                           prevCandle1m.Close <= (decimal)activeFvg.ZoneHigh;
                bool currRejected = activeFvg.IsBullish
                    ? candle1m.Close > (decimal)activeFvg.ZoneHigh
                    : candle1m.Close < (decimal)activeFvg.ZoneLow;

                if (prevClosedInsideFvg && currRejected)
                {
                    // Entry triggered!
                    double entryPrice = (double)candle1m.Close;
                    double slPrice = activeFvg.IsBullish
                        ? (double)activeFvg.ObLow - DeriveMinPriceStep(ordered1m, idx1m)
                        : (double)activeFvg.ObHigh + DeriveMinPriceStep(ordered1m, idx1m);
                    
                    double riskDistance = Math.Abs(entryPrice - slPrice);
                    double tp1Price = activeFvg.IsBullish
                        ? entryPrice + 2 * riskDistance
                        : entryPrice - 2 * riskDistance;
                    
                    double tp2Price = activeFvg.IsBullish
                        ? sessState.SessionHigh > entryPrice ? sessState.SessionHigh : entryPrice + 3 * riskDistance
                        : sessState.SessionLow > 0 && sessState.SessionLow < entryPrice ? sessState.SessionLow : entryPrice - 3 * riskDistance;

                    var qty = CalcQuantity(runningCapital, p.RiskPercent, riskDistance);

                    openTrade = new BacktestTradeResult(
                        Id: Guid.NewGuid().ToString(),
                        EntryTime: ToIstDateTime(candle1m.Timestamp),
                        EntryPrice: Math.Round(entryPrice, 2),
                        ExitTime: null,
                        ExitPrice: 0,
                        StopLoss: Math.Round(slPrice, 2),
                        Target: Math.Round(tp2Price, 2),
                        Quantity: qty,
                        TradeType: activeFvg.IsBullish ? "LONG" : "SHORT",
                        Pnl: 0,
                        PnlPercent: 0
                        //RiskReward: riskDistance > 0 ? Math.Round((Math.Abs(tp2Price - entryPrice)) / riskDistance, 2) : 0
                    );

                    movedToBreakeven = false;
                    activeFvg = null;
                }
            }

            // Update session highs/lows for dynamic TP2
            if (sessState.SessionHigh == 0 || candle1m.High > (decimal)sessState.SessionHigh)
                sessState.SessionHigh = (double)candle1m.High;
            if (sessState.SessionLow == 0 || candle1m.Low < (decimal)sessState.SessionLow)
                sessState.SessionLow = (double)candle1m.Low;
        }

        return trades;
    }

    // Helper: Find closest 5m candle index to 1m timestamp
    private static int FindClosestIndexInList(List<Candle> candles, DateTimeOffset[] times, DateTimeOffset targetTime)
    {
        for (int i = times.Length - 1; i >= 0; i--)
            if (times[i] <= targetTime)
                return i;
        return -1;
    }

    // Helper: Derive minimum price step from observed candle data
    private static double DeriveMinPriceStep(List<Candle> candles, int upToIdx)
    {
        decimal minDiff = decimal.MaxValue;
        for (int i = 1; i <= Math.Min(upToIdx, 20); i++)
        {
            var diff = Math.Abs(candles[i].Close - candles[i - 1].Close);
            if (diff > 0 && diff < minDiff)
                minDiff = diff;
        }
        return (double)(minDiff == decimal.MaxValue ? 0.05m : minDiff);
    }

    // Helper: Find 5m candle closest to a given 1m timestamp
    private static int Find15minAtOrBefore(List<Candle> candles15m, DateTimeOffset[] times15m, DateTimeOffset targetTime)
    {
        for (int i = times15m.Length - 1; i >= 0; i--)
            if (times15m[i] <= targetTime)
                return i;
        return -1;
    }

    // Helper: Find the index of the closest matching candle in 5m list
    private static int FindClosest5MinuteCandleIndex(List<Candle> candles5m, DateTimeOffset[] times5m, DateTimeOffset targetTime)
    {
        for (int i = times5m.Length - 1; i >= 0; i--)
            if (times5m[i] <= targetTime)
                return i;
        return -1;
    }

    // ─── State Classes ───
    private sealed class DaySmcState
    {
        public int CompletedTrades;
    }

    private sealed class SessionSmcState
    {
        public bool IsBullishBias;
        public double SessionHigh;
        public double SessionLow;
    }

    private sealed class FvgState
    {
        public bool IsBullish;
        public decimal ZoneLow;
        public decimal ZoneHigh;
        public decimal ObHigh;
        public decimal ObLow;
        public DateTimeOffset CreatedAt;
    }


    // ─────────────────────────────────────────────────────────
    // SHARED HELPERS
    // ─────────────────────────────────────────────────────────


    /// <summary>
    /// Walks back from <paramref name="crossoverIdx"/> through the bearish EMA phase that just ended
    /// (bars where FastEMA &lt; SlowEMA) and returns the highest High in that run.
    /// Used as the LONG target after a bullish crossover.
    /// </summary>
    private static double FindPreviousBullRunHigh(List<Candle> candles, IndicatorValues[] indicators, int crossoverIdx)
    {
        double high = 0;
        for (int k = crossoverIdx - 1; k >= 0; k--)
        {
            // Walk through the bearish phase (FastEMA < SlowEMA); stop when we exit back into the prior bullish phase
            if (indicators[k].EMAFast >= indicators[k].EMASlow) break;
            var h = (double)candles[k].High;
            if (h > high) high = h;
        }
        return high;
    }

    /// <summary>
    /// Walks back from <paramref name="crossoverIdx"/> through the bullish EMA phase that just ended
    /// (bars where FastEMA &gt; SlowEMA) and returns the lowest Low in that run.
    /// Used as the SHORT target after a bearish crossover.
    /// </summary>
    private static double FindPreviousBearRunLow(List<Candle> candles, IndicatorValues[] indicators, int crossoverIdx)
    {
        double low = double.MaxValue;
        for (int k = crossoverIdx - 1; k >= 0; k--)
        {
            // Walk through the bullish phase (FastEMA > SlowEMA); stop when we exit back into the prior bearish phase
            if (indicators[k].EMAFast <= indicators[k].EMASlow) break;
            var l = (double)candles[k].Low;
            if (l < low) low = l;
        }
        return low == double.MaxValue ? 0 : low;
    }

    private static double FindRecentSwingLow(List<Candle> candles, int fromIdx, int lookback = 10)
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

    private static double FindRecentSwingHigh(List<Candle> candles, int fromIdx, int lookback = 10)
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
        double finalCapital = initialCapital + totalPnl;
        double totalReturn = (totalPnl / initialCapital) * 100.0;

        double grossProfit = trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        double grossLoss = Math.Abs(trades.Where(t => t.Pnl < 0).Sum(t => t.Pnl));
        double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 9999.0 : 0;
        double avgWinPnl = winningTrades > 0 ? grossProfit / winningTrades : 0;
        double avgLossPnl = losingTrades > 0 ? -(grossLoss / losingTrades) : 0;

        // Quantity-weighted avgRR: larger positions count proportionally more
        long totalQty = trades.Sum(t => (long)t.Quantity);
        double avgRR = totalQty > 0
            ? trades.Sum(t =>
            {
                var risk = Math.Abs(t.EntryPrice - t.StopLoss);
                if (risk <= 0) return 0.0;
                bool isLong = t.TradeType == "LONG";
                var signedMove = isLong ? t.ExitPrice - t.EntryPrice : t.EntryPrice - t.ExitPrice;
                return (signedMove / risk) * t.Quantity;
            }) / totalQty
            : 0;

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
            EquityCurve: equityCurve,
            InitialCapital: Math.Round(initialCapital, 2),
            FinalCapital: Math.Round(finalCapital, 2),
            AvgWinPnl: Math.Round(avgWinPnl, 2),
            AvgLossPnl: Math.Round(avgLossPnl, 2));
    }

    private static BacktestMetrics EmptyMetrics(double initialCapital, DateTime timestamp) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0, [new EquityPoint(timestamp, initialCapital)],
            Math.Round(initialCapital, 2), Math.Round(initialCapital, 2), 0, 0);
}