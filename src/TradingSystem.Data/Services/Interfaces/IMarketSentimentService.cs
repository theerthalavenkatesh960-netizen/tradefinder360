using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface IMarketSentimentService
{
    Task<MarketContext> GetCurrentMarketContextAsync(CancellationToken cancellationToken = default);
    Task<MarketContext> AnalyzeAndUpdateMarketSentimentAsync(CancellationToken cancellationToken = default);
    Task<List<MarketSentiment>> GetHistoryAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    decimal AdjustConfidenceForMarketSentiment(decimal baseConfidence, SentimentType sentiment);
}