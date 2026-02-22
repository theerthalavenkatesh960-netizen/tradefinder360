using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Upstox;
using TradingSystem.Upstox.Models;
using TradingSystem.Upstox.Services;
using TradingSystem.WorkerService.DataSeeders;
using TradingSystem.WorkerService.Scheduling;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var connectionString = builder.Configuration.GetConnectionString("Supabase");
builder.Services.AddDbContextFactory<TradingDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.EnableRetryOnFailure();
        });
});

builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<IInstrumentPriceRepository, InstrumentPriceRepository>();
builder.Services.AddScoped<ISectorRepository, SectorRepository>();
builder.Services.AddScoped<IMarketCandleRepository, MarketCandleRepository>();
builder.Services.AddScoped<IIndicatorService, IndicatorService>();

builder.Services.AddScoped<CsvSeedService>();

var upstoxConfig = new UpstoxConfig();
builder.Configuration.GetSection("Upstox").Bind(upstoxConfig);
builder.Services.AddSingleton(upstoxConfig);

builder.Services.AddScoped<TradingSystem.Upstox.Services.IUpstoxTokenProvider, UpstoxTokenProvider>();

builder.Services.AddHttpClient();
builder.Services.AddScoped<UpstoxClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var config = sp.GetRequiredService<UpstoxConfig>();

    var httpClient = httpClientFactory.CreateClient();
    var client = new UpstoxClient(httpClient, config);

    try
    {
        var tokenProvider = sp.GetRequiredService<TradingSystem.Upstox.Services.IUpstoxTokenProvider>();
        var token = tokenProvider.GetAccessTokenAsync().GetAwaiter().GetResult();
        if (!string.IsNullOrWhiteSpace(token))
        {
            client.SetAccessToken(token);
        }
    }
    catch (Exception ex)
    {
        var logger = sp.GetRequiredService<ILogger<UpstoxClient>>();
        logger.LogWarning(ex, "Failed to initialize UpstoxClient with stored token");
    }

    return client;
});

builder.Services.AddScoped<IUpstoxInstrumentService, UpstoxInstrumentService>();
builder.Services.AddScoped<IUpstoxPriceService, UpstoxPriceService>();

builder.Services.AddQuartzWithSchedules(builder.Configuration);

var host = builder.Build();
host.Run();
