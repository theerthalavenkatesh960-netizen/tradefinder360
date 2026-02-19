using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quartz;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services;
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

builder.Services.AddScoped<IUpstoxTokenProvider, UpstoxTokenProvider>();

builder.Services.AddHttpClient();
builder.Services.AddScoped<UpstoxClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var config = sp.GetRequiredService<UpstoxConfig>();
    var tokenProvider = sp.GetRequiredService<IUpstoxTokenProvider>();

    var httpClient = httpClientFactory.CreateClient();
    var client = new UpstoxClient(httpClient, config);

    var token = tokenProvider.GetAccessTokenAsync().GetAwaiter().GetResult();

    if (!string.IsNullOrWhiteSpace(token))
    {
        client.SetAccessToken(token);
    }

    return client;
});

builder.Services.AddScoped<IUpstoxInstrumentService, UpstoxInstrumentService>();
builder.Services.AddScoped<IUpstoxPriceService, UpstoxPriceService>();

builder.Services.AddQuartzWithSchedules(builder.Configuration);

var host = builder.Build();
host.Run();
