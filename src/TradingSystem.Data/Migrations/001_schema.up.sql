/*
  # Trading System — Full Schema (UP)

  Creates all tables required by the trading platform.

  Tables
  ------
  1. instruments           — Tradable assets (indices + stocks)
  2. market_candles        — OHLCV historical candle data
  3. indicator_snapshots   — Computed technical indicator values per candle
  4. trades                — Full trade lifecycle records
  5. scan_snapshots        — Periodic market scan results with setup scores
  6. recommendations       — Trade setup recommendations with entry/SL/target

  Seed Data
  ---------
  Default instruments: NIFTY, BANKNIFTY, RELIANCE, TCS, INFY, HDFCBANK, ICICIBANK
*/

-- ─────────────────────────────────────────────
-- 1. INSTRUMENTS
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS instruments (
    id                     SERIAL PRIMARY KEY,
    instrument_key         VARCHAR(50)   UNIQUE NOT NULL,
    exchange               VARCHAR(20)   NOT NULL,
    symbol                 VARCHAR(50)   NOT NULL,
    instrument_type        VARCHAR(10)   NOT NULL,
    lot_size               INTEGER       NOT NULL DEFAULT 1,
    tick_size              NUMERIC(18,4) NOT NULL DEFAULT 0.05,
    is_derivatives_enabled BOOLEAN       NOT NULL DEFAULT false,
    default_trading_mode   VARCHAR(10)   NOT NULL DEFAULT 'EQUITY',
    is_active              BOOLEAN       NOT NULL DEFAULT true,
    created_at             TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at             TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_instruments_symbol ON instruments(symbol);
CREATE INDEX IF NOT EXISTS idx_instruments_active  ON instruments(is_active);

INSERT INTO instruments
    (instrument_key, exchange, symbol, instrument_type, lot_size, tick_size, is_derivatives_enabled, default_trading_mode)
VALUES
    ('NSE:NIFTY',     'NSE', 'NIFTY',     'INDEX', 50, 0.05, true,  'OPTIONS'),
    ('NSE:BANKNIFTY', 'NSE', 'BANKNIFTY', 'INDEX', 25, 0.05, true,  'OPTIONS'),
    ('NSE:RELIANCE',  'NSE', 'RELIANCE',  'STOCK',  1, 0.05, true,  'EQUITY'),
    ('NSE:TCS',       'NSE', 'TCS',       'STOCK',  1, 0.05, true,  'EQUITY'),
    ('NSE:INFY',      'NSE', 'INFY',      'STOCK',  1, 0.05, true,  'EQUITY'),
    ('NSE:HDFCBANK',  'NSE', 'HDFCBANK',  'STOCK',  1, 0.05, true,  'EQUITY'),
    ('NSE:ICICIBANK', 'NSE', 'ICICIBANK', 'STOCK',  1, 0.05, true,  'EQUITY')
ON CONFLICT (instrument_key) DO NOTHING;

-- ─────────────────────────────────────────────
-- 2. MARKET CANDLES
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS market_candles (
    id                BIGSERIAL     PRIMARY KEY,
    instrument_key    VARCHAR(50)   NOT NULL,
    timeframe_minutes INTEGER       NOT NULL,
    timestamp         TIMESTAMPTZ   NOT NULL,
    open              NUMERIC(18,4) NOT NULL,
    high              NUMERIC(18,4) NOT NULL,
    low               NUMERIC(18,4) NOT NULL,
    close             NUMERIC(18,4) NOT NULL,
    volume            BIGINT        NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_market_candles_lookup
    ON market_candles(instrument_key, timeframe_minutes, timestamp DESC);

-- ─────────────────────────────────────────────
-- 3. INDICATOR SNAPSHOTS
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS indicator_snapshots (
    id                BIGSERIAL     PRIMARY KEY,
    instrument_key    VARCHAR(50)   NOT NULL,
    timeframe_minutes INTEGER       NOT NULL,
    timestamp         TIMESTAMPTZ   NOT NULL,
    ema_fast          NUMERIC(18,4) NOT NULL DEFAULT 0,
    ema_slow          NUMERIC(18,4) NOT NULL DEFAULT 0,
    rsi               NUMERIC(18,4) NOT NULL DEFAULT 50,
    macd_line         NUMERIC(18,4) NOT NULL DEFAULT 0,
    macd_signal       NUMERIC(18,4) NOT NULL DEFAULT 0,
    macd_histogram    NUMERIC(18,4) NOT NULL DEFAULT 0,
    adx               NUMERIC(18,4) NOT NULL DEFAULT 0,
    plus_di           NUMERIC(18,4) NOT NULL DEFAULT 0,
    minus_di          NUMERIC(18,4) NOT NULL DEFAULT 0,
    atr               NUMERIC(18,4) NOT NULL DEFAULT 0,
    bollinger_upper   NUMERIC(18,4) NOT NULL DEFAULT 0,
    bollinger_middle  NUMERIC(18,4) NOT NULL DEFAULT 0,
    bollinger_lower   NUMERIC(18,4) NOT NULL DEFAULT 0,
    vwap              NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_indicator_snapshots_lookup
    ON indicator_snapshots(instrument_key, timeframe_minutes, timestamp DESC);

-- ─────────────────────────────────────────────
-- 4. TRADES
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS trades (
    id                  UUID          PRIMARY KEY,
    instrument_key      VARCHAR(50)   NOT NULL,
    trade_type          VARCHAR(20)   NOT NULL,
    entry_time          TIMESTAMPTZ   NOT NULL,
    exit_time           TIMESTAMPTZ,
    entry_price         NUMERIC(18,4) NOT NULL,
    exit_price          NUMERIC(18,4),
    quantity            INTEGER       NOT NULL,
    stop_loss           NUMERIC(18,4) NOT NULL,
    target              NUMERIC(18,4) NOT NULL,
    atr_at_entry        NUMERIC(18,4) NOT NULL,
    option_symbol       VARCHAR(100),
    option_strike       NUMERIC(18,4),
    option_entry_price  NUMERIC(18,4),
    option_exit_price   NUMERIC(18,4),
    entry_reason        TEXT          NOT NULL,
    exit_reason         TEXT,
    direction           VARCHAR(10)   NOT NULL,
    state               VARCHAR(20)   NOT NULL,
    pnl                 NUMERIC(18,4) NOT NULL DEFAULT 0,
    pnl_percent         NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_trades_instrument  ON trades(instrument_key);
CREATE INDEX IF NOT EXISTS idx_trades_entry_time  ON trades(entry_time DESC);
CREATE INDEX IF NOT EXISTS idx_trades_state       ON trades(state);

-- ─────────────────────────────────────────────
-- 5. SCAN SNAPSHOTS
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS scan_snapshots (
    id              BIGSERIAL     PRIMARY KEY,
    instrument_key  VARCHAR(50)   NOT NULL,
    timestamp       TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    market_state    VARCHAR(30)   NOT NULL,
    setup_score     INTEGER       NOT NULL DEFAULT 0,
    bias            VARCHAR(10)   NOT NULL DEFAULT 'NONE',
    adx_score       INTEGER       NOT NULL DEFAULT 0,
    rsi_score       INTEGER       NOT NULL DEFAULT 0,
    ema_vwap_score  INTEGER       NOT NULL DEFAULT 0,
    volume_score    INTEGER       NOT NULL DEFAULT 0,
    bollinger_score INTEGER       NOT NULL DEFAULT 0,
    structure_score INTEGER       NOT NULL DEFAULT 0,
    last_close      NUMERIC(18,4) NOT NULL DEFAULT 0,
    atr             NUMERIC(18,4) NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_scan_snapshots_lookup
    ON scan_snapshots(instrument_key, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_scan_snapshots_score
    ON scan_snapshots(setup_score DESC, timestamp DESC);

-- ─────────────────────────────────────────────
-- 6. RECOMMENDATIONS
-- ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS recommendations (
    id                UUID          PRIMARY KEY DEFAULT gen_random_uuid(),
    instrument_key    VARCHAR(50)   NOT NULL,
    timestamp         TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    direction         VARCHAR(10)   NOT NULL,
    entry_price       NUMERIC(18,4) NOT NULL,
    stop_loss         NUMERIC(18,4) NOT NULL,
    target            NUMERIC(18,4) NOT NULL,
    risk_reward_ratio NUMERIC(8,2)  NOT NULL DEFAULT 0,
    confidence        INTEGER       NOT NULL DEFAULT 0,
    option_type       VARCHAR(10),
    option_strike     NUMERIC(18,4),
    explanation_text  TEXT          NOT NULL DEFAULT '',
    reasoning_points  JSONB         NOT NULL DEFAULT '[]',
    is_active         BOOLEAN       NOT NULL DEFAULT true,
    created_at        TIMESTAMPTZ   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    expires_at        TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_recommendations_instrument
    ON recommendations(instrument_key, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_recommendations_active
    ON recommendations(is_active, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_recommendations_confidence
    ON recommendations(confidence DESC, timestamp DESC);
