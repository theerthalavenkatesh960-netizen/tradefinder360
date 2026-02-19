using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IInstrumentRepository : ICommonRepository<TradingInstrument>
{
    Task<TradingInstrument?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
    Task<TradingInstrument?> GetByInstrumentKeyAsync(string instrumentKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradingInstrument>> GetActiveInstrumentsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TradingInstrument>> GetByExchangeAsync(string exchange, CancellationToken cancellationToken = default);
    Task<int> BulkUpsertAsync(IEnumerable<TradingInstrument> instruments, CancellationToken cancellationToken = default);
}
