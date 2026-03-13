using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories.Interfaces;

public interface ITradeOutcomeRepository
{
    Task<TradeOutcome> CreateAsync(TradeOutcome outcome, CancellationToken cancellationToken = default);
    Task<TradeOutcome?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<List<TradeOutcome>> GetOpenTradesAsync(CancellationToken cancellationToken = default);
    Task<List<TradeOutcome>> GetClosedTradesAsync(DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, CancellationToken cancellationToken = default);
    Task<List<TradeOutcome>> GetByModelVersionAsync(string modelVersion, CancellationToken cancellationToken = default);
    Task<List<TradeOutcome>> GetByMarketRegimeAsync(string regime, CancellationToken cancellationToken = default);
    Task UpdateAsync(TradeOutcome outcome, CancellationToken cancellationToken = default);
    Task<TradeOutcomeStatistics> GetStatisticsAsync(string? modelVersion = null, DateTimeOffset? startDate = null, DateTimeOffset? endDate = null, CancellationToken cancellationToken = default);
}

public class TradeOutcomeStatistics
{
    public int TotalTrades { get; set; }
    public int SuccessfulTrades { get; set; }
    public float WinRate { get; set; }
    public decimal TotalPnL { get; set; }
    public float AveragePredictionError { get; set; }
    public float AveragePredictionAccuracy { get; set; }
    public float ProfitFactor { get; set; }
    public float SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
}