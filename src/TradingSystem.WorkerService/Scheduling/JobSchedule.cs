namespace TradingSystem.WorkerService.Scheduling;

public class JobSchedule
{
    public Type JobType { get; }
    public string CronExpression { get; }
    public TimeZoneInfo TimeZone { get; }

    public JobSchedule(Type jobType, string cronExpression, TimeZoneInfo? tz = null)
    {
        JobType = jobType;
        CronExpression = cronExpression;
        TimeZone = tz ?? TimeZoneInfo.Utc;
    }
}
