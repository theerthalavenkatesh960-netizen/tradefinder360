using Microsoft.Extensions.Logging;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Indicators;
using TradingSystem.Scanner.Strategies;

namespace TradingSystem.Scanner.Services;

/// <summary>
/// Service for backtesting trading strategies on historical data
/// </summary>
public class BacktestingService
{
    private readonly IMarketCandleRepository _candleRepository;
    private readonly IInstrumentService _instrumentService;
    private readonly IIndicatorService _indicatorService;
    private readonly ILogger<BacktestingService> _logger;
    private readonly Dictionary<StrategyType, ITradingStrategy> _strategies;

    public BacktestingService(
        IMarketCandleRepository candleRepository,
        IInstrumentService instrumentService,
        IIndicatorService indicatorService,
        ILogger<BacktestingService> logger)
    {
        _candleRepository = candleRepository;
        _instrumentService = instrumentService;
        _indicatorService = indicatorService;
        _logger = logger;

        // Register all strategies
        _strategies = new Dictionary<StrategyType, ITradingStrategy>
        {
            [StrategyType.MOMENTUM] = new MomentumStrategy(),
            [StrategyType.BREAKOUT] = new BreakoutStrategy(),
            [StrategyType.MEAN_REVERSION] = new MeanReversionStrategy(),
            [StrategyType.SWING_TRADING] = new SwingTradingStrategy()
        };
    }

    /// <summary>
    /// Run backtest for a strategy on historical data
    /// </summary>
    public async Task<BacktestResult> RunBacktestAsync(
        BacktestConfig config,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting backtest for {Strategy} on instrument {InstrumentId} from {Start} to {End}",
            config.Strategy, config.InstrumentId, config.StartDate, config.EndDate);

        // Validate inputs
        ValidateConfig(config);

        // Get instrument
        var instrument = await _instrumentService.GetByIdAsync(config.InstrumentId);
        if (instrument == null)
            throw new ArgumentException($"Instrument {config.InstrumentId} not found");

        // Get strategy
        if (!_strategies.TryGetValue(config.Strategy, out var strategy))
            throw new ArgumentException($"Strategy {config.Strategy} not found");

        // Get historical candles
        var candles = await _candleRepository.GetByInstrumentIdAsync(
            config.InstrumentId,
            config.TimeframeMinutes,
            config.StartDate,
            config.EndDate,
            cancellationToken);

        if (!candles.Any())
            throw new InvalidOperationException("No historical data available for the specified period");

        _logger.LogInformation("Loaded {Count} candles for backtesting", candles.Count);

        // Run backtest simulation
        var result = SimulateBacktest(config, instrument, strategy, candles.ToList());

        _logger.LogInformation(
            "Backtest completed: {Trades} trades, {WinRate:F1}% win rate, {Return:F2}% return",
            result.TotalTrades, result.WinRate, result.TotalReturnPercent);

        return result;
    }

    private BacktestResult SimulateBacktest(
        BacktestConfig config,
        TradingInstrument instrument,
        ITradingStrategy strategy,
        List<MarketCandle> candles)
    {
        var result = new BacktestResult
        {
            Strategy = config.Strategy,
            Symbol = instrument.Symbol,
            TimeframeMinutes = config.TimeframeMinutes,
            StartDate = config.StartDate,
            EndDate = config.EndDate,
            InitialCapital = config.InitialCapital,
            TotalBars = candles.Count
        };

        decimal currentCapital = config.InitialCapital;
        decimal peakCapital = config.InitialCapital;
        decimal maxDrawdown = 0m;
        BacktestTrade? currentTrade = null;
        int tradeNumber = 0;
        int entryBarIndex = -1; // [Fix #5] Track entry bar index directly

        // Need minimum bars for indicators
        const int minBarsForIndicators = 50;
        if (candles.Count < minBarsForIndicators)
            throw new InvalidOperationException($"Need at least {minBarsForIndicators} bars for backtesting");

        // [Fix #1] Reuse IndicatorEngine — feed candles sequentially, compute all indicators
        // Uses same parameters as EnsureIndicatorsCalculatedAsync in IndicatorService
        var engine = new IndicatorEngine(
            emaFastPeriod: 20, emaSlowPeriod: 50,
            rsiPeriod: 14,
            macdFast: 12, macdSlow: 26, macdSignal: 9,
            adxPeriod: 14, atrPeriod: 14,
            bollingerPeriod: 20, bollingerStdDev: 2.0m);

        // [Fix #12] Pre-map all candles once instead of O(n²) re-mapping per bar
        var allCandles = candles.Select(MapToCandle).ToList();

        // Warm up the engine on the first minBarsForIndicators candles
        for (int i = 0; i < minBarsForIndicators; i++)
        {
            engine.Calculate(allCandles[i]);
        }

        // Simulate each bar from the warmup boundary onward
        for (int i = minBarsForIndicators; i < candles.Count; i++)
        {
            var currentBar = candles[i];
            var currentCandle = allCandles[i];

            // [Fix #1] Calculate full indicators via the engine (MACD, ADX, Bollinger, VWAP, DI±, etc.)
            var indicators = engine.Calculate(currentCandle);

            // [Fix #12] Build the historical slice as a span view — only needed for strategy.Evaluate
            var historicalCandles = allCandles.GetRange(0, i + 1);

            // Check if we have an open position
            if (currentTrade != null)
            {
                // [Fix #7 & #8] Track mark-to-market equity for drawdown and equity curve
                decimal unrealizedPnL = CalculateUnrealizedPnL(currentTrade, currentBar.Close);
                decimal markToMarketEquity = currentCapital + unrealizedPnL;

                // Update peak and drawdown using mark-to-market
                if (markToMarketEquity > peakCapital)
                {
                    peakCapital = markToMarketEquity;
                }
                else
                {
                    var drawdown = peakCapital - markToMarketEquity;
                    if (drawdown > maxDrawdown)
                    {
                        maxDrawdown = drawdown;
                        result.MaxDrawdownDate = currentBar.Timestamp.DateTime;
                    }
                }

                // Check exit conditions
                var exitResult = CheckExitConditions(
                    currentTrade,
                    currentBar,
                    config,
                    strategy,
                    instrument,
                    historicalCandles,
                    indicators);

                if (exitResult.shouldExit)
                {
                    // Close trade
                    currentTrade.ExitTime = currentBar.Timestamp.DateTime;
                    currentTrade.ExitPrice = exitResult.exitPrice;
                    currentTrade.ExitReason = exitResult.exitReason;
                    currentTrade.BarsHeld = i - entryBarIndex; // [Fix #5] Direct subtraction, no FindIndex

                    // Calculate P&L
                    decimal grossPnL = currentTrade.Direction == "BUY"
                        ? (currentTrade.ExitPrice - currentTrade.EntryPrice) * currentTrade.Quantity
                        : (currentTrade.EntryPrice - currentTrade.ExitPrice) * currentTrade.Quantity;

                    // [Fix #6] Commission was already deducted from capital on entry.
                    // Only add exit commission here. PnL = gross - exit commission only.
                    decimal exitCommission = currentTrade.ExitPrice * currentTrade.Quantity * (config.CommissionPercent / 100);
                    currentTrade.Commission += exitCommission;

                    currentTrade.PnL = grossPnL - exitCommission; // [Fix #6] Subtract only exit commission from gross
                    currentTrade.PnLPercent = (currentTrade.PnL / (currentTrade.EntryPrice * currentTrade.Quantity)) * 100;

                    currentCapital += currentTrade.PnL; // Net of exit commission; entry commission already deducted
                    result.Trades.Add(currentTrade);

                    // Update peak after trade close as well
                    if (currentCapital > peakCapital)
                    {
                        peakCapital = currentCapital;
                    }

                    currentTrade = null;
                    entryBarIndex = -1;
                }

                // [Fix #8] Record mark-to-market equity (includes unrealized P&L during open trades)
                result.EquityCurve[currentBar.Timestamp.DateTime] = currentTrade != null
                    ? markToMarketEquity
                    : currentCapital;
            }
            else
            {
                // No open position, check for entry signal
                var signal = strategy.Evaluate(instrument, historicalCandles, indicators);

                if (signal.IsValid && signal.Confidence >= config.Strategy switch
                {
                    StrategyType.MOMENTUM => 60,
                    StrategyType.BREAKOUT => 65,
                    StrategyType.MEAN_REVERSION => 55,
                    StrategyType.SWING_TRADING => 60,
                    _ => 60
                })
                {
                    // [Fix #6] Position size based on risk per trade:
                    // Risk amount = capital * PositionSizePercent / 100
                    // Risk per share = |entry - stopLoss|
                    // Quantity = riskAmount / riskPerShare
                    tradeNumber++;
                    decimal riskAmount = currentCapital * (config.PositionSizePercent / 100);
                    decimal riskPerShare = Math.Abs(signal.EntryPrice - signal.StopLoss);

                    int quantity;
                    if (riskPerShare > 0)
                    {
                        quantity = (int)(riskAmount / riskPerShare);
                    }
                    else
                    {
                        // Fallback: if stop loss equals entry (shouldn't happen), use old method
                        quantity = (int)(riskAmount / signal.EntryPrice);
                    }

                    if (quantity > 0)
                    {
                        decimal entryCommission = signal.EntryPrice * quantity * (config.CommissionPercent / 100);

                        currentTrade = new BacktestTrade
                        {
                            TradeNumber = tradeNumber,
                            EntryTime = currentBar.Timestamp.DateTime,
                            Direction = signal.Direction,
                            EntryPrice = signal.EntryPrice,
                            StopLoss = signal.StopLoss,
                            Target = signal.Target,
                            Quantity = quantity,
                            Commission = entryCommission
                        };

                        entryBarIndex = i; // [Fix #5] Store bar index directly
                        currentCapital -= entryCommission; // Entry commission deducted from capital once
                    }
                }

                // Record equity curve (no open position)
                result.EquityCurve[currentBar.Timestamp.DateTime] = currentCapital;
            }
        }

        // Close any remaining open trade at the end
        if (currentTrade != null)
        {
            var lastBar = candles[^1];
            currentTrade.ExitTime = lastBar.Timestamp.DateTime;
            currentTrade.ExitPrice = lastBar.Close;
            currentTrade.ExitReason = "END_OF_PERIOD";
            currentTrade.BarsHeld = (candles.Count - 1) - entryBarIndex; // [Fix #5]

            decimal grossPnL = currentTrade.Direction == "BUY"
                ? (currentTrade.ExitPrice - currentTrade.EntryPrice) * currentTrade.Quantity
                : (currentTrade.EntryPrice - currentTrade.ExitPrice) * currentTrade.Quantity;

            decimal exitCommission = currentTrade.ExitPrice * currentTrade.Quantity * (config.CommissionPercent / 100);
            currentTrade.Commission += exitCommission;
            currentTrade.PnL = grossPnL - exitCommission; // [Fix #6] Only exit commission
            currentTrade.PnLPercent = (currentTrade.PnL / (currentTrade.EntryPrice * currentTrade.Quantity)) * 100;

            currentCapital += currentTrade.PnL;
            result.Trades.Add(currentTrade);
        }

        // Calculate final metrics
        result.FinalCapital = currentCapital;
        CalculatePerformanceMetrics(result, config, maxDrawdown);

        return result;
    }

    /// <summary>
    /// Calculate unrealized P&L for an open trade at the given market price.
    /// </summary>
    private static decimal CalculateUnrealizedPnL(BacktestTrade trade, decimal currentPrice)
    {
        return trade.Direction == "BUY"
            ? (currentPrice - trade.EntryPrice) * trade.Quantity
            : (trade.EntryPrice - currentPrice) * trade.Quantity;
    }

    private (bool shouldExit, decimal exitPrice, string exitReason) CheckExitConditions(
        BacktestTrade trade,
        MarketCandle currentBar,
        BacktestConfig config,
        ITradingStrategy strategy,
        TradingInstrument instrument,
        List<Candle> historicalCandles,
        IndicatorValues indicators)
    {
        // [Fix #4] When both stop-loss and target can be hit on the same bar,
        // use the candle Open to determine which was likely hit first.
        bool slHit = false;
        bool tgtHit = false;

        if (config.UseStopLoss)
        {
            if (trade.Direction == "BUY")
                slHit = currentBar.Low <= trade.StopLoss;
            else
                slHit = currentBar.High >= trade.StopLoss;
        }

        if (config.UseTarget)
        {
            if (trade.Direction == "BUY")
                tgtHit = currentBar.High >= trade.Target;
            else
                tgtHit = currentBar.Low <= trade.Target;
        }

        if (slHit && tgtHit)
        {
            // Both hit on the same bar — use Open to resolve ambiguity
            if (trade.Direction == "BUY")
            {
                // If open is closer to (or below) stop, stop was likely hit first
                bool stopFirst = currentBar.Open <= trade.StopLoss ||
                    Math.Abs(currentBar.Open - trade.StopLoss) < Math.Abs(currentBar.Open - trade.Target);
                return stopFirst
                    ? (true, trade.StopLoss, "STOP_LOSS")
                    : (true, trade.Target, "TARGET_HIT");
            }
            else
            {
                bool stopFirst = currentBar.Open >= trade.StopLoss ||
                    Math.Abs(currentBar.Open - trade.StopLoss) < Math.Abs(currentBar.Open - trade.Target);
                return stopFirst
                    ? (true, trade.StopLoss, "STOP_LOSS")
                    : (true, trade.Target, "TARGET_HIT");
            }
        }

        if (slHit)
            return (true, trade.StopLoss, "STOP_LOSS");

        if (tgtHit)
            return (true, trade.Target, "TARGET_HIT");

        // Check for signal reversal
        var signal = strategy.Evaluate(instrument, historicalCandles, indicators);
        if (signal.IsValid && signal.Direction != trade.Direction)
        {
            return (true, currentBar.Close, "SIGNAL_REVERSAL");
        }

        return (false, 0, string.Empty);
    }

    private void CalculatePerformanceMetrics(
        BacktestResult result, BacktestConfig config, decimal maxDrawdown)
    {
        var initialCapital = config.InitialCapital;

        result.TotalTrades = result.Trades.Count;
        result.WinningTrades = result.Trades.Count(t => t.PnL > 0);
        result.LosingTrades = result.Trades.Count(t => t.PnL <= 0);
        result.WinRate = result.TotalTrades > 0 ? (result.WinningTrades / (decimal)result.TotalTrades) * 100 : 0;

        // Return metrics
        result.TotalReturn = result.FinalCapital - initialCapital;
        result.TotalReturnPercent = (result.TotalReturn / initialCapital) * 100;
        result.AverageReturn = result.TotalTrades > 0 ? result.Trades.Average(t => t.PnL) : 0;
        result.AverageReturnPercent = result.TotalTrades > 0 ? result.Trades.Average(t => t.PnLPercent) : 0;

        var winningTrades = result.Trades.Where(t => t.PnL > 0).ToList();
        var losingTrades = result.Trades.Where(t => t.PnL <= 0).ToList();

        result.AverageWinPercent = winningTrades.Any() ? winningTrades.Average(t => t.PnLPercent) : 0;
        result.AverageLossPercent = losingTrades.Any() ? losingTrades.Average(t => t.PnLPercent) : 0;

        // Risk metrics — [Fix #7] maxDrawdown now includes intra-trade mark-to-market
        result.MaxDrawdown = maxDrawdown;
        // [Fix #7] MaxDrawdownPercent should be relative to the peak, not initial capital
        result.MaxDrawdownPercent = maxDrawdown > 0 && initialCapital > 0
            ? (maxDrawdown / (initialCapital + result.Trades
                .Where(t => t.PnL > 0)
                .Select((_, idx) => result.Trades.Take(idx + 1).Sum(tr => tr.PnL))
                .DefaultIfEmpty(0)
                .Max())) * 100
            : 0;
        // Simplified: use peak capital from equity curve for more accurate calculation
        if (result.EquityCurve.Count > 0)
        {
            var peakFromCurve = result.EquityCurve.Values.Max();
            result.MaxDrawdownPercent = peakFromCurve > 0 ? (maxDrawdown / peakFromCurve) * 100 : 0;
        }

        decimal grossProfit = winningTrades.Sum(t => t.PnL);
        decimal grossLoss = Math.Abs(losingTrades.Sum(t => t.PnL));
        // [Fix #9] ProfitFactor should be positive infinity (max decimal) when no losses, not 0
        result.ProfitFactor = grossLoss > 0
            ? grossProfit / grossLoss
            : grossProfit > 0 ? decimal.MaxValue : 0;

        // [Fix #10 & #11] Sharpe and Sortino ratios — sample std dev (N-1), annualized
        if (result.Trades.Count > 1)
        {
            var returns = result.Trades.Select(t => t.PnLPercent).ToList();
            var avgReturn = returns.Average();
            var stdDev = CalculateStandardDeviation(returns);

            // Annualization factor: estimate trades per year from backtest period
            var tradingDays = (config.EndDate - config.StartDate).TotalDays;
            decimal tradesPerYear = tradingDays > 0
                ? (decimal)(result.TotalTrades / tradingDays * 252)
                : result.TotalTrades;
            decimal annualizationFactor = tradesPerYear > 0
                ? (decimal)Math.Sqrt((double)tradesPerYear)
                : 1;

            result.SharpeRatio = stdDev > 0
                ? Math.Round(avgReturn / stdDev * annualizationFactor, 4)
                : 0;

            var negativeReturns = returns.Where(r => r < 0).ToList();
            var downstdDev = negativeReturns.Count >= 2
                ? CalculateStandardDeviation(negativeReturns)
                : 0;
            result.SortinoRatio = downstdDev > 0
                ? Math.Round(avgReturn / downstdDev * annualizationFactor, 4)
                : 0;
        }

        // Additional metrics
        result.LargestWin = winningTrades.Any() ? winningTrades.Max(t => t.PnL) : 0;
        result.LargestLoss = losingTrades.Any() ? losingTrades.Min(t => t.PnL) : 0;
        result.AverageBarsHeld = result.Trades.Any() ? (decimal)result.Trades.Average(t => t.BarsHeld) : 0;
        result.TotalCommission = result.Trades.Sum(t => t.Commission);

        // Calculate consecutive wins/losses
        result.ConsecutiveWins = CalculateMaxConsecutive(result.Trades, true);
        result.ConsecutiveLosses = CalculateMaxConsecutive(result.Trades, false);
    }

    /// <summary>
    /// [Fix #10] Sample standard deviation using (N-1) denominator (Bessel's correction).
    /// </summary>
    private static decimal CalculateStandardDeviation(List<decimal> values)
    {
        if (values.Count < 2) return 0;

        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumOfSquares / (values.Count - 1)));
    }

    private static int CalculateMaxConsecutive(List<BacktestTrade> trades, bool countWins)
    {
        int maxConsecutive = 0;
        int currentConsecutive = 0;

        foreach (var trade in trades)
        {
            bool isWin = trade.PnL > 0;
            if (isWin == countWins)
            {
                currentConsecutive++;
                maxConsecutive = Math.Max(maxConsecutive, currentConsecutive);
            }
            else
            {
                currentConsecutive = 0;
            }
        }

        return maxConsecutive;
    }

    private static Candle MapToCandle(MarketCandle mc)
    {
        return new Candle
        {
            Timestamp = mc.Timestamp,
            Open = mc.Open,
            High = mc.High,
            Low = mc.Low,
            Close = mc.Close,
            Volume = mc.Volume,
            TimeframeMinutes = mc.TimeframeMinutes
        };
    }

    private void ValidateConfig(BacktestConfig config)
    {
        if (config.StartDate >= config.EndDate)
            throw new ArgumentException("Start date must be before end date");

        if (config.InitialCapital <= 0)
            throw new ArgumentException("Initial capital must be positive");

        if (config.PositionSizePercent <= 0 || config.PositionSizePercent > 100)
            throw new ArgumentException("Position size must be between 0 and 100");

        if (config.CommissionPercent < 0)
            throw new ArgumentException("Commission cannot be negative");
    }
}