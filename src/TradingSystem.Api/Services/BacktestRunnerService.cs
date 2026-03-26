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

        var trades = request.Strategy.Name.ToUpperInvariant() switch
        {
            "ORB" => RunORB(orderedCandles, indicators, request.Strategy.Params, initialCapital),
            "RSI_REVERSAL" => RunRsiReversal(orderedCandles, indicators, request.Strategy.Params, initialCapital),
            "EMA_CROSSOVER" => RunEmaCrossover(orderedCandles, indicators, request.Strategy.Params, initialCapital),
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

    // ???????????????????????????????????????????????????????????????
    // STRATEGY A: Opening Range Breakout
    // ???????????????????????????????????????????????????????????????
    private List<BacktestTradeResult> RunORB(
        List<Candle> candles, IndicatorValues[] indicators, StrategyParams p, double capital)
    {
        var trades = new List<BacktestTradeResult>();
        var timeframe = p.Timeframe;

        // Number of opening-range candles: first 5 minutes worth
        int orbCandleCount = timeframe switch
        {
            1 => 5,
            _ => 1
        };

        // Group candles by trading day in IST
        var dayGroups = candles
            .Select((c, i) => (Candle: c, Index: i, Indicator: indicators[i]))
            .GroupBy(x => TimeZoneInfo.ConvertTime(x.Candle.Timestamp, Ist).Date)
            .OrderBy(g => g.Key);

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

            for (int j = orbCandleCount; j < dayCandles.Count; j++)
            {
                var (candle, globalIdx, ind) = dayCandles[j];
                var istTime = TimeZoneInfo.ConvertTime(candle.Timestamp, Ist);

                // Force close at end of day
                if (j == dayCandles.Count - 1 && openTrade != null)
                {
                    trades.Add(CloseTrade(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close));
                    openTrade = null;
                    break;
                }

                // Check exits for open trade
                if (openTrade != null)
                {
                    var exitResult = CheckExit(openTrade, candle, (double)ind.ATR, p, ref trailStop);
                    if (exitResult != null)
                    {
                        trades.Add(exitResult);
                        openTrade = null;
                    }
                    continue;
                }

                // No new trades after 14:00 IST
                if (TimeOnly.FromDateTime(istTime.DateTime) >= NoCutoff)
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
                        var entryPrice = (double)nextCandle.Open;
                        var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                        var sl = entryPrice - slDistance;
                        var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                        var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                        openTrade = new BacktestTradeResult(
                            Guid.NewGuid().ToString(),
                            nextCandle.Timestamp.UtcDateTime, entryPrice,
                            default, 0, sl, target, qty, 0, 0, "LONG");
                        trailStop = sl;
                        j++; // skip the entry candle
                    }
                }
                else if ((double)candle.Close < openingLow)
                {
                    // SHORT breakout
                    if (j + 1 < dayCandles.Count)
                    {
                        var nextCandle = dayCandles[j + 1].Candle;
                        var entryPrice = (double)nextCandle.Open;
                        var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                        var sl = entryPrice + slDistance;
                        var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                        var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                        openTrade = new BacktestTradeResult(
                            Guid.NewGuid().ToString(),
                            nextCandle.Timestamp.UtcDateTime, entryPrice,
                            default, 0, sl, target, qty, 0, 0, "SHORT");
                        trailStop = sl;
                        j++;
                    }
                }
            }

            // Force close at day end if still open
            if (openTrade != null)
            {
                var lastCandle = dayCandles[^1].Candle;
                trades.Add(CloseTrade(openTrade, lastCandle.Timestamp.UtcDateTime, (double)lastCandle.Close));
            }
        }

        return trades;
    }

    // ???????????????????????????????????????????????????????????????
    // STRATEGY B: RSI Reversal
    // ???????????????????????????????????????????????????????????????
    private List<BacktestTradeResult> RunRsiReversal(
        List<Candle> candles, IndicatorValues[] indicators, StrategyParams p, double capital)
    {
        var trades = new List<BacktestTradeResult>();
        var oversold = p.RsiOversold ?? 30;
        var overbought = p.RsiOverbought ?? 70;

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
                    // Cooling-off logic: if loss, skip 2 candles in same direction
                    if (exitResult.Pnl < 0)
                    {
                        cooldownDirection = exitResult.TradeType;
                        cooldownUntilIndex = i + 2;
                    }
                    trades.Add(exitResult);
                    openTrade = null;
                }
                continue;
            }

            // Force close on last candle
            if (i == candles.Count - 2 && openTrade != null)
            {
                trades.Add(CloseTrade(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close));
                openTrade = null;
                continue;
            }

            var atr = (double)ind.ATR;
            if (atr <= 0) continue;

            var rsiNow = (double)ind.RSI;
            var rsiNext = (double)nextInd.RSI;

            // LONG: RSI crosses back up from oversold, price > VWAP
            if (rsiNow < oversold && rsiNext > oversold && (double)candle.Close > (double)ind.VWAP)
            {
                if (cooldownDirection == "LONG" && i <= cooldownUntilIndex)
                    continue;

                if (i + 2 < candles.Count)
                {
                    var entryCandle = candles[i + 2]; // enter at candle after next
                    var entryPrice = (double)entryCandle.Open;
                    var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                    var sl = entryPrice - slDistance;
                    var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                    var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                    openTrade = new BacktestTradeResult(
                        Guid.NewGuid().ToString(),
                        entryCandle.Timestamp.UtcDateTime, entryPrice,
                        default, 0, sl, target, qty, 0, 0, "LONG");
                    trailStop = sl;
                    i += 2;
                }
            }
            // SHORT: RSI crosses back down from overbought, price < VWAP
            else if (rsiNow > overbought && rsiNext < overbought && (double)candle.Close < (double)ind.VWAP)
            {
                if (cooldownDirection == "SHORT" && i <= cooldownUntilIndex)
                    continue;

                if (i + 2 < candles.Count)
                {
                    var entryCandle = candles[i + 2];
                    var entryPrice = (double)entryCandle.Open;
                    var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                    var sl = entryPrice + slDistance;
                    var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                    var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                    openTrade = new BacktestTradeResult(
                        Guid.NewGuid().ToString(),
                        entryCandle.Timestamp.UtcDateTime, entryPrice,
                        default, 0, sl, target, qty, 0, 0, "SHORT");
                    trailStop = sl;
                    i += 2;
                }
            }
        }

        // Force close if still open
        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            trades.Add(CloseTrade(openTrade, last.Timestamp.UtcDateTime, (double)last.Close));
        }

        return trades;
    }

    // ???????????????????????????????????????????????????????????????
    // STRATEGY C: EMA Crossover
    // ???????????????????????????????????????????????????????????????
    private List<BacktestTradeResult> RunEmaCrossover(
        List<Candle> candles, IndicatorValues[] indicators, StrategyParams p, double capital)
    {
        var trades = new List<BacktestTradeResult>();

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
                    trades.Add(CloseTrade(openTrade, candle.Timestamp.UtcDateTime, (double)candle.Close));
                    openTrade = null;
                    // Fall through to check for new entry on this candle
                }
                else
                {
                    var exitResult = CheckExit(openTrade, candle, atr, p, ref trailStop);
                    if (exitResult != null)
                    {
                        trades.Add(exitResult);
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
                var entryPrice = (double)nextCandle.Open;
                var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                var sl = entryPrice - slDistance;
                var target = CalcTarget(p, entryPrice, slDistance, atr, true);
                var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                openTrade = new BacktestTradeResult(
                    Guid.NewGuid().ToString(),
                    nextCandle.Timestamp.UtcDateTime, entryPrice,
                    default, 0, sl, target, qty, 0, 0, "LONG");
                trailStop = sl;
                i++;
            }
            // Bearish crossover
            else if (bearishCross && i + 1 < candles.Count)
            {
                var nextCandle = candles[i + 1];
                var entryPrice = (double)nextCandle.Open;
                var slDistance = CalcStopLossDistance(p, entryPrice, atr, (double)candle.Low, (double)candle.High);
                var sl = entryPrice + slDistance;
                var target = CalcTarget(p, entryPrice, slDistance, atr, false);
                var qty = CalcQuantity(capital, p.RiskPercent, slDistance);

                openTrade = new BacktestTradeResult(
                    Guid.NewGuid().ToString(),
                    nextCandle.Timestamp.UtcDateTime, entryPrice,
                    default, 0, sl, target, qty, 0, 0, "SHORT");
                trailStop = sl;
                i++;
            }
        }

        // Force close if still open
        if (openTrade != null && candles.Count > 0)
        {
            var last = candles[^1];
            trades.Add(CloseTrade(openTrade, last.Timestamp.UtcDateTime, (double)last.Close));
        }

        return trades;
    }

    // ???????????????????????????????????????????????????????????????
    // SHARED HELPERS
    // ???????????????????????????????????????????????????????????????

    private static double CalcStopLossDistance(StrategyParams p, double entryPrice, double atr, double candleLow, double candleHigh)
    {
        return p.StopLossType.ToUpperInvariant() switch
        {
            "ATR" => atr * 1.5,
            "FIXED_PERCENT" => entryPrice * ((p.SlPercent ?? 1.0) / 100.0),
            "CANDLE" => Math.Max(entryPrice - candleLow, candleHigh - entryPrice),
            _ => atr * 1.5
        };
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

    private static BacktestTradeResult? CheckExit(
        BacktestTradeResult trade, Candle candle, double atr, StrategyParams p, ref double trailStop)
    {
        bool isLong = trade.TradeType == "LONG";
        double high = (double)candle.High;
        double low = (double)candle.Low;

        if (p.TargetType.Equals("TRAILING", StringComparison.OrdinalIgnoreCase))
        {
            if (isLong)
            {
                trailStop = Math.Max(trailStop, high - atr);
                if (low <= trailStop)
                    return CloseTrade(trade, candle.Timestamp.UtcDateTime, trailStop);
            }
            else
            {
                trailStop = Math.Min(trailStop, low + atr);
                if (high >= trailStop)
                    return CloseTrade(trade, candle.Timestamp.UtcDateTime, trailStop);
            }
            return null;
        }

        // RR_RATIO mode: check SL then target
        if (isLong)
        {
            if (low <= trade.StopLoss)
                return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.StopLoss);
            if (high >= trade.Target)
                return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.Target);
        }
        else
        {
            if (high >= trade.StopLoss)
                return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.StopLoss);
            if (low <= trade.Target)
                return CloseTrade(trade, candle.Timestamp.UtcDateTime, trade.Target);
        }

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

    // ???????????????????????????????????????????????????????????????
    // METRICS
    // ???????????????????????????????????????????????????????????????

    private static BacktestMetrics CalculateMetrics(List<BacktestTradeResult> trades, double initialCapital, List<Candle> candles)
    {
        if (trades.Count == 0)
            return EmptyMetrics(initialCapital, candles.Count > 0 ? candles[0].Timestamp.UtcDateTime : DateTime.UtcNow);

        int totalTrades = trades.Count;
        int winningTrades = trades.Count(t => t.Pnl >= 0);
        int losingTrades = trades.Count(t => t.Pnl < 0);
        double winRate = (double)winningTrades / totalTrades;
        double totalPnl = trades.Sum(t => t.Pnl);
        double totalReturn = (totalPnl / initialCapital) * 100.0;

        double grossProfit = trades.Where(t => t.Pnl > 0).Sum(t => t.Pnl);
        double grossLoss = Math.Abs(trades.Where(t => t.Pnl < 0).Sum(t => t.Pnl));
        double profitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;

        // Avg RR per trade
        double avgRR = trades.Average(t =>
        {
            var risk = Math.Abs(t.EntryPrice - t.StopLoss);
            return risk > 0 ? Math.Abs(t.ExitPrice - t.EntryPrice) / risk : 0;
        });

        // Equity curve and max drawdown
        var equityCurve = new List<EquityPoint>();
        double equity = initialCapital;
        double peak = initialCapital;
        double maxDrawdown = 0;

        // Initial point
        if (candles.Count > 0)
            equityCurve.Add(new EquityPoint(candles[0].Timestamp.UtcDateTime, initialCapital));

        foreach (var trade in trades.OrderBy(t => t.ExitTime))
        {
            equity += trade.Pnl;
            equityCurve.Add(new EquityPoint(trade.ExitTime, Math.Round(equity, 2)));

            if (equity > peak)
                peak = equity;

            var drawdown = peak - equity;
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
