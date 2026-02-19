using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface ITradeService
{
    Task SaveAsync(string instrumentKey, Trade trade);
    Task UpdateAsync(TradeRecord tradeRecord);
    Task<List<TradeRecord>> GetByInstrumentAsync(string instrumentKey, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<TradeRecord>> GetTodayAsync(string instrumentKey);
    Task<TradeRecord?> GetByIdAsync(Guid id);
}
