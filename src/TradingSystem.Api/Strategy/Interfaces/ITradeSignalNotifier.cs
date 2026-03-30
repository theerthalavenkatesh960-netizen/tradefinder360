using TradingSystem.Api.Strategy.Models;

namespace TradingSystem.Api.Strategy.Interfaces;

/// <summary>
/// Notifies connected clients when a trade signal is generated.
/// </summary>
public interface ITradeSignalNotifier
{
    Task NotifyAsync(TradeSignal signal, CancellationToken ct = default);

    /// <summary>
    /// Returns all signals generated in the current session.
    /// </summary>
    IReadOnlyList<TradeSignal> GetSessionSignals();

    /// <summary>
    /// Provides a stream of signals as they are generated.
    /// </summary>
    IAsyncEnumerable<TradeSignal> GetSignalStreamAsync(CancellationToken ct = default);
}
