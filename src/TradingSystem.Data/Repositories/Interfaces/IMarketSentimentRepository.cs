using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface IMarketSentimentRepository : ICommonRepository<MarketSentiment>
{
    Task<MarketSentiment?> GetLatestAsync(CancellationToken cancellationToken = default);
    Task<List<MarketSentiment>> GetHistoryAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}