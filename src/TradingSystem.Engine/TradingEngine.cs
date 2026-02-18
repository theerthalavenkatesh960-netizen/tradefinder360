using TradingSystem.Core.Models;
using TradingSystem.Configuration.Models;
using TradingSystem.MarketData;
using TradingSystem.Indicators;
using TradingSystem.MarketState;
using TradingSystem.Strategy;
using TradingSystem.Risk;
using TradingSystem.Execution;
using TradingSystem.Execution.Interfaces;
using TradingSystem.Logging;
using TradingSystem.Data.Services;

namespace TradingSystem.Engine;

public class TradingEngine
{
    private readonly TradingConfig _config;
    private readonly MarketDataEngine _marketData;
    private readonly IndicatorEngine _indicators;
    private readonly MarketStateEngine _marketStateEngine;
    private readonly StrategyEngine _strategy;
    private readonly RiskEngine _risk;
    private readonly ExecutionEngine _execution;
    private readonly TradingLogger _logger;
    private readonly TradingDataService _dataService;
    private readonly TradeManager _tradeManager;
    private readonly TradingInstrument _instrument;

    private IndicatorValues? _currentIndicators;
    private MarketStateInfo? _currentMarketState;

    public TradingEngine(
        TradingConfig config,
        TradingDataService dataService,
        TradingInstrument instrument)
    {
        _config = config;
        _dataService = dataService;
        _instrument = instrument;

        _marketData = new MarketDataEngine(_config.Timeframe);

        var scaler = new TimeframeScaler(_config.Timeframe, _config.Indicators);
        _indicators = scaler.CreateScaledIndicatorEngine();

        _marketStateEngine = new MarketStateEngine(_config.MarketState);
        _strategy = new StrategyEngine(_config.Limits);
        _risk = new RiskEngine(_config.Risk);

        IBrokerAdapter broker = new MockBrokerAdapter();
        _execution = new ExecutionEngine(broker, _config.Execution, _instrument);

        _logger = new TradingLogger();
        _tradeManager = new TradeManager();

        _marketData.OnNewCandle += OnNewCandle;

        _logger.LogInfo("Trading Engine Initialized");
        _logger.LogInfo("Instrument: {Instrument} | Mode: {Mode} | Timeframe: {Timeframe}min",
            _instrument.GetDisplayName(),
            _instrument.DefaultTradingMode,
            _config.Timeframe.ActiveTimeframeMinutes);
    }

    private async void OnNewCandle(object? sender, Candle candle)
    {
        try
        {
            _logger.LogCandle(candle);

            if (!_marketData.HasMinimumCandles(50))
            {
                _logger.LogDebug("Insufficient candles for analysis. Current: {Count}", _marketData.GetCandleCount());
                return;
            }

            _currentIndicators = _indicators.Calculate(candle);
            _strategy.UpdateIndicatorHistory(_currentIndicators);

            var candles = _marketData.GetCandles();
            _currentMarketState = _marketStateEngine.DetermineState(candles, _currentIndicators);

            _logger.LogIndicators(_currentIndicators.GetType().GetProperties()
                .ToDictionary(p => p.Name, p => (decimal)p.GetValue(_currentIndicators)!), candle.Timestamp);
            _logger.LogMarketState(_currentMarketState);

            if (_config.Database.EnablePersistence)
            {
                await _dataService.SaveCandleAsync(_instrument.InstrumentKey, candle);
                await _dataService.SaveIndicatorsAsync(_instrument.InstrumentKey, _config.Timeframe.ActiveTimeframeMinutes, _currentIndicators);
            }

            if (_tradeManager.HasActiveTrade())
            {
                await ManageExistingTrade(candle.Close);
            }
            else
            {
                await CheckForNewEntry(candles);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("OnNewCandle", ex);
        }
    }

    private async Task ManageExistingTrade(decimal currentSpotPrice)
    {
        var trade = _tradeManager.GetActiveTrade();
        if (trade == null || _currentIndicators == null) return;

        var exitSignal = _risk.CheckForExit(trade, currentSpotPrice, _currentIndicators, DateTime.Now);

        if (exitSignal.ShouldExit)
        {
            _logger.LogInfo("Exit signal triggered: {Reason}", exitSignal.Reason);

            var (success, exitPrice, message) = await _execution.ExecuteExit(
                new Option { Symbol = trade.OptionSymbol },
                trade.Quantity);

            if (success)
            {
                var exitIndicators = new Dictionary<string, decimal>
                {
                    ["RSI"] = _currentIndicators.RSI,
                    ["ADX"] = _currentIndicators.ADX,
                    ["MacdLine"] = _currentIndicators.MacdLine
                };

                _tradeManager.CloseTrade(currentSpotPrice, exitPrice, exitSignal.Reason, exitIndicators);

                var completedTrade = _tradeManager.ClearTrade();
                if (completedTrade != null)
                {
                    _logger.LogTradeExit(completedTrade, exitSignal.Reason);

                    if (_config.Database.EnablePersistence)
                        await _dataService.SaveTradeAsync(_instrument.InstrumentKey, completedTrade);

                    _risk.RecordTrade(completedTrade.PnL ?? 0, DateTime.Now);
                }
            }
            else
            {
                _logger.LogWarning("Failed to execute exit: {Message}", message);
            }
        }
    }

    private async Task CheckForNewEntry(List<Candle> candles)
    {
        if (_currentIndicators == null || _currentMarketState == null) return;

        if (!_strategy.ShouldAllowNewTrade(DateTime.Now))
        {
            _logger.LogDebug("No new trades allowed after cutoff time");
            return;
        }

        if (!_risk.CanTakeTrade(DateTime.Now, _config.Limits.MaxTradesPerDay))
        {
            _logger.LogRiskCheck("DailyLimits", false,
                $"Trades: {_risk.GetDailyTradeCount()}/{_config.Limits.MaxTradesPerDay}, Loss: {_risk.GetDailyLoss()}");
            return;
        }

        var entrySignal = _strategy.CheckForEntry(_currentMarketState, candles, _currentIndicators);

        _logger.LogSignal("Entry", entrySignal.IsValid, entrySignal.Reason, entrySignal.ValidationDetails);

        if (!entrySignal.IsValid) return;

        var riskParams = _risk.CalculateRiskParameters(
            entrySignal.EntryPrice,
            _currentIndicators.ATR,
            entrySignal.Direction,
            _instrument.LotSize);

        var (success, option, orderId, message) = await _execution.ExecuteEntry(
            entrySignal.Direction,
            entrySignal.EntryPrice,
            riskParams.PositionSize);

        if (success && option != null)
        {
            var trade = new Trade
            {
                Id = Guid.NewGuid(),
                EntryTime = DateTime.Now,
                Direction = entrySignal.Direction,
                State = TradeState.IN_TRADE,
                SpotEntryPrice = entrySignal.EntryPrice,
                OptionSymbol = option.Symbol,
                OptionStrike = option.Strike,
                OptionEntryPrice = option.LastPrice,
                Quantity = riskParams.PositionSize,
                StopLoss = riskParams.StopLossPrice,
                Target = riskParams.TargetPrice,
                ATRAtEntry = _currentIndicators.ATR,
                EntryReason = entrySignal.Reason,
                EntryIndicators = new Dictionary<string, decimal>
                {
                    ["RSI"] = _currentIndicators.RSI,
                    ["ADX"] = _currentIndicators.ADX,
                    ["MacdLine"] = _currentIndicators.MacdLine,
                    ["ATR"] = _currentIndicators.ATR
                }
            };

            _tradeManager.SetActiveTrade(trade);
            _logger.LogTradeEntry(trade, entrySignal.Reason);

            if (_config.Database.EnablePersistence)
                await _dataService.SaveTradeAsync(_instrument.InstrumentKey, trade);
        }
        else
        {
            _logger.LogWarning("Failed to execute entry: {Message}", message);
        }
    }

    public void ProcessTick(Tick tick)
    {
        _marketData.ProcessTick(tick);
    }

    public void ProcessCandle(Candle candle)
    {
        _marketData.ProcessCandle(candle);
    }

    public TradeState GetEngineState() => _tradeManager.GetState();

    public Trade? GetActiveTrade() => _tradeManager.GetActiveTrade();

    public MarketStateInfo? GetCurrentMarketState() => _currentMarketState;

    public IndicatorValues? GetCurrentIndicators() => _currentIndicators;
}
