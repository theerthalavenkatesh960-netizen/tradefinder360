using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using TradingSystem.WorkerService.Jobs;

namespace TradingSystem.WorkerService.Scheduling
{
    public static class QuartzSetupExtensions
    {
        public static void AddQuartzWithSchedules(this IServiceCollection services, IConfiguration configuration)
        {
            var seederConfig = new SeederConfig();
            configuration.GetSection("Seeder").Bind(seederConfig);
            services.Configure<SeederConfig>(configuration.GetSection("Seeder"));

            services.AddQuartz(q =>
            {
                q.UsePersistentStore(store =>
                {
                    store.UsePostgres(pg =>
                    {
                        pg.ConnectionString = configuration.GetConnectionString("QuartzDb")!;
                        pg.TablePrefix = "QRTZ_";
                    });
                    store.UseNewtonsoftJsonSerializer();
                });


                var schedules = QuartzJobRegistry.GetSchedules();

                foreach (var schedule in schedules)
                {
                    var jobKey = new JobKey(schedule.JobType.Name);

                    q.AddJob(
                        schedule.JobType,
                        jobKey,
                        jobCfg => jobCfg.StoreDurably()
                    );

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

                if (seederConfig.EnableCsvSeeding)
                {
                    var csvSeederJobKey = new JobKey("CsvDataSeederJob");
                    q.AddJob<CsvDataSeederJob>(opts => opts
                        .WithIdentity(csvSeederJobKey)
                        .StoreDurably());

                    q.AddTrigger(opts => opts
                        .ForJob(csvSeederJobKey)
                        .WithIdentity("CsvDataSeederJob-trigger")
                        .StartNow()
                        .WithSimpleSchedule(x => x.WithRepeatCount(0)));
                }
            });

            services.AddQuartzHostedService(options =>
            {
                options.WaitForJobsToComplete = true;
            });
        }
    }
}
