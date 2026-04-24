# Professional Intraday Options Trading System

A comprehensive, production-ready **intraday options trading algorithm** built in **.NET Core 8.0**. This system implements a robust trend-pullback strategy for NIFTY options with complete modularity, risk management, and observability.

## TradeFinder360 🚀

> **AI-Powered Algorithmic Trading System with Self-Learning Capabilities**

TradeFinder360 is an enterprise-grade algorithmic trading platform that combines traditional technical analysis with cutting-edge machine learning to generate high-probability trade signals. The system features continuous learning, adaptive market regime detection, and automated model retraining.

---

## Architecture Overview

This trading system follows a **multi-layered, modular architecture** designed for professional trading applications:

```
┌─────────────────────────────────────────────────────────────┐
│                    Trading Engine                           │
│  (Orchestrates all components and manages trade lifecycle)  │
└──────────────────┬──────────────────────────────────────────┘
                   │
        ┌──────────┴──────────┐
        │                     │
┌───────▼────────┐   ┌────────▼─────────┐
│  Market Data   │   │  Configuration   │
│    Engine      │   │     Manager      │
└───────┬────────┘   └──────────────────┘
        │
┌───────▼────────┐   ┌─────────────────┐
│   Indicator    │   │  Market State   │
│    Engine      │───│     Engine      │
└───────┬────────┘   └────────┬────────┘
        │                     │
┌───────▼────────┐   ┌────────▼─────────┐
│   Strategy     │   │   Risk Engine    │
│    Engine      │───│                  │
└───────┬────────┘   └────────┬─────────┘
        │                     │
┌───────▼────────────────────▼─────────┐
│        Execution Engine               │
│    (Options Selection & Orders)       │
└───────────────┬───────────────────────┘
                │
        ┌───────┴────────┐
        │                │
┌───────▼────────┐  ┌────▼──────────┐
│    Logging     │  │   Database    │
│    System      │  │  (Supabase)   │
└────────────────┘  └───────────────┘
```

## Project Structure

```
TradingSystem/
├── TradingSystem.sln
├── src/
│   ├── TradingSystem.Core/              # Core domain models
│   │   └── Models/
│   │       ├── Candle.cs
│   │       ├── Tick.cs
│   │       ├── Trade.cs
│   │       ├── Option.cs
│   │       └── MarketState.cs
│   │
│   ├── TradingSystem.Configuration/     # Configuration management
│   │   ├── Models/
│   │   │   └── TradingConfig.cs
│   │   └── ConfigurationManager.cs
│   │
│   ├── TradingSystem.MarketData/        # Candle building & storage
│   │   ├── CandleBuilder.cs
│   │   ├── CandleStore.cs
│   │   └── MarketDataEngine.cs
│   │
│   ├── TradingSystem.Indicators/        # Technical indicators (all custom)
│   │   ├── EMA.cs
│   │   ├── RSI.cs
│   │   ├── MACD.cs
│   │   ├── ADX.cs
│   │   ├── ATR.cs
│   │   ├── BollingerBands.cs
│   │   ├── VWAP.cs
│   │   ├── TimeframeScaler.cs
│   │   └── IndicatorEngine.cs
│   │
│   ├── TradingSystem.MarketState/       # Trend/sideways detection
│   │   ├── StructureAnalyzer.cs
│   │   └── MarketStateEngine.cs
│   │
│   ├── TradingSystem.Strategy/          # Entry logic
│   │   ├── Models/
│   │   │   └── EntrySignal.cs
│   │   ├── PullbackDetector.cs
│   │   └── StrategyEngine.cs
│   │
│   ├── TradingSystem.Risk/              # Risk management
│   │   ├── Models/
│   │   │   └── RiskParameters.cs
│   │   └── RiskEngine.cs
│   │
│   ├── TradingSystem.Execution/         # Options execution
│   │   ├── Interfaces/
│   │   │   └── IBrokerAdapter.cs
│   │   ├── OptionsSelector.cs
│   │   ├── ExecutionEngine.cs
│   │   └── MockBrokerAdapter.cs
│   │
│   ├── TradingSystem.Data/              # Database persistence
│   │   ├── Models/
│   │   │   └── TradeRecord.cs
│   │   ├── SupabaseRepository.cs
│   │   └── DatabaseSchema.sql
│   │
│   ├── TradingSystem.Logging/           # Structured logging
│   │   └── TradingLogger.cs
│   │
│   └── TradingSystem.Engine/            # Main orchestrator
│       ├── TradeManager.cs
│       ├── TradingEngine.cs
│       ├── Program.cs
│       ├── appsettings.json
│       └── appsettings.5min.json
│
└── README.md
```

## Key Features

### 1. Timeframe-Agnostic Design
- **Default**: 15-minute candles
- **Switchable**: Change to 5-minute in config without code changes
- **Auto-scaling**: All indicators automatically adjust their periods based on timeframe multiplier

### 2. Complete Indicator Suite (Built from Scratch)
- ✅ EMA (Fast & Slow)
- ✅ RSI
- ✅ MACD (with Signal & Histogram)
- ✅ ADX (with +DI & -DI)
- ✅ ATR
- ✅ Bollinger Bands
- ✅ VWAP

### 3. Intelligent Market State Detection
- **SIDEWAYS**: Weak ADX, choppy price, narrow bands → NO TRADES
- **TRENDING_BULLISH**: Strong ADX, price above EMAs/VWAP, bullish structure
- **TRENDING_BEARISH**: Strong ADX, price below EMAs/VWAP, bearish structure

### 4. Professional Entry Strategy
- **Type**: Trend Pullback Entry
- **Requirements**:
  - Market must be trending (ADX > 25)
  - Pullback to EMA Fast or Bollinger middle band
  - Lower volume during pullback
  - Strong entry candle in trend direction

### 5. Risk Management
- **ATR-based stops**: 1.5× ATR
- **ATR-based targets**: 2× ATR (Risk:Reward = 1:1.33)
- **Multiple exit conditions**:
  - Stop loss / Target hit
  - RSI crossover (50 level)
  - Price breaks EMA Slow against trend
  - MACD crosses zero line
- **Daily limits**:
  - Max trades per day (configurable)
  - Max daily loss threshold
  - Cooldown after consecutive losses

### 6. Options Execution
- Always trades **ATM (At-The-Money)** options
- Nearest weekly expiry
- Direction:
  - Bullish trend → Buy CALL
  - Bearish trend → Buy PUT
- Broker-agnostic interface (easy to integrate with any broker API)

### 7. Data Persistence
- **Supabase integration** for PostgreSQL storage
- Tracks:
  - All trades with entry/exit details
  - Candle history
  - Market state transitions
- Complete audit trail

### 8. Comprehensive Logging
- Structured JSON logging with Serilog
- Logs:
  - Every candle with OHLCV
  - All indicator values
  - Market state changes
  - Entry/exit signals with reasons
  - Risk checks
  - Trade P&L

### 9. AI-Powered Features (TradeFinder360)
- **AI Alpha Model**: Generates high-probability trade signals using machine learning.
- **Market Regime Detection**: Identifies various market states for better decision-making.
- **Continuous Learning**: The system adapts and improves over time based on trade outcomes.
- **Portfolio Optimization**: Dynamic allocation based on market conditions and risk.

## Setup Instructions

### Prerequisites
- **.NET 8.0 SDK**
- **Supabase account** (free tier works)
- **Upstox trading account** (for live trading)

### 1. Clone and Build

```bash
# Navigate to project directory
cd TradingSystem

# Restore NuGet packages
dotnet restore

# Build solution
dotnet build
```

### 2. Configure Supabase

1. Create a Supabase project at [supabase.com](https://supabase.com)
2. Run the SQL schema from `src/TradingSystem.Data/DatabaseSchema.sql` in Supabase SQL Editor
3. Get your Supabase URL and Anon Key from Project Settings → API

### 3. Update Configuration

Edit `src/TradingSystem.Engine/appsettings.json`:

```json
{
  "Trading": {
    "Database": {
      "SupabaseUrl": "https://your-project.supabase.co",
      "SupabaseKey": "your-anon-key",
      "EnablePersistence": true
    }
  }
}
```

### 4. Configure Upstox API

Edit `src/TradingSystem.Api/appsettings.json`:

```json
{
  "Upstox": {
    "ApiKey": "your-api-key",
    "ApiSecret": "your-api-secret",
    "RedirectUri": "http://localhost:5000/callback"
  }
}
```

### 5. Run Database Migrations

```bash
cd src/TradingSystem.Data
dotnet ef database update
```

### 6. Run the System

```bash
cd src/TradingSystem.Engine
dotnet run
```

### 7. Access Swagger UI

Navigate to: `http://localhost:5000`

### 8. Claude MCP Integration (Optional)

1. Start the API project so MCP tools are available at `http://localhost:61577/mcp`.
2. Copy `claude_desktop_config.json` from this folder into your Claude Desktop config location.

Windows: `%APPDATA%\\Claude\\claude_desktop_config.json`

macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`

Linux: `~/.config/Claude/claude_desktop_config.json`

3. Restart Claude Desktop.
4. Confirm these tools are listed from the TradeFinder MCP server:
  - `GetOhlcData`
  - `GetIndicatorSnapshot`
  - `GetSupportResistanceLevels`
  - `GetStockSummary`
5. For the web app panel, configure Anthropic on the API server (not in the browser):
  - Set `Anthropic:ApiKey` in `src/TradingSystem.Api/appsettings.json` for local-only testing, or
  - Set environment variable `Anthropic__ApiKey`
  - Set `AiProvider:Active = Anthropic`
  - Set `AiProvider:AnthropicModel = claude-opus-4-5`

### 9. Free Local AI Testing with Ollama (No Anthropic Key)

You can run AI analysis locally before paying for Anthropic:

1. Install Ollama and run:
  - `ollama pull llama3.1:8b-instruct`
  - `ollama serve`
2. In `src/TradingSystem.Api/appsettings.json`, set:
  - `AiProvider:Active` = `Ollama`
  - `AiProvider:OllamaUseBestAvailable` = `true`
  - `AiProvider:OllamaModel` = fallback model if no preferred model is installed
  - `AiProvider:OllamaBaseUrl` = `http://localhost:11434`
  - Optional: tune `AiProvider:OllamaBestModelPriority` for your machine
3. Start the API and SPA normally.
4. Use the Stock Analysis panel; the same endpoint now calls local Ollama through the backend proxy.

To switch back to Anthropic later:

1. Set `AiProvider:Active` = `Anthropic`
2. Configure `Anthropic__ApiKey` (recommended) or `Anthropic:ApiKey` in appsettings
3. Set `AiProvider:AnthropicModel` to your preferred Claude model

## Configuration Guide

### Switching Timeframes

**For 15-minute trading** (default):
```json
{
  "Trading": {
    "Timeframe": {
      "ActiveTimeframeMinutes": 15,
      "BaseTimeframeMinutes": 15
    },
    "Limits": {
      "MaxTradesPerDay": 3
    }
  }
}
```

**For 5-minute trading**:
```json
{
  "Trading": {
    "Timeframe": {
      "ActiveTimeframeMinutes": 5,
      "BaseTimeframeMinutes": 15
    },
    "Limits": {
      "MaxTradesPerDay": 2
    }
  }
}
```

The system automatically scales all indicators:
- 15-min: EMA Fast = 20, EMA Slow = 50, RSI = 14
- 5-min: EMA Fast = 60, EMA Slow = 150, RSI = 42

### Risk Parameters

```json
{
  "Risk": {
    "StopLossATRMultiplier": 1.5,      // SL distance
    "TargetATRMultiplier": 2.0,         // Target distance
    "MaxDailyLossAmount": 10000,        // Max loss per day
    "CooldownMinutesAfterLoss": 30      // Wait time after loss
  }
}
```

### Market State Thresholds

```json
{
  "MarketState": {
    "SidewaysAdxThreshold": 20,         // Below this = sideways
    "TrendingAdxThreshold": 25,         // Above this = trending
    "BullishRsiThreshold": 55,          // RSI for bullish
    "BearishRsiThreshold": 45           // RSI for bearish
  }
}
```

## Integration with Real Broker

The system uses a broker-agnostic interface. To integrate with your broker:

1. Implement `IBrokerAdapter` interface:

```csharp
public class YourBrokerAdapter : IBrokerAdapter
{
    public async Task<List<Option>> GetOptionChain(string underlying, DateTime? expiry = null)
    {
        // Call your broker's API
    }

    public async Task<string> PlaceOrder(Option option, int quantity, string orderType)
    {
        // Place order via broker API
    }

    // Implement other methods...
}
```

2. Replace `MockBrokerAdapter` in `TradingEngine.cs`:

```csharp
IBrokerAdapter broker = new YourBrokerAdapter();
_execution = new ExecutionEngine(broker, _config.Execution);
```

## Testing & Backtesting

The system includes a market data simulator for testing:

```csharp
// Program.cs includes a simulation loop
await SimulateMarketData(engine);
```

For backtesting with historical data:
1. Load historical candles from CSV/database
2. Feed them to `engine.ProcessCandle(candle)`
3. Analyze trades from Supabase database

## Monitoring & Observability

### Logs Location
- Console output (real-time)
- `logs/trading-{date}.log` (JSON format)

### Database Tables
- `trades`: All trade details
- `candles`: Historical price data
- `market_states`: Market condition history

### Key Metrics to Monitor
- Daily trade count
- Win rate
- Average R-multiple
- Max drawdown
- ADX trends
- Market state distribution

## Production Deployment Checklist

- [ ] Set up proper Supabase RLS policies
- [ ] Configure production database credentials
- [ ] Set up monitoring and alerting
- [ ] Test with paper trading first
- [ ] Implement position sizing based on account size
- [ ] Add order confirmations and validations
- [ ] Set up backup and disaster recovery
- [ ] Document broker-specific integration
- [ ] Test network failure scenarios
- [ ] Configure logging levels for production

## System Principles

1. **All decisions based on SPOT price**, not option charts
2. **Options are execution instruments only**
3. **Trend confirmation before entry** (no prediction)
4. **Aggressive sideways market avoidance**
5. **Capital protection is priority #1**
6. **Configuration over hardcoding**
7. **Each responsibility in separate module**

## Performance Characteristics

- **Memory**: ~50-100 MB for 200 candles in memory
- **CPU**: Minimal (indicator calculations are O(1) per candle)
- **Latency**: < 10ms for signal generation
- **Scalability**: Can handle multiple instruments in parallel

## License

This is a professional trading system. Use at your own risk. Not financial advice.

## Support & Contributions

For issues, enhancements, or integration questions, refer to the modular architecture to identify the relevant component.

---

## TradeFinder360 Features

### AI-Powered Features
- **AI Alpha Model**: Generates high-probability trade signals using machine learning.
- **Market Regime Detection**: Identifies various market states for better decision-making.
- **Continuous Learning**: The system adapts and improves over time based on trade outcomes.
- **Portfolio Optimization**: Dynamic allocation based on market conditions and risk.

---

**Built with:** .NET Core 8.0 | Serilog | Supabase | PostgreSQL | ML.NET

**Strategy Type:** Trend Pullback | ATR-based Risk Management | AI-Powered Trading

**Target Market:** NIFTY Options (easily adaptable to other markets)
