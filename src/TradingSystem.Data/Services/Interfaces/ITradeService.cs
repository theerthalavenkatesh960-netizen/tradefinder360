using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services.Interfaces;

public interface ITradeService
{
    Task SaveAsync(int instrumentId, Trade trade);
    Task UpdateAsync(TradeRecord tradeRecord);
    Task<List<TradeRecord>> GetByInstrumentAsync(int instrumentId, DateTime? startDate = null, DateTime? endDate = null);
    Task<List<TradeRecord>> GetTodayAsync(int instrumentId);
    Task<TradeRecord?> GetByIdAsync(long id);
}
