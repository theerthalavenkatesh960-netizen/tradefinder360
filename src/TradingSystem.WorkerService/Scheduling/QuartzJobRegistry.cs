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
                    "0 0 0 1 1 ?",          // 6:30 AM IST
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")
                ),

                 new JobSchedule(
                     typeof(DailyPriceUpdateJob),
                     "0 */30 * 1 * ?",        // Every 10 minutes
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
