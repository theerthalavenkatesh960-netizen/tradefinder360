using Serilog;
using Serilog.Formatting.Compact;
using TradingSystem.Core.Models;

namespace TradingSystem.Logging;

public class TradingLogger
{
    private readonly ILogger _logger;

    public TradingLogger(string logFilePath = "logs/trading.log")
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                new CompactJsonFormatter(),
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .CreateLogger();
    }

    public void LogCandle(Candle candle)
    {
        _logger.Information("Candle: {Timestamp} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume}",
            candle.Timestamp, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume);
    }

    public void LogIndicators(Dictionary<string, decimal> indicators, DateTimeOffset timestamp)
    {
        _logger.Information("Indicators at {Timestamp}: {@Indicators}",
            timestamp, indicators);
    }

    public void LogMarketState(MarketStateInfo state)
    {
        _logger.Information("Market State: {State} | Reason: {Reason} | {@Indicators}",
            state.State, state.Reason, state.Indicators);
    }

    public void LogTradeEntry(Trade trade, string reason)
    {
        _logger.Information("TRADE ENTRY: {Direction} | Spot: {SpotPrice} | Option: {OptionSymbol} @ {OptionPrice} | SL: {StopLoss} | Target: {Target} | Reason: {Reason}",
            trade.Direction, trade.SpotEntryPrice, trade.OptionSymbol, trade.OptionEntryPrice,
            trade.StopLoss, trade.Target, reason);
    }

    public void LogTradeExit(Trade trade, string reason)
    {
        _logger.Information("TRADE EXIT: {Direction} | Entry: {EntryPrice} | Exit: {ExitPrice} | PnL: {PnL} ({PnLPercent}%) | Reason: {Reason}",
            trade.Direction, trade.OptionEntryPrice, trade.OptionExitPrice,
            trade.PnL, trade.PnLPercent, reason);
    }

    public void LogSignal(string signalType, bool isValid, string reason, Dictionary<string, string>? details = null)
    {
        if (isValid)
        {
            _logger.Information("SIGNAL: {Type} | Valid: {IsValid} | Reason: {Reason} | {@Details}",
                signalType, isValid, reason, details);
        }
        else
        {
            _logger.Debug("Signal Rejected: {Type} | Reason: {Reason} | {@Details}",
                signalType, reason, details);
        }
    }

    public void LogRiskCheck(string checkType, bool passed, string reason)
    {
        _logger.Information("Risk Check: {Type} | Passed: {Passed} | {Reason}",
            checkType, passed, reason);
    }

    public void LogError(string context, Exception ex)
    {
        _logger.Error(ex, "Error in {Context}: {Message}", context, ex.Message);
    }

    public void LogInfo(string message, params object[] args)
    {
        _logger.Information(message, args);
    }

    public void LogWarning(string message, params object[] args)
    {
        _logger.Warning(message, args);
    }

    public void LogDebug(string message, params object[] args)
    {
        _logger.Debug(message, args);
    }
}
