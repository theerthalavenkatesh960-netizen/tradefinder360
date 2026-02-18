using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public interface ITradeRepository
{
    Task SaveAsync(string instrumentKey, Trade trade);
    Task UpdateAsync(TradeRecord tradeRecord);
    Task<List<TradeRecord>> GetByInstrumentAsync(string instrumentKey, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<TradeRecord>> GetTodayTradesAsync(string instrumentKey);
    Task<TradeRecord?> GetByIdAsync(Guid id);
}
