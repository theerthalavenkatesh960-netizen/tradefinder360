using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace TradingSystem.WorkerService.Scheduling
{
    public static class QuartzSetupExtensions
    {
        public static void AddQuartzWithSchedules(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddQuartz(q =>
            {
                var schedules = QuartzJobRegistry.GetSchedules();

                foreach (var schedule in schedules)
                {
                    var jobKey = new JobKey(schedule.JobType.Name);

                    // Register job by Type
                    q.AddJob(
                        schedule.JobType,
                        jobKey,
                        jobCfg => jobCfg.StoreDurably()
                    );

                    // Register trigger
                    q.AddTrigger(triggerCfg => triggerCfg
                        .ForJob(jobKey)
                        .WithIdentity($"{schedule.JobType.Name}-trigger")
                        .WithCronSchedule(schedule.CronExpression, cron =>
                        {
                            cron.InTimeZone(schedule.TimeZone);
                            cron.WithMisfireHandlingInstructionFireAndProceed();
                        })
                    );
                }

                //var quartzConfig = configuration.GetSection("Quartz:PersistentStore");
                // Optional: Enable persistent store if using Postgres
                q.UsePersistentStore(store =>
                {
                    store.UsePostgres(pg =>
                    {
                        pg.ConnectionString = configuration.GetConnectionString("QuartzDb")!;
                        pg.TablePrefix =  string.Empty;//quartzConfig.GetValue<string>("TablePrefix") ?? "Swing_";
                    });

                    store.UseNewtonsoftJsonSerializer();   // For Quartz 3.15 – not obsolete
                });
            });

            // Starts Quartz HostedService
            services.AddQuartzHostedService(options =>
            {
                options.WaitForJobsToComplete = true;
            });
        }
    }
}
