using Microsoft.EntityFrameworkCore;
using TradingSystem.AI.Services;
using TradingSystem.Core.Events;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;
using TradingSystem.Scanner.Services;
using TradingSystem.Upstox;
using TradingSystem.Upstox.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()    
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Trading System API", Version = "v1" });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddDbContext<TradingDbContext>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Supabase"),
        npgsql =>
        {
            npgsql.EnableRetryOnFailure();
        });
});

builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddScoped<IInstrumentPriceRepository, InstrumentPriceRepository>();
builder.Services.AddScoped<ISectorRepository, SectorRepository>();

builder.Services.AddScoped<IInstrumentService, InstrumentService>();
builder.Services.AddScoped<ICandleService, CandleService>();
builder.Services.AddScoped<IIndicatorService, IndicatorService>();
builder.Services.AddScoped<ITradeService, TradeService>();
builder.Services.AddScoped<IScanService, ScanService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IMarketSentimentService, MarketSentimentService>();
builder.Services.AddScoped<TradingSystem.Upstox.Services.IUpstoxTokenProvider, UpstoxTokenProvider>();

// AI Services
builder.Services.AddSingleton<TradePredictionService>();
builder.Services.AddScoped<AIRecommendationService>();

var scannerConfig = new ScannerConfig();
builder.Configuration.GetSection("Scanner").Bind(scannerConfig);
builder.Services.AddSingleton(scannerConfig);

var upstoxConfig = new UpstoxConfig();
builder.Configuration.GetSection("Upstox").Bind(upstoxConfig);
builder.Services.AddSingleton(upstoxConfig);

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
    catch
    {
    }

    return client;
});

builder.Services.AddScoped<SetupScoringService>();
builder.Services.AddScoped<MarketScannerService>();
builder.Services.AddScoped<TradeRecommendationService>();
builder.Services.AddScoped<IMarketCandleRepository, MarketCandleRepository>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddScoped<BacktestingService>();
builder.Services.AddScoped<PortfolioOptimizationService>();
builder.Services.AddScoped<IStrategySignalRepository, StrategySignalRepository>();
builder.Services.AddScoped<IStrategyPerformanceRepository, StrategyPerformanceRepository>();
builder.Services.AddScoped<IMarketSentimentRepository, MarketSentimentRepository>();

// Event Bus
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();

// Feature Engineering & Storage
builder.Services.AddScoped<IFeatureStoreRepository, FeatureStoreRepository>();
builder.Services.AddScoped<FeatureEngineeringService>();
builder.Services.AddScoped<TrainingDatasetService>();

var app = builder.Build();

app.UseExceptionHandler("/error");
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading System API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors();
app.MapControllers();
app.Map("/error", (HttpContext ctx) => Results.Problem());

app.Run();
