using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingSystem.WorkerService.Jobs
{
    [DisallowConcurrentExecution]
    public class UpdateStocksJob : IJob
    {
        private readonly ILogger<UpdateStocksJob> _logger;
        private readonly IStockSyncService _stockSyncService;

        public UpdateStocksJob(
            ILogger<UpdateStocksJob> logger,
            IStockSyncService stockSyncService)
        {
            _logger = logger;
            _stockSyncService = stockSyncService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Stock Update Job Started at {time}", DateTime.UtcNow);

            try
            {
                await _stockSyncService.SyncStocksAsync();

                _logger.LogInformation("Stock Update Job Completed Successfully at {time}", DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stock Update Job Failed at {time}", DateTime.UtcNow);
                throw;
            }
        }
    }
}
