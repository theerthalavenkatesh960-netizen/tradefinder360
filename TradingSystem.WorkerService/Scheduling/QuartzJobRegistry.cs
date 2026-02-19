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
                    "0 0/2 * * * ?",          // 6:30 AM IST
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")
                ),

                 new JobSchedule(
                     typeof(DailyPriceUpdateJob),
                     "0 */10 * * * ?",        // Every 10 minutes
                     TimeZoneInfo.Utc
                 )
            };
        }
    }
}
