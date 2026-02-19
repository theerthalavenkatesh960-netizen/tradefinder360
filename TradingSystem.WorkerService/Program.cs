using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Upstox;
using TradingSystem.Upstox.Models;
using TradingSystem.Upstox.Services;
using TradingSystem.WorkerService.DataSeeders;
using TradingSystem.WorkerService.Jobs;
using TradingSystem.WorkerService.Scheduling;

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new InvalidOperationException("Database connection string not found");

builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<IInstrumentPriceRepository, InstrumentPriceRepository>();
builder.Services.AddScoped<ISectorRepository, SectorRepository>();

builder.Services.AddScoped<CsvSeedService>();

var upstoxConfig = new UpstoxConfig();
builder.Configuration.GetSection("Upstox").Bind(upstoxConfig);
builder.Services.AddSingleton(upstoxConfig);

builder.Services.AddHttpClient<UpstoxClient>();
builder.Services.AddScoped<UpstoxClient>();
builder.Services.AddScoped<IUpstoxInstrumentService, UpstoxInstrumentService>();
builder.Services.AddScoped<IUpstoxPriceService, UpstoxPriceService>();

builder.Services.AddQuartzWithSchedules(builder.Configuration);

var host = builder.Build();
host.Run();
