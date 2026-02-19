using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public interface IInstrumentPriceRepository : ICommonRepository<InstrumentPrice>
{
    Task<IReadOnlyList<InstrumentPrice>> GetByInstrumentIdAsync(int instrumentId, string timeframe, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    Task<InstrumentPrice?> GetLatestPriceAsync(int instrumentId, string timeframe, CancellationToken cancellationToken = default);
    Task<int> BulkUpsertAsync(IEnumerable<InstrumentPrice> prices, CancellationToken cancellationToken = default);
    Task<Dictionary<int, InstrumentPrice>> GetLatestPricesForInstrumentsAsync(IEnumerable<int> instrumentIds, string timeframe, CancellationToken cancellationToken = default);
}
