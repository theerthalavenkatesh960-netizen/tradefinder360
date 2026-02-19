using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Upstox;
using TradingSystem.Upstox.Models;
using TradingSystem.Upstox.Services;
using TradingSystem.WorkerService.Jobs;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("Database connection string not found");

builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<IInstrumentPriceRepository, InstrumentPriceRepository>();

var upstoxConfig = new UpstoxConfig();
builder.Configuration.GetSection("Upstox").Bind(upstoxConfig);
builder.Services.AddSingleton(upstoxConfig);

builder.Services.AddHttpClient<UpstoxClient>();
builder.Services.AddScoped<UpstoxClient>();
builder.Services.AddScoped<IUpstoxInstrumentService, UpstoxInstrumentService>();
builder.Services.AddScoped<IUpstoxPriceService, UpstoxPriceService>();

builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();

    var instrumentSyncJobKey = new JobKey("InstrumentSyncJob");
    q.AddJob<InstrumentSyncJob>(opts => opts.WithIdentity(instrumentSyncJobKey));
    q.AddTrigger(opts => opts
        .ForJob(instrumentSyncJobKey)
        .WithIdentity("InstrumentSyncJob-trigger")
        .WithCronSchedule("0 0 2 * * ?"));

    var dailyPriceJobKey = new JobKey("DailyPriceUpdateJob");
    q.AddJob<DailyPriceUpdateJob>(opts => opts.WithIdentity(dailyPriceJobKey));
    q.AddTrigger(opts => opts
        .ForJob(dailyPriceJobKey)
        .WithIdentity("DailyPriceUpdateJob-trigger")
        .WithCronSchedule("0 30 18 * * ?"));
});

builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

var host = builder.Build();
host.Run();
