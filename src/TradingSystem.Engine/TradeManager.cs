using TradingSystem.Core.Models;

namespace TradingSystem.Engine;

public class TradeManager
{
    private Trade? _currentTrade;
    private readonly object _lock = new();

    public bool HasActiveTrade()
    {
        lock (_lock)
        {
            return _currentTrade != null && _currentTrade.State == TradeState.IN_TRADE;
        }
    }

    public Trade? GetActiveTrade()
    {
        lock (_lock)
        {
            return _currentTrade;
        }
    }

    public void SetActiveTrade(Trade trade)
    {
        lock (_lock)
        {
            _currentTrade = trade;
            _currentTrade.State = TradeState.IN_TRADE;
        }
    }

    public void CloseTrade(
        decimal spotExitPrice,
        decimal optionExitPrice,
        string exitReason,
        Dictionary<string, decimal>? exitIndicators = null)
    {
        lock (_lock)
        {
            if (_currentTrade == null) return;

            _currentTrade.State = TradeState.EXITED;
            _currentTrade.ExitTime = DateTime.Now;
            _currentTrade.SpotExitPrice = spotExitPrice;
            _currentTrade.OptionExitPrice = optionExitPrice;
            _currentTrade.ExitReason = exitReason;
            _currentTrade.ExitIndicators = exitIndicators;

            var pnl = (_currentTrade.OptionExitPrice.Value - _currentTrade.OptionEntryPrice) * _currentTrade.Quantity;
            if (_currentTrade.Direction == TradeDirection.PUT)
            {
                pnl = (_currentTrade.OptionEntryPrice - _currentTrade.OptionExitPrice.Value) * _currentTrade.Quantity;
            }

            _currentTrade.PnL = pnl;
            _currentTrade.PnLPercent = (_currentTrade.OptionExitPrice.Value - _currentTrade.OptionEntryPrice) /
                                       _currentTrade.OptionEntryPrice * 100;
        }
    }

    public Trade? ClearTrade()
    {
        lock (_lock)
        {
            var completedTrade = _currentTrade;
            _currentTrade = null;
            return completedTrade;
        }
    }

    public TradeState GetState()
    {
        lock (_lock)
        {
            return _currentTrade?.State ?? TradeState.WAIT;
        }
    }
}
