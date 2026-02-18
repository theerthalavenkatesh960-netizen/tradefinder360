using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingSystem.Configuration;
using TradingSystem.Configuration.Models;
using TradingSystem.Core.Models;
using TradingSystem.Data;
using TradingSystem.Data.Repositories;
using TradingSystem.Data.Services;
using TradingSystem.Upstox;
using TradingSystem.Engine;

Console.WriteLine("=== MULTI-ASSET TRADING SYSTEM ===");
Console.WriteLine("Professional Algorithmic Trading Platform");
Console.WriteLine("=========================================\n");

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

var services = new ServiceCollection();

var tradingConfig = new TradingConfig();
configuration.GetSection("Trading").Bind(tradingConfig);

services.AddSingleton(tradingConfig);
services.AddSingleton(tradingConfig.Instrument);
services.AddSingleton(tradingConfig.Timeframe);
services.AddSingleton(tradingConfig.Indicators);
services.AddSingleton(tradingConfig.Risk);
services.AddSingleton(tradingConfig.Limits);
services.AddSingleton(tradingConfig.MarketState);
services.AddSingleton(tradingConfig.Execution);

var connectionString = tradingConfig.Database.ConnectionString;
if (string.IsNullOrEmpty(connectionString))
{
    connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
        ?? "Host=localhost;Database=trading;Username=postgres;Password=postgres";
}

services.AddDbContext<TradingDbContext>(options =>
    options.UseNpgsql(connectionString));

services.AddScoped<IInstrumentRepository, InstrumentRepository>();
services.AddScoped<ICandleRepository, CandleRepository>();
services.AddScoped<IIndicatorRepository, IndicatorRepository>();
services.AddScoped<ITradeRepository, TradeRepository>();
services.AddScoped<TradingDataService>();

services.AddHttpClient<UpstoxClient>();
services.AddSingleton(new Upstox.Models.UpstoxConfig
{
    ApiKey = tradingConfig.Upstox.ApiKey,
    ApiSecret = tradingConfig.Upstox.ApiSecret,
    AccessToken = tradingConfig.Upstox.AccessToken,
    BaseUrl = tradingConfig.Upstox.BaseUrl,
    MaxRetries = tradingConfig.Upstox.MaxRetries,
    RetryDelayMs = tradingConfig.Upstox.RetryDelayMs,
    RateLimitPerSecond = tradingConfig.Upstox.RateLimitPerSecond
});
services.AddSingleton<UpstoxMarketDataService>();

var serviceProvider = services.BuildServiceProvider();

Console.WriteLine($"Active Instrument: {tradingConfig.Instrument.ActiveInstrumentKey}");
Console.WriteLine($"Trading Mode: {tradingConfig.Instrument.TradingMode}");
Console.WriteLine($"Timeframe: {tradingConfig.Timeframe.ActiveTimeframeMinutes} minutes");
Console.WriteLine($"Database: PostgreSQL (EF Core)");
Console.WriteLine($"Data Source: Upstox API");
Console.WriteLine();

TradingInstrument? activeInstrument = null;

using (var scope = serviceProvider.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
    try
    {
        await dbContext.Database.CanConnectAsync();
        Console.WriteLine("Database connection: SUCCESS");

        var instrumentRepo = scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
        activeInstrument = await instrumentRepo.GetByKeyAsync(tradingConfig.Instrument.ActiveInstrumentKey);

        if (activeInstrument == null)
        {
            Console.WriteLine($"Instrument '{tradingConfig.Instrument.ActiveInstrumentKey}' not found in database.");
            Console.WriteLine("Run the SQL migration script to seed default instruments:");
            Console.WriteLine("  psql -d trading -f src/TradingSystem.Data/Migrations/001_InitialSchema.sql");
            return;
        }

        Console.WriteLine($"Instrument loaded: {activeInstrument.GetDisplayName()} | Lot: {activeInstrument.LotSize} | Mode: {activeInstrument.DefaultTradingMode}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database connection: FAILED - {ex.Message}");
        Console.WriteLine("Please ensure PostgreSQL is running and the connection string is correct.");
        Console.WriteLine("You can run the SQL migration script at: src/TradingSystem.Data/Migrations/001_InitialSchema.sql");
        return;
    }
}

using var engineScope = serviceProvider.CreateScope();
var dataService = engineScope.ServiceProvider.GetRequiredService<TradingDataService>();
var engine = new TradingEngine(tradingConfig, dataService, activeInstrument!);

Console.WriteLine("\nEngine initialized successfully!");
Console.WriteLine("System is ready for production trading.\n");

Console.WriteLine("To start live trading:");
Console.WriteLine("1. Ensure Upstox API credentials are configured");
Console.WriteLine("2. Set the active instrument in appsettings.json");
Console.WriteLine("3. Uncomment and implement live data feed in TradingEngine");
Console.WriteLine("\nPress Ctrl+C to stop.");

await Task.Delay(Timeout.Infinite);
