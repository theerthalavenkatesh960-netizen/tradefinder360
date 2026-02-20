using Microsoft.EntityFrameworkCore;
using TradingSystem.Core.Models;
using TradingSystem.Data.Repositories.Interfaces;

namespace TradingSystem.Data.Repositories;

public class InstrumentRepository : CommonRepository<TradingInstrument>, IInstrumentRepository
{
    public InstrumentRepository(TradingDbContext context) : base(context)
    {
    }

    public async Task<TradingInstrument?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(i => i.Symbol == symbol, cancellationToken);
    }

    public async Task<TradingInstrument?> GetByInstrumentKeyAsync(string instrumentKey, CancellationToken cancellationToken = default)
    {
        return await _dbSet.FirstOrDefaultAsync(i => i.InstrumentKey == instrumentKey, cancellationToken);
    }

    public async Task<IReadOnlyList<TradingInstrument>> GetActiveInstrumentsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.IsActive && i.InstrumentType == InstrumentType.STOCK)
            .OrderBy(i => i.Symbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TradingInstrument>> GetByExchangeAsync(string exchange, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .Where(i => i.Exchange == exchange && i.IsActive)
            .OrderBy(i => i.Symbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> BulkUpsertAsync(IEnumerable<TradingInstrument> instruments, CancellationToken cancellationToken = default)
    {
        var instrumentList = instruments.ToList();
        var instrumentKeys = instrumentList.Select(i => i.InstrumentKey).ToList();

        var existingInstruments = await _dbSet
            .Where(i => instrumentKeys.Contains(i.InstrumentKey))
            .ToDictionaryAsync(i => i.InstrumentKey, cancellationToken);

        var toAdd = new List<TradingInstrument>();
        var toUpdate = new List<TradingInstrument>();

        foreach (var instrument in instrumentList)
        {
            if (existingInstruments.TryGetValue(instrument.InstrumentKey, out var existing))
            {
                existing.Exchange = instrument.Exchange;
                existing.Symbol = instrument.Symbol;
                existing.Name = instrument.Name;
                existing.InstrumentType = instrument.InstrumentType;
                existing.LotSize = instrument.LotSize;
                existing.TickSize = instrument.TickSize;
                existing.IsDerivativesEnabled = instrument.IsDerivativesEnabled;
                existing.DefaultTradingMode = instrument.DefaultTradingMode;
                existing.IsActive = instrument.IsActive;
                existing.UpdatedAt = DateTime.UtcNow;
                toUpdate.Add(existing);
            }
            else
            {
                instrument.CreatedAt = DateTime.UtcNow;
                instrument.UpdatedAt = DateTime.UtcNow;
                toAdd.Add(instrument);
            }
        }

        if (toAdd.Any())
        {
            await _dbSet.AddRangeAsync(toAdd, cancellationToken);
        }

        if (toUpdate.Any())
        {
            _dbSet.UpdateRange(toUpdate);
        }

        return await _context.SaveChangesAsync(cancellationToken);
    }
}
