using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Server;
using System.Text;
using TradingSystem.AI.Services;
using TradingSystem.Api.McpTools;
using TradingSystem.Api.Services;
using TradingSystem.Api.Services.Strategies;
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
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter your JWT token (without the 'Bearer ' prefix)"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt:Key is required for authentication.");
}

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "TradingSystem.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "TradingSystem.Client";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });

builder.Services.AddAuthorization();

var connectionString = builder.Configuration.GetConnectionString("Supabase");

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
            npgsql.CommandTimeout(120);
        })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
}, poolSize: 64);

builder.Services.AddDbContextFactory<TradingDbContext>(options =>
{
    options.UseNpgsql(
        connectionString,
        npgsql =>
        {
            npgsql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
            npgsql.CommandTimeout(120);
        })
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
}, ServiceLifetime.Scoped);
    
// Core Repositories
builder.Services.AddScoped(typeof(ICommonRepository<>), typeof(CommonRepository<>));
builder.Services.AddScoped<IInstrumentRepository, InstrumentRepository>();
builder.Services.AddTransient<IInstrumentPriceRepository, InstrumentPriceRepository>();
builder.Services.AddScoped<ISectorRepository, SectorRepository>();
builder.Services.AddScoped<IMarketCandleRepository, MarketCandleRepository>();
builder.Services.AddScoped<IStrategySignalRepository, StrategySignalRepository>();
builder.Services.AddScoped<IStrategyPerformanceRepository, StrategyPerformanceRepository>();
builder.Services.AddScoped<IMarketSentimentRepository, MarketSentimentRepository>();

// AI Repositories
builder.Services.AddScoped<IFeatureStoreRepository, FeatureStoreRepository>();
builder.Services.AddScoped<ITradeOutcomeRepository, TradeOutcomeRepository>();
builder.Services.AddScoped<IAIModelVersionRepository, AIModelVersionRepository>();

// Portfolio Learning Repositories
builder.Services.AddScoped<IFusionLearningConfigRepository, FusionLearningConfigRepository>();
builder.Services.AddScoped<IPortfolioPerformanceHistoryRepository, PortfolioPerformanceHistoryRepository>();

// Core Services
builder.Services.AddTransient<IInstrumentService, InstrumentService>();
builder.Services.AddTransient<ICandleService, CandleService>();
builder.Services.AddTransient<IIndicatorService, IndicatorService>();
builder.Services.AddScoped<ITradeService, TradeService>();
builder.Services.AddScoped<IScanService, ScanService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IUserProfileService, UserProfileService>();
builder.Services.AddScoped<IMarketSentimentService, MarketSentimentService>();
builder.Services.AddScoped<TradingSystem.Upstox.Services.IUpstoxTokenProvider, UpstoxTokenProvider>();

// AI Services - Basic ML
builder.Services.AddSingleton<TradePredictionService>();
builder.Services.AddScoped<AIRecommendationService>();

// AI Services - Advanced Alpha Model
builder.Services.AddScoped<MetaFactorService>();
builder.Services.AddScoped<MarketRegimeService>();
builder.Services.AddScoped<AIAlphaModelService>();
builder.Services.AddScoped<RegimeBasedPortfolioOptimizer>();

// AI Services - Self-Learning System
builder.Services.AddScoped<TradeOutcomeService>();
builder.Services.AddScoped<ModelTrainingPipeline>();
builder.Services.AddScoped<ModelPerformanceMonitor>();
builder.Services.AddScoped<ReinforcementLearningService>();

// AI Services - Portfolio Fusion Learning
builder.Services.AddScoped<PortfolioPerformanceAnalyzer>();
builder.Services.AddScoped<PortfolioLearningService>();
builder.Services.AddScoped<SignalCorrelationAnalyzer>();
builder.Services.AddScoped<SectorIntelligenceService>();
builder.Services.AddScoped<PortfolioLearningOrchestrator>();

// Scanner Services
builder.Services.AddScoped<SetupScoringService>();
builder.Services.AddTransient<MarketScannerService>();
builder.Services.AddTransient<TradeRecommendationService>();
builder.Services.AddScoped<StrategyService>();
builder.Services.AddScoped<BacktestingService>();
builder.Services.AddScoped<PortfolioOptimizationService>();
builder.Services.AddScoped<IPortfolioManagerService, PortfolioManagerService>();
builder.Services.AddScoped<INewsIngestionService, NewsIngestionService>();

// Backtest Runner
builder.Services.AddScoped<BacktestRunnerService>();
builder.Services.AddScoped<IBacktestStrategy, OrbStrategy>();
builder.Services.AddScoped<IBacktestStrategy, RsiReversalStrategy>();
builder.Services.AddScoped<IBacktestStrategy, EmaCrossoverStrategy>();
builder.Services.AddScoped<IBacktestStrategy, SmcFvgStrategy>();
builder.Services.AddScoped<BacktestStrategyRegistry>();

// Event Bus
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();

// Feature Engineering & Storage
builder.Services.AddScoped<FeatureEngineeringService>();
builder.Services.AddScoped<TrainingDatasetService>();

// Configuration
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

// Add MCP Server for Claude AI integration
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<StockAnalysisTools>();

var app = builder.Build();

app.UseExceptionHandler("/error");
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading System API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapMcp("/mcp");
app.Map("/error", (HttpContext ctx) => Results.Problem());

app.Run();
