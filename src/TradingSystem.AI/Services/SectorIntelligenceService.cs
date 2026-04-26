using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Data;

namespace TradingSystem.AI.Services;

/// <summary>
/// Computes a normalized sector momentum signal in [-1, 1] from recent constituent price moves.
/// </summary>
public class SectorIntelligenceService
{
    private readonly TradingDbContext _dbContext;
    private readonly ILogger<SectorIntelligenceService> _logger;

    public SectorIntelligenceService(
        TradingDbContext dbContext,
        ILogger<SectorIntelligenceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<decimal> GetSectorSignalAsync(string sectorName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectorName))
        {
            return 0m;
        }

        var sectorId = await _dbContext.Sectors
            .AsNoTracking()
            .Where(x => x.IsActive && x.Name == sectorName)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!sectorId.HasValue)
        {
            return 0m;
        }

        var instrumentIds = await _dbContext.Instruments
            .AsNoTracking()
            .Where(x => x.IsActive && x.SectorId == sectorId.Value)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (instrumentIds.Count == 0)
        {
            return 0m;
        }

        var pricesSince = DateTimeOffset.UtcNow.AddHours(-36);
        var priceRows = await _dbContext.InstrumentPrices
            .AsNoTracking()
            .Where(x => instrumentIds.Contains(x.InstrumentId) && x.Timestamp >= pricesSince)
            .OrderByDescending(x => x.Timestamp)
            .ToListAsync(cancellationToken);

        var returns = new List<decimal>();
        foreach (var group in priceRows.GroupBy(x => x.InstrumentId))
        {
            var pair = group
                .OrderByDescending(x => x.Timestamp)
                .Take(2)
                .ToList();

            if (pair.Count < 2)
            {
                continue;
            }

            var latest = pair[0].Close;
            var previous = pair[1].Close;
            if (previous <= 0)
            {
                continue;
            }

            returns.Add((latest - previous) / previous);
        }

        if (returns.Count == 0)
        {
            return 0m;
        }

        var meanReturn = returns.Average();
        var positiveBreadth = (decimal)returns.Count(x => x > 0m) / returns.Count;

        var normalizedMean = Math.Clamp(meanReturn / 0.08m, -1m, 1m);
        var breadthSignal = (positiveBreadth * 2m) - 1m;

        var finalSignal = Math.Clamp((normalizedMean * 0.70m) + (breadthSignal * 0.30m), -1m, 1m);

        _logger.LogDebug(
            "Sector signal computed for {Sector}: Signal={Signal:N3}, MeanReturn={MeanReturn:P2}, Breadth={Breadth:P2}, Count={Count}",
            sectorName,
            finalSignal,
            meanReturn,
            positiveBreadth,
            returns.Count);

        return finalSignal;
    }
}
