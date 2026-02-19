using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingSystem.WorkerService.Services
{
    public interface IStockSyncService
    {
        Task SyncStocksAsync();
    }
}
