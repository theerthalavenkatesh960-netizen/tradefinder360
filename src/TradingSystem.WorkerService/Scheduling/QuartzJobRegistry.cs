using TradingSystem.WorkerService.Jobs;

namespace TradingSystem.WorkerService.Scheduling
{
    public static class QuartzJobRegistry
    {
        public static List<JobSchedule> GetSchedules()
        {
            return new()
            {
                new JobSchedule(
                    typeof(InstrumentSyncJob),
                    "0 0 0 1 1 ?",          // Once on Jan 1st
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")
                ),

                new JobSchedule(
                    typeof(DailyPriceUpdateJob),
                    "0 30 16 ? * MON-FRI",        // Every 2 minutes
                    TimeZoneInfo.Utc
                ),

                new JobSchedule(
                    typeof(MarketCandlesUpdateJob),
                    "0 */2 * 1 * ?",        // Every 4 minutes
                    TimeZoneInfo.Utc
                ),

                new JobSchedule(
                    typeof(IndicatorSnapshotsUpdateJob),
                    "0 */10 * 1 * ?",       // Every 10 minutes (after candles are loaded)
                    TimeZoneInfo.Utc
                ),

                new JobSchedule(
                    typeof(PartitionMaintenanceJob),
                    "0 0 2 28 * ?",         // 2:00 AM on the 28th of every month
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")
                )
            };
        }
    }
}
