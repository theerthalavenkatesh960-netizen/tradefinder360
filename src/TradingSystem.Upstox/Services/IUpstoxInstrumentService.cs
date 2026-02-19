using TradingSystem.Core.Models;

namespace TradingSystem.Upstox.Services;

public interface IUpstoxInstrumentService
{
    Task<List<TradingInstrument>> FetchInstrumentsAsync(string exchange, CancellationToken cancellationToken = default);
    Task<List<TradingInstrument>> FetchAllEquityInstrumentsAsync(CancellationToken cancellationToken = default);
}
