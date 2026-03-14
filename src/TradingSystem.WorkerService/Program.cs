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

// CRITICAL FIX: Configure DbContext with proper pooling for high concurrency
builder.Services.AddDbContextPool<TradingDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsql.CommandTimeout(120); // 2 min timeout for bulk operations
            npgsql.MaxBatchSize(100); // Optimize batch inserts
        })
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Performance boost
}, poolSize: 128); // Support up to 128 concurrent contexts

// Keep factory for jobs that need multiple contexts
builder.Services.AddDbContextFactory<TradingDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            npgsql.CommandTimeout(120);
            npgsql.MaxBatchSize(100);
        })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<IInstrumentPriceRepository, InstrumentPriceRepository>();
builder.Services.AddScoped<ISectorRepository, SectorRepository>();
builder.Services.AddScoped<IMarketCandleRepository, MarketCandleRepository>();
builder.Services.AddScoped<IIndicatorService, IndicatorService>();
builder.Services.AddScoped<IMarketSentimentRepository, MarketSentimentRepository>();
builder.Services.AddScoped<IMarketSentimentService, MarketSentimentService>();

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
