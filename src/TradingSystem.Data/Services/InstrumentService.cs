using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Services;

public class InstrumentService : IInstrumentService
{
    private readonly TradingDbContext _db;

    public InstrumentService(TradingDbContext db)
    {
        _db = db;
    }

    public async Task<TradingInstrument?> GetByKeyAsync(string instrumentKey)
        => await _db.Instruments.FirstOrDefaultAsync(i => i.InstrumentKey == instrumentKey);

    public async Task<List<TradingInstrument>> GetActiveAsync()
        => await _db.Instruments.Where(i => i.IsActive).OrderBy(i => i.Symbol).ToListAsync();

    public async Task<Dictionary<string, string>> GetKeyToSymbolMapAsync()
        => await _db.Instruments.ToDictionaryAsync(i => i.InstrumentKey, i => i.Symbol);

    public async Task AddAsync(TradingInstrument instrument)
    {
        await _db.Instruments.AddAsync(instrument);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(TradingInstrument instrument)
    {
        _db.Instruments.Update(instrument);
        await _db.SaveChangesAsync();
    }
}
