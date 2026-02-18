using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;

namespace TradingSystem.Data.Repositories;

public class InstrumentRepository : IInstrumentRepository
{
    private readonly TradingDbContext _context;

    public InstrumentRepository(TradingDbContext context)
    {
        _context = context;
    }

    public async Task<TradingInstrument?> GetByKeyAsync(string instrumentKey)
    {
        return await _context.Instruments
            .FirstOrDefaultAsync(i => i.InstrumentKey == instrumentKey);
    }

    public async Task<List<TradingInstrument>> GetActiveInstrumentsAsync()
    {
        return await _context.Instruments
            .Where(i => i.IsActive)
            .OrderBy(i => i.Symbol)
            .ToListAsync();
    }

    public async Task<TradingInstrument?> GetByIdAsync(int id)
    {
        return await _context.Instruments.FindAsync(id);
    }

    public async Task AddAsync(TradingInstrument instrument)
    {
        instrument.CreatedAt = DateTime.UtcNow;
        await _context.Instruments.AddAsync(instrument);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(TradingInstrument instrument)
    {
        instrument.UpdatedAt = DateTime.UtcNow;
        _context.Instruments.Update(instrument);
        await _context.SaveChangesAsync();
    }
}
