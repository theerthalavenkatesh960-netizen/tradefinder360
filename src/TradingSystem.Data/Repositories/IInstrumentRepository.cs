using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public interface IInstrumentRepository
{
    Task<TradingInstrument?> GetByKeyAsync(string instrumentKey);
    Task<List<TradingInstrument>> GetActiveInstrumentsAsync();
    Task<TradingInstrument?> GetByIdAsync(int id);
    Task AddAsync(TradingInstrument instrument);
    Task UpdateAsync(TradingInstrument instrument);
}
