using TradingSystem.Core.Models;

namespace TradingSystem.Upstox.Services;

public interface IUpstoxPriceService
{
    Task<List<InstrumentPrice>> FetchHistoricalPricesAsync(
        string instrumentKey,
        string interval,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    Task<Dictionary<string, List<InstrumentPrice>>> FetchBulkHistoricalPricesAsync(
        IEnumerable<string> instrumentKeys,
        string interval,
        DateTime fromDate,
        DateTime toDate,
        int batchSize = 10,
        CancellationToken cancellationToken = default);
}
