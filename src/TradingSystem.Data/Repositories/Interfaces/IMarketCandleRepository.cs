using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IMarketCandleRepository : ICommonRepository<MarketCandle>
{
    Task<IReadOnlyList<MarketCandle>> GetByInstrumentKeyAsync(
        string instrumentKey,
        int timeframeMinutes,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    Task<MarketCandle?> GetLatestCandleAsync(
        string instrumentKey,
        int timeframeMinutes,
        CancellationToken cancellationToken = default);

    Task<int> BulkUpsertAsync(
        IEnumerable<MarketCandle> candles,
        CancellationToken cancellationToken = default);
}