-- Trading System Database Schema for Supabase

-- Trades Table
CREATE TABLE IF NOT EXISTS trades (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    entry_time TIMESTAMPTZ NOT NULL,
    exit_time TIMESTAMPTZ,
    direction TEXT NOT NULL CHECK (direction IN ('CALL', 'PUT')),
    state TEXT NOT NULL CHECK (state IN ('WAIT', 'READY', 'IN_TRADE', 'EXITED')),
    spot_entry_price DECIMAL(10, 2) NOT NULL,
    spot_exit_price DECIMAL(10, 2),
    option_symbol TEXT NOT NULL,
    option_strike DECIMAL(10, 2) NOT NULL,
    option_entry_price DECIMAL(10, 2) NOT NULL,
    option_exit_price DECIMAL(10, 2),
    quantity INTEGER NOT NULL,
    stop_loss DECIMAL(10, 2) NOT NULL,
    target DECIMAL(10, 2) NOT NULL,
    atr_at_entry DECIMAL(10, 2) NOT NULL,
    entry_reason TEXT NOT NULL,
    exit_reason TEXT,
    pnl DECIMAL(10, 2),
    pnl_percent DECIMAL(10, 4),
    created_at TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE trades ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Enable all access for authenticated users"
ON trades FOR ALL
TO authenticated
USING (true)
WITH CHECK (true);

CREATE INDEX idx_trades_entry_time ON trades(entry_time);
CREATE INDEX idx_trades_state ON trades(state);

-- Candles Table
CREATE TABLE IF NOT EXISTS candles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timestamp TIMESTAMPTZ NOT NULL,
    open DECIMAL(10, 2) NOT NULL,
    high DECIMAL(10, 2) NOT NULL,
    low DECIMAL(10, 2) NOT NULL,
    close DECIMAL(10, 2) NOT NULL,
    volume BIGINT NOT NULL,
    timeframe_minutes INTEGER NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE candles ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Enable all access for authenticated users"
ON candles FOR ALL
TO authenticated
USING (true)
WITH CHECK (true);

CREATE INDEX idx_candles_timestamp ON candles(timestamp);
CREATE INDEX idx_candles_timeframe ON candles(timeframe_minutes);

-- Market States Table
CREATE TABLE IF NOT EXISTS market_states (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    timestamp TIMESTAMPTZ NOT NULL,
    state TEXT NOT NULL CHECK (state IN ('SIDEWAYS', 'TRENDING_BULLISH', 'TRENDING_BEARISH')),
    reason TEXT NOT NULL,
    adx DECIMAL(10, 4) NOT NULL,
    rsi DECIMAL(10, 4) NOT NULL,
    macd DECIMAL(10, 4) NOT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

ALTER TABLE market_states ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Enable all access for authenticated users"
ON market_states FOR ALL
TO authenticated
USING (true)
WITH CHECK (true);

CREATE INDEX idx_market_states_timestamp ON market_states(timestamp);
CREATE INDEX idx_market_states_state ON market_states(state);
