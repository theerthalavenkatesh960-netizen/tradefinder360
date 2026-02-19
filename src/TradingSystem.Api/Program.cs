using Microsoft.EntityFrameworkCore;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Repositories.Interfaces;
using TradingSystem.Data.Services;
using TradingSystem.Data.Services.Interfaces;
using TradingSystem.Scanner;
using TradingSystem.Scanner.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
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

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? "Host=localhost;Database=trading;Username=postgres;Password=postgres";

builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseNpgsql(connectionString));

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

var scannerConfig = new ScannerConfig();
builder.Configuration.GetSection("Scanner").Bind(scannerConfig);
builder.Services.AddSingleton(scannerConfig);

builder.Services.AddScoped<SetupScoringService>();
builder.Services.AddScoped<MarketScannerService>();
builder.Services.AddScoped<TradeRecommendationService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trading System API v1"));

app.UseCors();
app.MapControllers();

app.Run();
