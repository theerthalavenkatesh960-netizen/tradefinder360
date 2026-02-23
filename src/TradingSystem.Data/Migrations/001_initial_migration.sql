/*
=========================================================
 TRADING SYSTEM - FULL CLEAN MIGRATION (FINAL STATE)
=========================================================

- instruments (extended fields included)
- sectors
- market_candles (PARTITIONED by timestamp - monthly)
- instrument_prices (NORMAL table, NOT partitioned)
- indicator_snapshots
- trades
- scan_snapshots
- recommendations
- user_profiles

Includes:
- All indexes
- All RLS policies
- All triggers
- All helper functions
- All partition helpers (for market_candles ONLY)
- PROPER FOREIGN KEY CONSTRAINTS using instrument_id

=========================================================
*/

------------------------------------------------------------
-- SCHEMA + EXTENSION
------------------------------------------------------------
CREATE SCHEMA IF NOT EXISTS public;
SET search_path TO public;

CREATE EXTENSION IF NOT EXISTS pgcrypto;

------------------------------------------------------------
-- 1. SECTORS
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS sectors (
  id SERIAL PRIMARY KEY,
  name TEXT NOT NULL,
  code TEXT NOT NULL UNIQUE,
  description TEXT DEFAULT '',
  is_active BOOLEAN DEFAULT true,
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_sectors_name ON sectors(name);
CREATE UNIQUE INDEX IF NOT EXISTS idx_sectors_code ON sectors(code);

ALTER TABLE sectors ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Authenticated users can read sectors" ON sectors;
CREATE POLICY "Authenticated users can read sectors"
  ON sectors FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "Service role can insert sectors" ON sectors;
CREATE POLICY "Service role can insert sectors"
  ON sectors FOR INSERT TO authenticated WITH CHECK (true);

DROP POLICY IF EXISTS "Service role can update sectors" ON sectors;
CREATE POLICY "Service role can update sectors"
  ON sectors FOR UPDATE TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "Service role can delete sectors" ON sectors;
CREATE POLICY "Service role can delete sectors"
  ON sectors FOR DELETE TO authenticated USING (true);

CREATE OR REPLACE FUNCTION update_sectors_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_update_sectors_updated_at ON sectors;
CREATE TRIGGER trigger_update_sectors_updated_at
  BEFORE UPDATE ON sectors
  FOR EACH ROW
  EXECUTE FUNCTION update_sectors_updated_at();

------------------------------------------------------------
-- 2. INSTRUMENTS
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS instruments (
    id SERIAL PRIMARY KEY,
    instrument_key VARCHAR(50) UNIQUE NOT NULL,
    name TEXT NOT NULL DEFAULT '',
    exchange VARCHAR(20) NOT NULL,
    symbol VARCHAR(50) NOT NULL,
    instrument_type VARCHAR(10) NOT NULL,
    lot_size INTEGER NOT NULL DEFAULT 1,
    tick_size NUMERIC(18,4) NOT NULL DEFAULT 0.05,
    is_derivatives_enabled BOOLEAN NOT NULL DEFAULT false,
    default_trading_mode VARCHAR(10) NOT NULL DEFAULT 'EQUITY',
    is_active BOOLEAN NOT NULL DEFAULT true,
    sector_id INT,
    industry TEXT DEFAULT '',
    market_cap DECIMAL(18,2),
    isin TEXT DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_instruments_sector
        FOREIGN KEY (sector_id)
        REFERENCES sectors(id)
        ON DELETE SET NULL
);

CREATE INDEX IF NOT EXISTS idx_instruments_symbol ON instruments(symbol);
CREATE INDEX IF NOT EXISTS idx_instruments_active ON instruments(is_active);
CREATE INDEX IF NOT EXISTS idx_instruments_sector_id ON instruments(sector_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_instruments_key ON instruments(instrument_key);

INSERT INTO instruments
(instrument_key, exchange, symbol, name, instrument_type, lot_size, tick_size, is_derivatives_enabled, default_trading_mode)
VALUES
('NSE_INDEX|Nifty 50','NSE','NIFTY','Nifty 50','INDEX',50,0.05,true,'OPTIONS'),
('NSE_INDEX|Nifty Bank','NSE','BANKNIFTY','Nifty Bank','INDEX',25,0.05,true,'OPTIONS')
ON CONFLICT (instrument_key) DO NOTHING;

------------------------------------------------------------
-- 3. MARKET_CANDLES (PARTITIONED) - USING instrument_id FK
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS market_candles (
    id BIGSERIAL,
    instrument_id INTEGER NOT NULL,
    timeframe_minutes INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    open NUMERIC(18,4) NOT NULL,
    high NUMERIC(18,4) NOT NULL,
    low NUMERIC(18,4) NOT NULL,
    close NUMERIC(18,4) NOT NULL,
    volume BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (id, timestamp),
    CONSTRAINT fk_market_candles_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
) PARTITION BY RANGE (timestamp);

CREATE INDEX IF NOT EXISTS idx_market_candles_lookup
  ON market_candles(instrument_id, timeframe_minutes, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_market_candles_instrument
  ON market_candles(instrument_id);

------------------------------------------------------------
-- MARKET_CANDLES PARTITION HELPERS
------------------------------------------------------------

CREATE OR REPLACE FUNCTION create_market_candles_monthly_partition(
    partition_year INTEGER,
    partition_month INTEGER
) RETURNS TEXT AS $$
DECLARE
    partition_name TEXT;
    start_date DATE;
    end_date DATE;
BEGIN
    start_date := make_date(partition_year, partition_month, 1);
    end_date := start_date + INTERVAL '1 month';

    partition_name := 'market_candles_' 
                      || partition_year 
                      || '_' 
                      || LPAD(partition_month::TEXT, 2, '0');

    IF EXISTS (
        SELECT 1 
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = partition_name
        AND n.nspname = 'public'
    ) THEN
        RETURN 'Partition ' || partition_name || ' already exists';
    END IF;

    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS %I PARTITION OF market_candles
         FOR VALUES FROM (%L) TO (%L)',
        partition_name,
        start_date,
        end_date
    );

    RETURN 'Created partition: ' 
           || partition_name 
           || ' for date range [' 
           || start_date 
           || ', ' 
           || end_date 
           || ')';
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION create_next_n_months_market_candles_partitions(
    months_ahead INTEGER DEFAULT 2
)
RETURNS TABLE(result TEXT) AS $$
DECLARE
    current_month_start DATE;
    target_year INTEGER;
    target_month INTEGER;
    i INTEGER;
    partition_result TEXT;
BEGIN
    current_month_start := date_trunc('month', CURRENT_DATE)::DATE;

    FOR i IN 0..months_ahead LOOP
        target_year := EXTRACT(YEAR FROM current_month_start + (i || ' months')::INTERVAL)::INTEGER;
        target_month := EXTRACT(MONTH FROM current_month_start + (i || ' months')::INTERVAL)::INTEGER;

        SELECT create_market_candles_monthly_partition(target_year, target_month)
        INTO partition_result;

        result := partition_result;
        RETURN NEXT;
    END LOOP;

    RETURN;
END;
$$ LANGUAGE plpgsql;

SELECT create_next_n_months_market_candles_partitions(3);

------------------------------------------------------------
-- 4. INSTRUMENT_PRICES
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS instrument_prices (
  id BIGSERIAL PRIMARY KEY,
  instrument_id INTEGER NOT NULL,
  timestamp TIMESTAMPTZ NOT NULL,
  open DECIMAL(18,4) NOT NULL,
  high DECIMAL(18,4) NOT NULL,
  low DECIMAL(18,4) NOT NULL,
  close DECIMAL(18,4) NOT NULL,
  volume BIGINT NOT NULL DEFAULT 0,
  timeframe TEXT NOT NULL DEFAULT '1D',
  created_at TIMESTAMPTZ DEFAULT now(),
  updated_at TIMESTAMPTZ DEFAULT now(),
  CONSTRAINT fk_instrument_prices_instrument
    FOREIGN KEY (instrument_id)
    REFERENCES instruments(id)
    ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS idx_instrument_prices_unique
  ON instrument_prices(instrument_id, timeframe, timestamp);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_timestamp
  ON instrument_prices(timestamp);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_instrument_timeframe
  ON instrument_prices(instrument_id, timeframe);

ALTER TABLE instrument_prices ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS "Authenticated users can read instrument prices" ON instrument_prices;
CREATE POLICY "Authenticated users can read instrument prices"
  ON instrument_prices FOR SELECT TO authenticated USING (true);

DROP POLICY IF EXISTS "Authenticated users can insert instrument prices" ON instrument_prices;
CREATE POLICY "Authenticated users can insert instrument prices"
  ON instrument_prices FOR INSERT TO authenticated WITH CHECK (true);

DROP POLICY IF EXISTS "Authenticated users can update instrument prices" ON instrument_prices;
CREATE POLICY "Authenticated users can update instrument prices"
  ON instrument_prices FOR UPDATE TO authenticated USING (true) WITH CHECK (true);

DROP POLICY IF EXISTS "Authenticated users can delete instrument prices" ON instrument_prices;
CREATE POLICY "Authenticated users can delete instrument prices"
  ON instrument_prices FOR DELETE TO authenticated USING (true);

CREATE OR REPLACE FUNCTION update_instrument_prices_updated_at()
RETURNS TRIGGER AS $$
BEGIN
  NEW.updated_at = now();
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trigger_update_instrument_prices_updated_at ON instrument_prices;
CREATE TRIGGER trigger_update_instrument_prices_updated_at
  BEFORE UPDATE ON instrument_prices
  FOR EACH ROW
  EXECUTE FUNCTION update_instrument_prices_updated_at();

------------------------------------------------------------
-- 5. INDICATOR_SNAPSHOTS - USING instrument_id FK
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS indicator_snapshots (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    timeframe_minutes INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    ema_fast NUMERIC(18,4) DEFAULT 0,
    ema_slow NUMERIC(18,4) DEFAULT 0,
    rsi NUMERIC(18,4) DEFAULT 50,
    macd_line NUMERIC(18,4) DEFAULT 0,
    macd_signal NUMERIC(18,4) DEFAULT 0,
    macd_histogram NUMERIC(18,4) DEFAULT 0,
    adx NUMERIC(18,4) DEFAULT 0,
    plus_di NUMERIC(18,4) DEFAULT 0,
    minus_di NUMERIC(18,4) DEFAULT 0,
    atr NUMERIC(18,4) DEFAULT 0,
    bollinger_upper NUMERIC(18,4) DEFAULT 0,
    bollinger_middle NUMERIC(18,4) DEFAULT 0,
    bollinger_lower NUMERIC(18,4) DEFAULT 0,
    vwap NUMERIC(18,4) DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_indicator_snapshots_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_indicator_snapshots_lookup
  ON indicator_snapshots(instrument_id, timeframe_minutes, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_indicator_snapshots_instrument
  ON indicator_snapshots(instrument_id);

------------------------------------------------------------
-- 6. TRADES - USING instrument_id FK
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS trades (
    id UUID PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    trade_type VARCHAR(20) NOT NULL,
    entry_time TIMESTAMPTZ NOT NULL,
    exit_time TIMESTAMPTZ,
    entry_price NUMERIC(18,4) NOT NULL,
    exit_price NUMERIC(18,4),
    quantity INTEGER NOT NULL,
    stop_loss NUMERIC(18,4) NOT NULL,
    target NUMERIC(18,4) NOT NULL,
    atr_at_entry NUMERIC(18,4) NOT NULL,
    option_symbol VARCHAR(100),
    option_strike NUMERIC(18,4),
    option_entry_price NUMERIC(18,4),
    option_exit_price NUMERIC(18,4),
    entry_reason TEXT NOT NULL,
    exit_reason TEXT,
    direction VARCHAR(10) NOT NULL,
    state VARCHAR(20) NOT NULL,
    pnl NUMERIC(18,4) DEFAULT 0,
    pnl_percent NUMERIC(18,4) DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ,
    CONSTRAINT fk_trades_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_trades_instrument ON trades(instrument_id);
CREATE INDEX IF NOT EXISTS idx_trades_entry_time ON trades(entry_time DESC);
CREATE INDEX IF NOT EXISTS idx_trades_state ON trades(state);

------------------------------------------------------------
-- 7. SCAN_SNAPSHOTS - USING instrument_id FK
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS scan_snapshots (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    market_state VARCHAR(30) NOT NULL,
    setup_score INTEGER DEFAULT 0,
    bias VARCHAR(10) DEFAULT 'NONE',
    adx_score INTEGER DEFAULT 0,
    rsi_score INTEGER DEFAULT 0,
    ema_vwap_score INTEGER DEFAULT 0,
    volume_score INTEGER DEFAULT 0,
    bollinger_score INTEGER DEFAULT 0,
    structure_score INTEGER DEFAULT 0,
    last_close NUMERIC(18,4) DEFAULT 0,
    atr NUMERIC(18,4) DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_scan_snapshots_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_scan_snapshots_lookup
  ON scan_snapshots(instrument_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_scan_snapshots_score
  ON scan_snapshots(setup_score DESC, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_scan_snapshots_instrument
  ON scan_snapshots(instrument_id);

------------------------------------------------------------
-- 8. RECOMMENDATIONS - USING instrument_id FK
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS recommendations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    instrument_id INTEGER NOT NULL,
    timestamp TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    direction VARCHAR(10) NOT NULL,
    entry_price NUMERIC(18,4) NOT NULL,
    stop_loss NUMERIC(18,4) NOT NULL,
    target NUMERIC(18,4) NOT NULL,
    risk_reward_ratio NUMERIC(8,2) DEFAULT 0,
    confidence INTEGER DEFAULT 0,
    option_type VARCHAR(10),
    option_strike NUMERIC(18,4),
    explanation_text TEXT DEFAULT '',
    reasoning_points JSONB DEFAULT '[]',
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    expires_at TIMESTAMPTZ,
    CONSTRAINT fk_recommendations_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_recommendations_instrument
  ON recommendations(instrument_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_recommendations_active
  ON recommendations(is_active, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_recommendations_confidence
  ON recommendations(confidence DESC, timestamp DESC);

------------------------------------------------------------
-- 9. USER_PROFILES
------------------------------------------------------------

CREATE TABLE IF NOT EXISTS user_profiles (
    id UUID DEFAULT gen_random_uuid() NOT NULL,
    user_id VARCHAR(100) NOT NULL,
    upstox_access_token TEXT NULL,
    upstox_refresh_token TEXT NULL,
    token_issued_at TIMESTAMPTZ NULL,
    created_on TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_on TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT user_profiles_pkey PRIMARY KEY (id),
    CONSTRAINT user_profiles_user_id_key UNIQUE (user_id)
);

CREATE INDEX IF NOT EXISTS idx_user_profiles_updated_on ON user_profiles(updated_on DESC);
CREATE INDEX IF NOT EXISTS idx_user_profiles_user_id ON user_profiles(user_id);