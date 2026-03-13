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
            "Backtest completed: {Trades} trades, {WinRate}% win rate, {Return}% return",
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

        // Need minimum bars for indicators
        const int minBarsForIndicators = 50;
        if (candles.Count < minBarsForIndicators)
            throw new InvalidOperationException($"Need at least {minBarsForIndicators} bars for backtesting");

        // Simulate each bar
        for (int i = minBarsForIndicators; i < candles.Count; i++)
        {
            var currentBar = candles[i];
            var historicalCandles = candles.Take(i + 1).Select(MapToCandle).ToList();
            
            // Calculate indicators for current bar
            var indicators = CalculateIndicators(historicalCandles);

            // Check if we have an open position
            if (currentTrade != null)
            {
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
                    currentTrade.BarsHeld = i - candles.FindIndex(c => c.Timestamp == currentTrade.EntryTime);

                    // Calculate P&L
                    decimal grossPnL = currentTrade.Direction == "BUY"
                        ? (currentTrade.ExitPrice - currentTrade.EntryPrice) * currentTrade.Quantity
                        : (currentTrade.EntryPrice - currentTrade.ExitPrice) * currentTrade.Quantity;

                    decimal exitCommission = currentTrade.ExitPrice * currentTrade.Quantity * (config.CommissionPercent / 100);
                    currentTrade.Commission += exitCommission;

                    currentTrade.PnL = grossPnL - currentTrade.Commission;
                    currentTrade.PnLPercent = (currentTrade.PnL / (currentTrade.EntryPrice * currentTrade.Quantity)) * 100;

                    currentCapital += currentTrade.PnL;
                    result.Trades.Add(currentTrade);

                    // Update peak and drawdown
                    if (currentCapital > peakCapital)
                    {
                        peakCapital = currentCapital;
                    }
                    else
                    {
                        var drawdown = peakCapital - currentCapital;
                        if (drawdown > maxDrawdown)
                        {
                            maxDrawdown = drawdown;
                            result.MaxDrawdownDate = currentBar.Timestamp.DateTime;
                        }
                    }

                    currentTrade = null;
                }
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
                    // Open new trade
                    tradeNumber++;
                    decimal positionSize = currentCapital * (config.PositionSizePercent / 100);
                    int quantity = (int)(positionSize / signal.EntryPrice);

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

                        currentCapital -= entryCommission;
                    }
                }
            }

            // Record equity curve
            result.EquityCurve[currentBar.Timestamp.DateTime] = currentCapital;
        }

        // Close any remaining open trade at the end
        if (currentTrade != null)
        {
            var lastBar = candles[^1];
            currentTrade.ExitTime = lastBar.Timestamp.DateTime;
            currentTrade.ExitPrice = lastBar.Close;
            currentTrade.ExitReason = "END_OF_PERIOD";
            currentTrade.BarsHeld = candles.Count - candles.FindIndex(c => c.Timestamp.DateTime == currentTrade.EntryTime);

            decimal grossPnL = currentTrade.Direction == "BUY"
                ? (currentTrade.ExitPrice - currentTrade.EntryPrice) * currentTrade.Quantity
                : (currentTrade.EntryPrice - currentTrade.ExitPrice) * currentTrade.Quantity;

            decimal exitCommission = currentTrade.ExitPrice * currentTrade.Quantity * (config.CommissionPercent / 100);
            currentTrade.Commission += exitCommission;
            currentTrade.PnL = grossPnL - currentTrade.Commission;
            currentTrade.PnLPercent = (currentTrade.PnL / (currentTrade.EntryPrice * currentTrade.Quantity)) * 100;

            currentCapital += currentTrade.PnL;
            result.Trades.Add(currentTrade);
        }

        // Calculate final metrics
        result.FinalCapital = currentCapital;
        CalculatePerformanceMetrics(result, config.InitialCapital, maxDrawdown);

        return result;
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
        // Check stop loss
        if (config.UseStopLoss)
        {
            if (trade.Direction == "BUY" && currentBar.Low <= trade.StopLoss)
            {
                return (true, trade.StopLoss, "STOP_LOSS");
            }
            if (trade.Direction == "SELL" && currentBar.High >= trade.StopLoss)
            {
                return (true, trade.StopLoss, "STOP_LOSS");
            }
        }

        // Check target
        if (config.UseTarget)
        {
            if (trade.Direction == "BUY" && currentBar.High >= trade.Target)
            {
                return (true, trade.Target, "TARGET_HIT");
            }
            if (trade.Direction == "SELL" && currentBar.Low <= trade.Target)
            {
                return (true, trade.Target, "TARGET_HIT");
            }
        }

        // Check for signal reversal
        var signal = strategy.Evaluate(instrument, historicalCandles, indicators);
        if (signal.IsValid && signal.Direction != trade.Direction)
        {
            return (true, currentBar.Close, "SIGNAL_REVERSAL");
        }

        return (false, 0, string.Empty);
    }

    private void CalculatePerformanceMetrics(BacktestResult result, decimal initialCapital, decimal maxDrawdown)
    {
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

        // Risk metrics
        result.MaxDrawdown = maxDrawdown;
        result.MaxDrawdownPercent = initialCapital > 0 ? (maxDrawdown / initialCapital) * 100 : 0;

        decimal grossProfit = winningTrades.Sum(t => t.PnL);
        decimal grossLoss = Math.Abs(losingTrades.Sum(t => t.PnL));
        result.ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : 0;

        // Sharpe and Sortino ratios
        if (result.Trades.Count > 1)
        {
            var returns = result.Trades.Select(t => t.PnLPercent).ToList();
            var avgReturn = returns.Average();
            var stdDev = CalculateStandardDeviation(returns);
            result.SharpeRatio = stdDev > 0 ? avgReturn / stdDev : 0;

            var negativeReturns = returns.Where(r => r < 0).ToList();
            var downstdDev = negativeReturns.Any() ? CalculateStandardDeviation(negativeReturns) : 0;
            result.SortinoRatio = downstdDev > 0 ? avgReturn / downstdDev : 0;
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

    private decimal CalculateStandardDeviation(List<decimal> values)
    {
        if (values.Count < 2) return 0;

        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumOfSquares / values.Count));
    }

    private int CalculateMaxConsecutive(List<BacktestTrade> trades, bool countWins)
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

    private IndicatorValues CalculateIndicators(List<Candle> candles)
    {
        // Calculate indicators on the fly for backtesting
        var closes = candles.Select(c => c.Close).ToList();
        var highs = candles.Select(c => c.High).ToList();
        var lows = candles.Select(c => c.Low).ToList();
        var volumes = candles.Select(c => c.Volume).ToList();

        return new IndicatorValues
        {
            EMAFast = CalculateEMA(closes, 12),
            EMASlow = CalculateEMA(closes, 26),
            RSI = CalculateRSI(closes, 14),
            ATR = CalculateATR(highs, lows, closes, 14),
            // Add other indicators as needed
        };
    }

    private decimal CalculateEMA(List<decimal> prices, int period)
    {
        if (prices.Count < period) return prices.LastOrDefault();
        
        decimal multiplier = 2m / (period + 1);
        decimal ema = prices.Take(period).Average();

        foreach (var price in prices.Skip(period))
        {
            ema = (price - ema) * multiplier + ema;
        }

        return ema;
    }

    private decimal CalculateRSI(List<decimal> prices, int period)
    {
        if (prices.Count < period + 1) return 50m;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < prices.Count; i++)
        {
            var change = prices[i] - prices[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? Math.Abs(change) : 0);
        }

        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();

        if (avgLoss == 0) return 100m;

        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private decimal CalculateATR(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        if (highs.Count < period + 1) return 0;

        var trueRanges = new List<decimal>();
        for (int i = 1; i < highs.Count; i++)
        {
            var tr = Math.Max(
                highs[i] - lows[i],
                Math.Max(
                    Math.Abs(highs[i] - closes[i - 1]),
                    Math.Abs(lows[i] - closes[i - 1])
                )
            );
            trueRanges.Add(tr);
        }

        return trueRanges.TakeLast(period).Average();
    }

    private Candle MapToCandle(MarketCandle mc)
    {
        return new Candle
        {
            Timestamp = mc.Timestamp,
            Open = mc.Open,
            High = mc.High,
            Low = mc.Low,
            Close = mc.Close,
            Volume = mc.Volume
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