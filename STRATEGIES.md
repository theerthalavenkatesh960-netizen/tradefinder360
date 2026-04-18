# Trading Strategies Guide

This document describes all available backtesting strategies in the platform and how to configure them.

## Overview

The backtesting platform currently supports **4 strategy families**, each with configurable modes and parameters:

1. **EMA** — Exponential Moving Average crossover-based strategies
2. **ORB** — Opening Range Breakout strategies
3. **RSI_REVERSAL** — Mean reversion based on RSI extremes
4. **SMC** — Smart Money Concepts with Fair Value Gaps and Order Blocks

---

## 1. EMA (Exponential Moving Average) Strategy Family

### What It Does
Trades EMA crossovers with multiple confirmation filters. Supports fast/slow/middle EMA configurations and switches between different entry/exit methodologies based on selected mode.

### Modes

#### CROSSOVER (Default Enhanced Mode)
**Entry Logic:**
- FastEMA crosses above SlowEMA → **LONG** entry signal
- FastEMA crosses below SlowEMA → **SHORT** entry signal
- Optional: Triple EMA stacking validation (FastEMA > MiddleEMA > SlowEMA for longs)

**Confirmation Filters** (pick ONE):
- **RSI**: Checks if RSI is in mid-zone (not overbought/oversold)
  - Long: RSI > Midline AND RSI < Overbought
  - Short: RSI < Midline AND RSI > Oversold
  
- **Volume**: Confirms with volume spike
  - Volume > (Average of last N candles × Multiplier)
  
- **Support & Resistance**: Entry near identified S/R levels
  - Identifies high/low over N candles, checks if price is within buffer %
  
- **Price Action**: Pattern recognition (Engulfing, Hammer, Doji, Morning Star)
  - Detects patterns within lookback period before entry

**Exit Logic:**
- Stop loss hit → immediate exit
- Target hit (entry ± SL × RRR) → exit
- Max holding period exceeded → close at candle close
- Opposite signal (new crossover) → exit current trade

**Best For:**
- Trending markets
- Swing trading on mid-range timeframes
- Traders wanting granular filter control

**Config Fields:**
```
FastEMA: 9-50 (default: 9)
SlowEMA: 20-200 (default: 21)
UseTripleEma: true/false (default: false)
MiddleEma: 10-100 (when UseTripleEma=true, default: 21)
EmaFilterType: RSI | VOLUME | SUPPORT_RESISTANCE | PRICE_ACTION
EmaSlType: FIXED_PERCENT | BELOW_EMA | ATR_BASED
TargetRRR: 1.5-5.0 (default: 2.0)
MaxHoldingPeriods: 1-50 (default: 10)
TradeDirection: LONG_ONLY | SHORT_ONLY | BOTH
```

---

#### PULLBACK
**Entry Logic:**
- Waits for EMA crossover signal
- Then waits for price to **pull back** towards the EMA
- Enters on pullback confirmation (prevents chasing extended moves)

**Best For:**
- Reducing drawdowns from early crossover entries
- Conservative traders
- Lower win rate but higher RR trades

---

#### SPEED
**Entry Logic:**
- Looks for **rapid/aggressive** EMA crossovers
- Enters on high-momentum crosses (large candle bodies, volume spike)
- No pullback waiting

**Best For:**
- Fast breakout traders
- Intraday scalping
- High volatility environments

---

#### PULLBACK_SPEED
**Entry Logic:**
- Hybrid mode: first waits for pullback, then requires speed confirmation
- Combines both methodologies

**Best For:**
- Balanced approach
- Medium-risk traders

---

## 2. ORB (Opening Range Breakout) Strategy Family

### What It Does
Trades breakouts from the opening range (first N minutes), with optional Fair Value Gap (FVG) and order block confirmation on retests.

### Modes

#### CLASSIC
**Entry Logic:**
1. Calculate opening range from first 5 candles (09:15–09:20 IST)
2. Define: `OrbHigh` = max high, `OrbLow` = min low
3. After 09:20, watch for breakout:
   - Close > OrbHigh + buffer → **LONG** entry signal
   - Close < OrbLow - buffer → **SHORT** entry signal
4. Add buffer (0.05%+) to reduce false breakouts
5. Require volume confirmation (volume > 1.5× avg last 10 candles)

**Exit Logic:**
- Stop loss (below/above ORB range) → immediate exit
- Target (RRR-based) → exit
- Time cutoff (after 10:30 IST) → no new entries
- Max 1–2 trades per day

**Best For:**
- Indian intraday/F&O trading
- Morning session scalpers
- High-probability, quick trades

**Config Fields:**
```
TimeFrame: 1/5/15 min (default: 5min)
RiskPercent: 0.5-5% (default: 1%)
StopLossType: FIXED_PERCENT | ATR | CANDLE
TargetType: RR_RATIO | TRAILING
IncludeOrderBlocks: true/false (ignored in CLASSIC mode)
```

---

#### FVG_RETEST
**Entry Logic:**
1. Same opening range as CLASSIC
2. Detect breakout (close > OrbHigh or < OrbLow)
3. **FVG Detection**: Identify 3-candle Fair Value Gaps
   - Bullish FVG: candle1.high < candle3.low
   - Bearish FVG: candle1.low > candle3.high
   - Min gap size: 0.1% of price
4. **Retest waiting**: Don't enter on first breakout
   - Wait for price to **retrace back into FVG zone**
5. **Engulfing confirmation**: Entry when current candle engulfs previous + closes beyond FVG
6. Volume confirmation on engulfing candle

**Exit Logic:**
- Stop loss just outside FVG zone
- Target: 3:1 RRR (from FVG bounds)
- Time cutoff: 09:20–10:30 IST
- Max 1 trade per day

**Optional Overlays:**
- **Show Order Blocks** (when `includeOrderBlocks=true`): Marks institutional order block zones in replay (visual annotation only, doesn't affect logic)

**Best For:**
- Professional/institutional-style trading
- Lower trade frequency, higher precision
- FVG-aware traders
- Replay analysis with order block visualization

**Config Fields:**
```
Same as CLASSIC +
includeOrderBlocks: true/false (replay annotation, default: false)
```

---

## 3. RSI_REVERSAL Strategy Family

### What It Does
Mean reversion strategy that enters when RSI reaches overbought/oversold levels.

### Entry Logic
- **Long**: RSI < Oversold threshold (e.g., 30)
  - Enters on RSI bounce back above threshold
- **Short**: RSI > Overbought threshold (e.g., 70)
  - Enters on RSI pullback below threshold

### Exit Logic
- Stop loss: opposite extreme (short exits near overbought, long near oversold)
- Target: RRR-based
- No time cutoff (trades any time during day)
- No trade limit

### Best For
- Range-bound/sideways markets
- Mean reversion traders
- Reversal confirmation strategies

### Config Fields
```
RsiOverbought: 50-90 (default: 70)
RsiOversold: 10-50 (default: 30)
RiskPercent: 0.5-5%
StopLossType: FIXED_PERCENT | ATR | CANDLE
TargetType: RR_RATIO | TRAILING
```

---

## 4. SMC (Smart Money Concepts) Strategy Family

### What It Does
Trades Fair Value Gaps and institutional order blocks based on order flow and smart money positioning.

### Entry Logic
1. Identify FVG formation (3-candle gap)
2. Wait for retracement into FVG zone
3. Confirm with order block alignment or engulfing candle
4. Enter on confirmation candle

### Exit Logic
- Stop loss: just outside FVG/order block zone
- Target: next FVG or swing high/low
- 3:1 RRR preferred

### Best For
- Swing traders (multiple candles to longer timeframes)
- Institutional flow traders
- Higher-quality, lower-frequency setups

### Config Fields
```
SmcMode: FVG_OB (reserved)
RiskPercent: 0.5-5%
StopLossType: FIXED_PERCENT | ATR | CANDLE
TargetType: RR_RATIO | TRAILING
TimeFrame: 15/30/60min+ (default: 5min for now)
```

---

## Configuration Guide

### Timeframe Selection (UI)
- **Intraday**: Shorter candles (1m, 5m), day-trading strategies
- **Swing**: Medium candles (15m, 30m, 60m), multi-day holds
- **Both**: Tests both methodologies

### Common Risk Management Fields (All Strategies)

| Field | Range | Description |
|-------|-------|-------------|
| **RiskPercent** | 0.5–5% | % of capital risked per trade |
| **StopLossType** | FIXED_PERCENT, ATR, CANDLE | How to calculate stop loss |
| **StopLossValue** | Strategy-dependent | Distance/multiplier for SL |
| **TargetType** | RR_RATIO, TRAILING | Exit target method |
| **RrRatio** | 1.5–5.0 | Risk:Reward ratio multiplier |
| **Timeframe** | 1, 5, 15, 30 | Candle timeframe (minutes) |

---

## Quick Selection Guide

| Scenario | Best Strategy | Mode | Reason |
|----------|---------------|------|--------|
| Trending market, filters important | **EMA** | CROSSOVER | High control, multiple confirmations |
| Fast scalp, morning breakout | **ORB** | CLASSIC | Quick, high-probability trades |
| Precision FVG trades, replay analysis | **ORB** | FVG_RETEST | Better risk/reward, visual insights |
| Ranging/sideways market | **RSI_REVERSAL** | N/A | Mean reversion on extremes |
| Swing trading, Smart Money flow | **SMC** | FVG_OB | Institutional-quality setups |
| Conservative, pullback entries | **EMA** | PULLBACK | Lower drawdown, patient entries |
| Aggressive momentum scalping | **EMA** | SPEED | Catches fast moves early |

---

## API Request Examples

### Example 1: EMA Crossover with RSI Filter
```json
{
  "strategy": {
    "name": "EMA",
    "params": {
      "timeframe": 5,
      "riskPercent": 1,
      "stopLossType": "ATR",
      "targetType": "RR_RATIO",
      "rrRatio": 2,
      "emaMode": "CROSSOVER",
      "fastEMA": 9,
      "slowEMA": 21,
      "useTripleEma": false,
      "emaFilterType": "RSI",
      "emaRsiPeriod": 14,
      "emaRsiMidline": 50,
      "rsiOverbought": 70,
      "rsiOversold": 30,
      "emaSlType": "ATR_BASED",
      "emaSlValue": 1.5,
      "tradeDirection": "BOTH"
    }
  }
}
```

### Example 2: ORB FVG Retest with Order Blocks
```json
{
  "strategy": {
    "name": "ORB",
    "params": {
      "timeframe": 5,
      "riskPercent": 1.5,
      "stopLossType": "ATR",
      "targetType": "RR_RATIO",
      "rrRatio": 3,
      "orbMode": "FVG_RETEST",
      "includeOrderBlocks": true
    }
  }
}
```

### Example 3: RSI Reversal
```json
{
  "strategy": {
    "name": "RSI_REVERSAL",
    "params": {
      "timeframe": 15,
      "riskPercent": 1,
      "stopLossType": "FIXED_PERCENT",
      "slPercent": 2,
      "targetType": "RR_RATIO",
      "rrRatio": 2.5,
      "rsiOverbought": 70,
      "rsiOversold": 30
    }
  }
}
```

---

## Performance Characteristics

### EMA Strategies
- **Win Rate**: 45–55% typically
- **Avg RR**: 1.8–2.5
- **Drawdown**: Medium (depends on filter)
- **Trade Frequency**: High (multiple trades per day)

### ORB Strategies
- **Win Rate**: 50–65% (CLASSIC), 55–70% (FVG_RETEST)
- **Avg RR**: 2.0–3.5
- **Drawdown**: Lower (tight SL)
- **Trade Frequency**: Low (1–2 trades per day)

### RSI Reversal
- **Win Rate**: 45–55%
- **Avg RR**: 1.5–2.5
- **Drawdown**: Medium-High (reversal risk)
- **Trade Frequency**: Medium

### SMC
- **Win Rate**: 55–70%
- **Avg RR**: 3.0–5.0
- **Drawdown**: Low (high RR offsets losses)
- **Trade Frequency**: Very Low (1 per day or less)

---

## Important Notes

1. **Backward Compatibility**: All 4 families are wired to the registry. Removing or renaming strategies will break the backtest API.
2. **Config Consolidation**: Old per-variant strategy classes (e.g., `EmaPullbackStrategy`, `OrbFvgRetestStrategy`) have been consolidated into mode-driven routing for maintainability.
3. **UI Sync**: The frontend strategy dropdown must always match the 4 registry keys: `EMA`, `ORB`, `RSI_REVERSAL`, `SMC`.
4. **Default Values**: All optional params have sensible defaults. Omitting a param uses its default.

---

## Future Extensions

To add a new strategy:
1. Create a new class implementing `IBacktestStrategy`
2. Register in `Program.cs` as `builder.Services.AddScoped<IBacktestStrategy, YourStrategy>()`
3. Add UI dropdown option in `BacktestControls.tsx`
4. Update `BacktestRequest` union type in `api.ts`
5. No controller changes needed—registry handles dispatch.

