using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Upstox.Constants;
using TradingSystem.WorkerService.Services;

public class StockSyncService : IStockSyncService
{
    private readonly ILogger<StockSyncService> _logger;
    private readonly IUpstoxService _upstoxService;
    private readonly ICommonRepository<StockPrice> _repository;
    private readonly SwingLyneDbContext _db;
    public StockSyncService(
         ILogger<StockSyncService> logger,
         IUpstoxService upstoxService,
         ICommonRepository<StockPrice> repository,
         SwingLyneDbContext db)
    {
        _logger = logger;
        _upstoxService = upstoxService;
        _repository = repository;
        _db = db;
    }
    public async Task SyncStocksAsync()
    {
       // _logger.LogInformation("Fetching stocks...");

        var keys = Constants.AllStocksInstrumentKeys
            .Split(',')
            .Distinct()
            .ToList();

        const int batchSize = 500;
        var allPrices = new List<Instr>();

        for (int i = 0; i < keys.Count; i += batchSize)
        {
            var batch = keys.Skip(i).Take(batchSize).ToList();

            var prices = await _upstoxService.GetStockPricesAsync(batch);

            if (prices is null)
                continue;

            allPrices.AddRange(prices.Select(p => new StockPrice
            {
                StockId = p.StockId,
                CurrentPrice = p.CurrentPrice,
                PriceDate = p.PriceDate,
                Open = p.Open,
                High = p.High,
                Low = p.Low,
                Close = p.Close,
                Volume = p.Volume
            }));
        }

        _logger.LogInformation("Total stocks received: {count}", allPrices.Count);

        await UpsertStockPricesAsync(allPrices);

        _logger.LogInformation("Stock prices synced successfully");
    }

    private async Task UpsertStockPricesAsync(List<StockPrice> prices)
    {
        if (!prices.Any())
            return;

        var stockIds = prices.Select(x => x.StockId).ToList();

        var existing = await _db.StockPrice
            .Where(x => stockIds.Contains(x.StockId))
            .ToDictionaryAsync(x => x.StockId);

        var newEntities = new List<StockPrice>();
        var updateEntities = new List<StockPrice>();

        foreach (var price in prices)
        {
            if (existing.TryGetValue(price.StockId, out var entity))
            {
                entity.CurrentPrice = price.CurrentPrice;
                entity.PriceDate = price.PriceDate;
                entity.Open = price.Open;
                entity.High = price.High;
                entity.Low = price.Low;
                entity.Close = price.Close;
                entity.AdjustedClose = price.AdjustedClose;
                entity.Volume = price.Volume;

                updateEntities.Add(entity);
            }
            else
            {
                newEntities.Add(price);
            }
        }

        if (newEntities.Any())
            await _repository.InsertBulkAsync(newEntities);

        if (updateEntities.Any())
            await _repository.UpdateBulkAsync(updateEntities);

        await _repository.SaveAsync();
    }
}
