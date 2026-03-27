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

    /// <summary>Slippage per side as a fraction of price (0.0005 = 0.05%).</summary>
    private const double SlippageFraction = 0.0005;

    /// <summary>Brokerage + STT + stamp as a fraction of turnover per side.</summary>
    private const double CommissionFraction = 0.0003;

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

    public async Task<BacktestResponse> RunAsync(BacktestRunRequest request, double initialCapital = 100000)
    {
        var instrument = await _instrumentService.GetBySymbolAsync(request.Symbol);
        if (instrument == null)
            throw new KeyNotFoundException($"Symbol not found");

        var candles = await _candleService.GetCandlesAsync(
            instrument.Id,
            request.Strategy.Params.Timeframe,
            request.From,
            request.To);

        if (candles.Count == 0)
            return new BacktestResponse([], EmptyMetrics(initialCapital, request.From));

        var orderedCandles = candles.OrderBy(c => c.Timestamp).ToList();
        var indicators = ComputeIndicators(orderedCandles, request.Strategy.Params);

        // [Fix #14] Pre-compute IST timestamps once for all candles
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

    private IndicatorValues[] ComputeIndicators(List<Candle> candles, StrategyParams p)
    {
        var fastEma = p.FastEMA ?? 9;
        var slowEma = p.SlowEMA ?? 21;

        var engine = new IndicatorEngine(
            emaFastPeriod: fastEma,
            emaSlowPeriod: slowEma,
            rsiPeriod: 14,
            macdFast: 12,
            macdSlow: 26,
            macdSignal: 9,
            adxPeriod: 14,
            atrPeriod: 14,
            bollingerPeriod: 20,
            bollingerStdDev: 2.0m);

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
        var timeframe = p.Timeframe;

        // Number of opening-range candles: first 5 minutes worth
        int orbCandleCount = timeframe switch
        {
            1 => 5,
            _ => 1
        };

        // [Fix #14] Group candles by trading day using pre-computed IST times
        var dayGroups = candles
            .Select((c, i) => (Candle: c, Index: i, Indicator: indicators[i], IstTime: istTimes[i]))
            .GroupBy(x => x.IstTime.Date)
            .OrderBy(g => g.Key);

        // [Fix #1] Track running capital for compounding position size
        double runningCapital = initialCapital;

        foreach (var dayGroup in dayGroups)
        {
            var dayCandles = dayGroup.OrderBy(x => x.Candle.Timestamp).ToList();
            if (dayCandles.Count <= orbCandleCount)
                continue;

            // Opening range
            var orbCandles = dayCandles.Take(orbCandleCount).ToList();
            var openingHigh = (double)orbCandles.Max(x => x.Candle.High);
            var openingLow = (double)orbCandles.Min(x => x.Candle.Low);

            BacktestTradeResult? openTrade = null;
            double trailStop = 0;
            // [Fix #7] Cooldown: skip re-entry for N candles after a loss within the same day
            int cooldownUntilJ = -1;

            for (int j = orbCandleCount; j < dayCandles.Count; j++)
            {
                var (candle, globalIdx, ind, istTime) = dayCandles[j];

                // Force close at end of day
                if (j == dayCandles.Count - 1 && openTrade != null)
                {
                    var closed = CloseTradeWithCosts(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close);
                    runningCapital += closed.Pnl;
                    trades.Add(closed);
                    openTrade = null;
                    break;
                }

                // Check exits for open trade
                if (openTrade != null)
                {
                    var exitResult = CheckExit(openTrade, candle, (double)ind.ATR, p, ref trailStop);
                    if (exitResult != null)
                    {
                        var closed = ApplyCosts(exitResult);
                        runningCapital += closed.Pnl;
                        trades.Add(closed);
                        // [Fix #7] After a loss, cooldown for 3 candles before re-entry
                        if (closed.Pnl < 0)
                            cooldownUntilJ = j + 3;
                        openTrade = null;
                    }
                    continue;
                }

                // No new trades after 14:00 IST
                if (TimeOnly.FromDateTime(istTime.DateTime) >= NoCutoff)
                    continue;

                // [Fix #7] Respect cooldown
                if (j <= cooldownUntilJ)
                    continue;

                // Entry signals
                var atr = (double)ind.ATR;
                if (atr <= 0) continue;

                if ((double)candle.Close > openingHigh)
                {
                    // LONG breakout — enter at next candle open
                    if (j + 1 < dayCandles.Count)
                    {
                        var nextCandle = dayCandles[j + 1].Candle;
                        var rawEntry = (double)nextCandle.Open;
                        var entryPrice = ApplySlippage(rawEntry, true); // [Fix #13] slippage on entry
                        var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, true);
                        var sl = entryPrice - slDistance;
                        var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                        var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                        openTrade = new BacktestTradeResult(
                            Guid.NewGuid().ToString(),
                            nextCandle.Timestamp.UtcDateTime, entryPrice,
                            default, 0, sl, target, qty, 0, 0, "LONG");
                        trailStop = sl;

                        // [Fix #3] Check if the entry candle itself breaches SL/target
                        var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
                        if (entryBarExit != null)
                        {
                            var closed = ApplyCosts(entryBarExit);
                            runningCapital += closed.Pnl;
                            trades.Add(closed);
                            if (closed.Pnl < 0) cooldownUntilJ = j + 3;
                            openTrade = null;
                        }
                        j++; // skip the entry candle
                    }
                }
                else if ((double)candle.Close < openingLow)
                {
                    // SHORT breakout
                    if (j + 1 < dayCandles.Count)
                    {
                        var nextCandle = dayCandles[j + 1].Candle;
                        var rawEntry = (double)nextCandle.Open;
                        var entryPrice = ApplySlippage(rawEntry, false); // [Fix #13]
                        var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, false);
                        var sl = entryPrice + slDistance;
                        var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                        var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                        openTrade = new BacktestTradeResult(
                            Guid.NewGuid().ToString(),
                            nextCandle.Timestamp.UtcDateTime, entryPrice,
                            default, 0, sl, target, qty, 0, 0, "SHORT");
                        trailStop = sl;

                        // [Fix #3] Check if entry candle breaches SL/target
                        var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
                        if (entryBarExit != null)
                        {
                            var closed = ApplyCosts(entryBarExit);
                            runningCapital += closed.Pnl;
                            trades.Add(closed);
                            if (closed.Pnl < 0) cooldownUntilJ = j + 3;
                            openTrade = null;
                        }
                        j++;
                    }
                }
            }

            // Force close at day end if still open
            if (openTrade != null)
            {
                var lastCandle = dayCandles[^1].Candle;
                var closed = CloseTradeWithCosts(openTrade, lastCandle.Timestamp.UtcDateTime, (double)lastCandle.Close);
                runningCapital += closed.Pnl;
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

        // [Fix #1] Track running capital
        double runningCapital = initialCapital;

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;
        int cooldownUntilIndex = -1;
        string? cooldownDirection = null;

        for (int i = 0; i < candles.Count - 1; i++)
        {
            var candle = candles[i];
            var ind = indicators[i];
            var nextInd = indicators[i + 1];
            var nextCandle = candles[i + 1];

            // Check exits first
            if (openTrade != null)
            {
                var exitResult = CheckExit(openTrade, candle, (double)ind.ATR, p, ref trailStop);
                if (exitResult != null)
                {
                    var closed = ApplyCosts(exitResult);
                    runningCapital += closed.Pnl;
                    // [Fix #15] Cooling-off: skip 5 candles (was 2) in same direction after a loss
                    if (closed.Pnl < 0)
                    {
                        cooldownDirection = closed.TradeType;
                        cooldownUntilIndex = i + 5;
                    }
                    trades.Add(closed);
                    openTrade = null;
                }
                // [Fix #4] Don't continue here — allow force-close check below
                else
                {
                    continue;
                }
            }

            // [Fix #4] Force close on second-to-last candle (now reachable)
            if (i == candles.Count - 2 && openTrade != null)
            {
                var closed = CloseTradeWithCosts(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close);
                runningCapital += closed.Pnl;
                trades.Add(closed);
                openTrade = null;
                continue;
            }

            // Skip entry logic if we still have an open trade (exit didn't trigger above)
            if (openTrade != null) continue;

            var atr = (double)ind.ATR;
            if (atr <= 0) continue;

            var rsiNow = (double)ind.RSI;
            var rsiNext = (double)nextInd.RSI;

            // [Fix #5] Use indicators at i+2 for VWAP confirmation, not stale ones from i
            // LONG: RSI crosses back up from oversold
            if (rsiNow < oversold && rsiNext > oversold)
            {
                if (cooldownDirection == "LONG" && i <= cooldownUntilIndex)
                    continue;

                if (i + 2 < candles.Count)
                {
                    var entryCandle = candles[i + 2];
                    var entryInd = indicators[i + 2]; // [Fix #5] Use entry-bar indicators

                    // Confirm price is above VWAP at entry time, not signal time
                    if ((double)entryCandle.Open <= (double)entryInd.VWAP && entryInd.VWAP > 0)
                    {
                        i += 2; // skip ahead but don't enter
                        continue;
                    }

                    var rawEntry = (double)entryCandle.Open;
                    var entryPrice = ApplySlippage(rawEntry, true); // [Fix #13]
                    var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, true);
                    var sl = entryPrice - slDistance;
                    var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                    var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                    openTrade = new BacktestTradeResult(
                        Guid.NewGuid().ToString(),
                        entryCandle.Timestamp.UtcDateTime, entryPrice,
                        default, 0, sl, target, qty, 0, 0, "LONG");
                    trailStop = sl;

                    // [Fix #3] Check if entry candle breaches SL/target
                    var entryBarExit = CheckExit(openTrade, entryCandle, atr, p, ref trailStop);
                    if (entryBarExit != null)
                    {
                        var closed = ApplyCosts(entryBarExit);
                        runningCapital += closed.Pnl;
                        trades.Add(closed);
                        if (closed.Pnl < 0) { cooldownDirection = "LONG"; cooldownUntilIndex = i + 5; }
                        openTrade = null;
                    }
                    i += 2;
                }
            }
            // SHORT: RSI crosses back down from overbought
            else if (rsiNow > overbought && rsiNext < overbought)
            {
                if (cooldownDirection == "SHORT" && i <= cooldownUntilIndex)
                    continue;

                if (i + 2 < candles.Count)
                {
                    var entryCandle = candles[i + 2];
                    var entryInd = indicators[i + 2]; // [Fix #5]

                    // Confirm price is below VWAP at entry time
                    if ((double)entryCandle.Open >= (double)entryInd.VWAP && entryInd.VWAP > 0)
                    {
                        i += 2;
                        continue;
                    }

                    var rawEntry = (double)entryCandle.Open;
                    var entryPrice = ApplySlippage(rawEntry, false); // [Fix #13]
                    var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, false);
                    var sl = entryPrice + slDistance;
                    var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                    var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                    openTrade = new BacktestTradeResult(
                        Guid.NewGuid().ToString(),
                        entryCandle.Timestamp.UtcDateTime, entryPrice,
                        default, 0, sl, target, qty, 0, 0, "SHORT");
                    trailStop = sl;

                    // [Fix #3] Check if entry candle breaches SL/target
                    var entryBarExit = CheckExit(openTrade, entryCandle, atr, p, ref trailStop);
                    if (entryBarExit != null)
                    {
                        var closed = ApplyCosts(entryBarExit);
                        runningCapital += closed.Pnl;
                        trades.Add(closed);
                        if (closed.Pnl < 0) { cooldownDirection = "SHORT"; cooldownUntilIndex = i + 5; }
                        openTrade = null;
                    }
                    i += 2;
                }
            }
        }

        // Force close if still open
        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            var closed = CloseTradeWithCosts(openTrade, last.Timestamp.UtcDateTime, (double)last.Close);
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

        // [Fix #1] Track running capital
        double runningCapital = initialCapital;

        BacktestTradeResult? openTrade = null;
        double trailStop = 0;

        for (int i = 1; i < candles.Count; i++)
        {
            var prevInd = indicators[i - 1];
            var curInd = indicators[i];
            var candle = candles[i];
            var atr = (double)curInd.ATR;

            bool fastAboveSlow = curInd.EMAFast > curInd.EMASlow;
            bool prevFastAboveSlow = prevInd.EMAFast > prevInd.EMASlow;
            bool bullishCross = !prevFastAboveSlow && fastAboveSlow;
            bool bearishCross = prevFastAboveSlow && !fastAboveSlow;

            // Check for opposite crossover exit (EMA strategy-specific exit)
            if (openTrade != null)
            {
                bool oppositeSignal = (openTrade.TradeType == "LONG" && bearishCross)
                                   || (openTrade.TradeType == "SHORT" && bullishCross);

                if (oppositeSignal)
                {
                    var closed = CloseTradeWithCosts(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close);
                    runningCapital += closed.Pnl;
                    trades.Add(closed);
                    openTrade = null;
                    // Fall through to check for new entry on this candle
                }
                else
                {
                    var exitResult = CheckExit(openTrade, candle, atr, p, ref trailStop);
                    if (exitResult != null)
                    {
                        var closed = ApplyCosts(exitResult);
                        runningCapital += closed.Pnl;
                        trades.Add(closed);
                        openTrade = null;
                    }
                    continue;
                }
            }

            if (openTrade != null) continue;
            if (atr <= 0) continue;
            if ((double)curInd.ADX <= 20) continue;

            // Bullish crossover
            if (bullishCross && i + 1 < candles.Count)
            {
                var nextCandle = candles[i + 1];
                var rawEntry = (double)nextCandle.Open;
                var entryPrice = ApplySlippage(rawEntry, true); // [Fix #13]
                var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, true);
                var sl = entryPrice - slDistance;
                var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                openTrade = new BacktestTradeResult(
                    Guid.NewGuid().ToString(),
                    nextCandle.Timestamp.UtcDateTime, entryPrice,
                    default, 0, sl, target, qty, 0, 0, "LONG");
                trailStop = sl;

                // [Fix #3] Check entry candle for immediate exit
                var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
                if (entryBarExit != null)
                {
                    var closed = ApplyCosts(entryBarExit);
                    runningCapital += closed.Pnl;
                    trades.Add(closed);
                    openTrade = null;
                }
                i++;
            }
            // Bearish crossover
            else if (bearishCross && i + 1 < candles.Count)
            {
                var nextCandle = candles[i + 1];
                var rawEntry = (double)nextCandle.Open;
                var entryPrice = ApplySlippage(rawEntry, false); // [Fix #13]
                var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High, false);
                var sl = entryPrice + slDistance;
                var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                var qty = CalcQuantity(runningCapital, p.RiskPercent, slDistance); // [Fix #1]

                openTrade = new BacktestTradeResult(
                    Guid.NewGuid().ToString(),
                    nextCandle.Timestamp.UtcDateTime, entryPrice,
                    default, 0, sl, target, qty, 0, 0, "SHORT");
                trailStop = sl;

                // [Fix #3] Check entry candle for immediate exit
                var entryBarExit = CheckExit(openTrade, nextCandle, atr, p, ref trailStop);
                if (entryBarExit != null)
                {
                    var closed = ApplyCosts(entryBarExit);
                    runningCapital += closed.Pnl;
                    trades.Add(closed);
                    openTrade = null;
                }
                i++;
            }
        }

        // Force close if still open
        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            var closed = CloseTradeWithCosts(openTrade, last.Timestamp.UtcDateTime, (double)last.Close);
            runningCapital += closed.Pnl;
            trades.Add(closed);
        }

        return trades;
    }

    // ─────────────────────────────────────────────────────────
    // SHARED HELPERS
    // ─────────────────────────────────────────────────────────

    /// <summary>
    /// [Fix #13] Apply slippage to entry price.
    /// LONG entries slip up (worse fill), SHORT entries slip down.
    /// </summary>
    private static double ApplySlippage(double price, bool isLong)
    {
        return isLong
            ? price * (1 + SlippageFraction)
            : price * (1 - SlippageFraction);
    }

    /// <summary>
    /// [Fix #12] Direction-aware stop-loss distance.
    /// LONG uses entry-to-low, SHORT uses high-to-entry for CANDLE mode.
    /// </summary>
    private static double CalcStopLossDistance(
        StrategyParams p, double entryPrice, double atr, double candleLow, double candleHigh, bool isLong)
    {
        var distance = p.StopLossType.ToUpperInvariant() switch
        {
            "ATR" => atr * 1.5,
            "FIXED_PERCENT" => entryPrice * ((p.SlPercent ?? 1.0) / 100.0),
            "CANDLE" => isLong
                ? Math.Max(entryPrice - candleLow, atr * 0.5) // [Fix #12] LONG: distance to candle low
                : Math.Max(candleHigh - entryPrice, atr * 0.5), // SHORT: distance from candle high
            _ => atr * 1.5
        };

        // Guard: ensure stop distance is at least 0.25 × ATR to avoid absurdly tight stops
        return Math.Max(distance, atr * 0.25);
    }

    private static double CalcTarget(StrategyParams p, double entryPrice, double slDistance, double atr, bool isLong)
    {
        if (p.TargetType.Equals("RR_RATIO", StringComparison.OrdinalIgnoreCase))
        {
            var targetDistance = slDistance * (p.RrRatio ?? 2.0);
            return isLong ? entryPrice + targetDistance : entryPrice - targetDistance;
        }

        // TRAILING — initial target set far away; trailing stop handles the exit
        var farDistance = slDistance * 10;
        return isLong ? entryPrice + farDistance : entryPrice - farDistance;
    }

    private static int CalcQuantity(double capital, double riskPercent, double slDistance)
    {
        if (slDistance <= 0) return 1;
        var riskAmount = capital * (riskPercent / 100.0);
        var qty = (int)Math.Floor(riskAmount / slDistance);
        return Math.Max(qty, 1);
    }

    /// <summary>
    /// [Fix #2 & #6] Exit check with same-bar ambiguity resolution.
    /// When both SL and target are hit on the same bar, uses Open proximity to decide.
    /// [Fix #6] In trailing mode, updates stop using previous bar's extreme (caller provides
    /// current candle — the trailStop was set from prior bars, only breach is checked here
    /// after a conservative update).
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
            // [Fix #6] For trailing mode, the trailStop should tighten based on
            // the previous bar's extreme. Since we process bar-by-bar, we update
            // trailStop conservatively: use (high - 1×ATR) for LONG, (low + 1×ATR) for SHORT.
            // The key fix: check breach BEFORE tightening with current bar data.
            if (isLong)
            {
                if (low <= trailStop)
                    return CloseTrade(trade, candle.Timestamp.UtcDateTime, trailStop);
                // Only tighten after confirming no breach on this bar
                trailStop = Math.Max(trailStop, high - atr);
            }
            else
            {
                if (high >= trailStop)
                    return CloseTrade(trade, candle.Timestamp.UtcDateTime, trailStop);
                trailStop = Math.Min(trailStop, low + atr);
            }
            return null;
        }

        // [Fix #2] RR_RATIO mode: detect if both SL and target are hit on same bar
        bool slHit, tgtHit;
        if (isLong)
        {
            slHit = low <= trade.StopLoss;
            tgtHit = high >= trade.Target;
        }
        else
        {
            slHit = high >= trade.StopLoss;
            tgtHit = low <= trade.Target;
        }

        if (slHit && tgtHit)
        {
            // Use Open to determine which was likely hit first
            var distToSl = Math.Abs(open - trade.StopLoss);
            var distToTgt = Math.Abs(open - trade.Target);

            if (isLong)
            {
                // If open is at or below SL, stop hit first
                bool stopFirst = open <= trade.StopLoss || distToSl < distToTgt;
                return stopFirst
                    ? CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.StopLoss)
                    : CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.Target);
            }
            else
            {
                bool stopFirst = open >= trade.StopLoss || distToSl < distToTgt;
                return stopFirst
                    ? CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.StopLoss)
                    : CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.Target);
            }
        }

        if (slHit)
            return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.StopLoss);
        if (tgtHit)
            return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.Target);

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

    /// <summary>
    /// [Fix #13] Apply commission costs to a closed trade.
    /// Deducts round-trip commission from PnL.
    /// </summary>
    private static BacktestTradeResult ApplyCosts(BacktestTradeResult closed)
    {
        var turnover = (closed.EntryPrice + closed.ExitPrice) * closed.Quantity;
        var totalCommission = turnover * CommissionFraction;
        var adjustedPnl = closed.Pnl - totalCommission;
        var adjustedPct = closed.EntryPrice != 0 && closed.Quantity > 0
            ? (adjustedPnl / (closed.EntryPrice * closed.Quantity)) * 100.0
            : 0;

        return closed with
        {
            Pnl = Math.Round(adjustedPnl, 2),
            PnlPercent = Math.Round(adjustedPct, 2)
        };
    }

    /// <summary>
    /// Close a trade and apply costs in one step (for force-close scenarios).
    /// </summary>
    private static BacktestTradeResult CloseTradeWithCosts(BacktestTradeResult open, DateTime exitTime, double exitPrice)
    {
        var closed = CloseTrade(open, exitTime, exitPrice);
        return ApplyCosts(closed);
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
        int winningTrades = trades.Count(t => t.Pnl > 0); // [Fix #10] Strict > 0
        int losingTrades = trades.Count(t => t.Pnl < 0);
        double winRate = (double)winningTrades / totalTrades;
        double totalPnl = trades.Sum(t => t.Pnl);
        double totalReturn = (totalPnl / initialCapital) * 100.0;

        double grossProfit = trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        double grossLoss = Math.Abs(trades.Where(t => t.Pnl < 0).Sum(t => t.Pnl));
        // [Fix #9] ProfitFactor: when no losses, use a large number instead of 0
        double profitFactor = grossLoss > 0
            ? grossProfit / grossLoss
            : grossProfit > 0 ? 9999.0 : 0;

        // [Fix #11] Avg RR per trade — signed: positive for wins, negative for losses
        double avgRR = trades.Average(t =>
        {
            var risk = Math.Abs(t.EntryPrice - t.StopLoss);
            if (risk <= 0) return 0;
            bool isLong = t.TradeType == "LONG";
            var signedMove = isLong
                ? t.ExitPrice - t.EntryPrice
                : t.EntryPrice - t.ExitPrice;
            return signedMove / risk;
        });

        // [Fix #8] Equity curve with intra-trade mark-to-market points
        var equityCurve = new List<EquityPoint>();
        double equity = initialCapital;
        double peak = initialCapital;
        double maxDrawdown = 0;

        // Initial point
        if (candles.Count > 0)
            equityCurve.Add(new EquityPoint(candles[0].Timestamp.UtcDateTime, initialCapital));

        // Build a lookup of trade entry/exit times for mark-to-market
        var orderedTrades = trades.OrderBy(t => t.EntryTime).ToList();
        int tradeIdx = 0;
        BacktestTradeResult? activeTrade = null;

        foreach (var candle in candles)
        {
            var candleTime = candle.Timestamp.UtcDateTime;

            // Check if a new trade opened on this candle
            while (tradeIdx < orderedTrades.Count && orderedTrades[tradeIdx].EntryTime <= candleTime)
            {
                var t = orderedTrades[tradeIdx];
                if (t.ExitTime <= candleTime)
                {
                    // Trade opened and closed before/on this candle
                    equity += t.Pnl;
                    tradeIdx++;
                }
                else
                {
                    // Trade is open during this candle
                    activeTrade = t;
                    tradeIdx++;
                    break;
                }
            }

            // Check if active trade closed on this candle
            if (activeTrade != null && activeTrade.ExitTime <= candleTime)
            {
                equity += activeTrade.Pnl;
                activeTrade = null;
            }

            // Mark-to-market: include unrealized P&L
            double mtmEquity = equity;
            if (activeTrade != null)
            {
                double currentPrice = (double)candle.Close;
                double unrealized = activeTrade.TradeType == "LONG"
                    ? (currentPrice - activeTrade.EntryPrice) * activeTrade.Quantity
                    : (activeTrade.EntryPrice - currentPrice) * activeTrade.Quantity;
                mtmEquity += unrealized;
            }

            equityCurve.Add(new EquityPoint(candleTime, Math.Round(mtmEquity, 2)));

            if (mtmEquity > peak)
                peak = mtmEquity;

            var drawdown = peak - mtmEquity;
            if (drawdown > maxDrawdown)
                maxDrawdown = drawdown;
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
            EquityCurve: equityCurve.OrderBy(e => e.Timestamp).ToList()
        );
    }

    private static BacktestMetrics EmptyMetrics(double initialCapital, DateTime timestamp) =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0,
            [new EquityPoint(timestamp, initialCapital)]);
}