/*
  # Initial Trading System Database Schema

  1. New Tables
    - `instruments`
      - Stores tradable assets (indices, stocks)
      - Supports multi-asset configuration
      - Unique instrument keys

    - `market_candles`
      - Stores OHLCV market data
      - Per instrument, per timeframe
      - Indexed for efficient queries

    - `indicator_snapshots`
      - Stores computed technical indicators
      - EMA, RSI, MACD, ADX, ATR, Bollinger, VWAP
      - Timestamped per candle

    - `trades`
      - Stores executed trades
      - Supports both options and equity
      - Complete trade lifecycle tracking

  2. Security
    - All tables use proper indexing
    - Timestamps with timezone support
    - Precision decimal handling for financial data

  3. Important Notes
    - Uses PostgreSQL-specific features
    - BIGSERIAL for high-volume data
    - Proper constraints and defaults
*/

-- Instruments Table
CREATE TABLE IF NOT EXISTS instruments (
    id SERIAL PRIMARY KEY,
    instrument_key VARCHAR(50) UNIQUE NOT NULL,
    exchange VARCHAR(20) NOT NULL,
    symbol VARCHAR(50) NOT NULL,
    instrument_type VARCHAR(10) NOT NULL,
    lot_size INTEGER NOT NULL DEFAULT 1,
    tick_size NUMERIC(18, 4) NOT NULL DEFAULT 0.05,
    is_derivatives_enabled BOOLEAN NOT NULL DEFAULT false,
    default_trading_mode VARCHAR(10) NOT NULL DEFAULT 'EQUITY',
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_instruments_symbol ON instruments(symbol);
CREATE INDEX IF NOT EXISTS idx_instruments_active ON instruments(is_active);

-- Market Candles Table
CREATE TABLE IF NOT EXISTS market_candles (
    id BIGSERIAL PRIMARY KEY,
    instrument_key VARCHAR(50) NOT NULL,
    timeframe_minutes INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    open NUMERIC(18, 4) NOT NULL,
    high NUMERIC(18, 4) NOT NULL,
    low NUMERIC(18, 4) NOT NULL,
    close NUMERIC(18, 4) NOT NULL,
    volume BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_market_candles_lookup
    ON market_candles(instrument_key, timeframe_minutes, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_market_candles_timestamp ON market_candles(timestamp DESC);

-- Indicator Snapshots Table
CREATE TABLE IF NOT EXISTS indicator_snapshots (
    id BIGSERIAL PRIMARY KEY,
    instrument_key VARCHAR(50) NOT NULL,
    timeframe_minutes INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    ema_fast NUMERIC(18, 4) NOT NULL DEFAULT 0,
    ema_slow NUMERIC(18, 4) NOT NULL DEFAULT 0,
    rsi NUMERIC(18, 4) NOT NULL DEFAULT 50,
    macd_line NUMERIC(18, 4) NOT NULL DEFAULT 0,
    macd_signal NUMERIC(18, 4) NOT NULL DEFAULT 0,
    macd_histogram NUMERIC(18, 4) NOT NULL DEFAULT 0,
    adx NUMERIC(18, 4) NOT NULL DEFAULT 0,
    plus_di NUMERIC(18, 4) NOT NULL DEFAULT 0,
    minus_di NUMERIC(18, 4) NOT NULL DEFAULT 0,
    atr NUMERIC(18, 4) NOT NULL DEFAULT 0,
    bollinger_upper NUMERIC(18, 4) NOT NULL DEFAULT 0,
    bollinger_middle NUMERIC(18, 4) NOT NULL DEFAULT 0,
    bollinger_lower NUMERIC(18, 4) NOT NULL DEFAULT 0,
    vwap NUMERIC(18, 4) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_indicator_snapshots_lookup
    ON indicator_snapshots(instrument_key, timeframe_minutes, timestamp DESC);

-- Trades Table
CREATE TABLE IF NOT EXISTS trades (
    id UUID PRIMARY KEY,
    instrument_key VARCHAR(50) NOT NULL,
    trade_type VARCHAR(20) NOT NULL,
    entry_time TIMESTAMPTZ NOT NULL,
    exit_time TIMESTAMPTZ,
    entry_price NUMERIC(18, 4) NOT NULL,
    exit_price NUMERIC(18, 4),
    quantity INTEGER NOT NULL,
    stop_loss NUMERIC(18, 4) NOT NULL,
    target NUMERIC(18, 4) NOT NULL,
    atr_at_entry NUMERIC(18, 4) NOT NULL,
    option_symbol VARCHAR(100),
    option_strike NUMERIC(18, 4),
    option_entry_price NUMERIC(18, 4),
    option_exit_price NUMERIC(18, 4),
    entry_reason TEXT NOT NULL,
    exit_reason TEXT,
    direction VARCHAR(10) NOT NULL,
    state VARCHAR(20) NOT NULL,
    pnl NUMERIC(18, 4) NOT NULL DEFAULT 0,
    pnl_percent NUMERIC(18, 4) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_trades_instrument ON trades(instrument_key);
CREATE INDEX IF NOT EXISTS idx_trades_entry_time ON trades(entry_time DESC);
CREATE INDEX IF NOT EXISTS idx_trades_state ON trades(state);

-- Seed Data: Default Instruments
INSERT INTO instruments (instrument_key, exchange, symbol, instrument_type, lot_size, tick_size, is_derivatives_enabled, default_trading_mode)
VALUES
    ('NSE:NIFTY', 'NSE', 'NIFTY', 'INDEX', 50, 0.05, true, 'OPTIONS'),
    ('NSE:BANKNIFTY', 'NSE', 'BANKNIFTY', 'INDEX', 25, 0.05, true, 'OPTIONS')
ON CONFLICT (instrument_key) DO NOTHING;

-- Comments for documentation
COMMENT ON TABLE instruments IS 'Tradable instruments configuration';
COMMENT ON TABLE market_candles IS 'Historical OHLCV market data';
COMMENT ON TABLE indicator_snapshots IS 'Computed technical indicators per candle';
COMMENT ON TABLE trades IS 'Executed trade records with full lifecycle';
