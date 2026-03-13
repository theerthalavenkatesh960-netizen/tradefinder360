-- =============================================
-- Migration: Add Self-Learning AI System Tables
-- Date: 2023-03-16
-- =============================================

-- ==========================================================
-- 1. TRADE OUTCOMES
-- ==========================================================

CREATE TABLE IF NOT EXISTS trade_outcomes (
    id BIGSERIAL PRIMARY KEY,

    instrument_id INTEGER NOT NULL,
    symbol VARCHAR(50) NOT NULL,

    entry_time TIMESTAMPTZ NOT NULL,
    exit_time TIMESTAMPTZ,

    entry_price NUMERIC(18,4) NOT NULL,
    exit_price NUMERIC(18,4),

    direction VARCHAR(10) NOT NULL,
    quantity NUMERIC(18,4) NOT NULL,

    predicted_return REAL NOT NULL,
    predicted_success_probability REAL NOT NULL,
    predicted_risk_score REAL NOT NULL,

    model_version VARCHAR(20) NOT NULL,

    meta_factors_json JSONB,
    market_regime_at_entry VARCHAR(50),
    regime_confidence REAL,

    actual_return REAL,
    profit_loss NUMERIC(18,4),
    profit_loss_percent REAL,

    is_successful BOOLEAN,

    prediction_error REAL,
    prediction_accuracy_score REAL,

    failure_reason TEXT,
    learning_tags JSONB,

    strategy VARCHAR(50),
    sector VARCHAR(100),

    status VARCHAR(20) NOT NULL DEFAULT 'OPEN',

    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT FK_trade_outcomes_instruments
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE,

    CONSTRAINT CK_trade_outcomes_direction
        CHECK (direction IN ('BUY','SELL')),

    CONSTRAINT CK_trade_outcomes_status
        CHECK (status IN ('OPEN','CLOSED','STOPPED_OUT','TARGET_HIT')),

    CONSTRAINT CK_trade_outcomes_probability
        CHECK (predicted_success_probability >= 0 AND predicted_success_probability <= 1),

    CONSTRAINT CK_trade_outcomes_regime_confidence
        CHECK (regime_confidence IS NULL OR (regime_confidence >= 0 AND regime_confidence <= 1))
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_trade_outcomes_instrument
ON trade_outcomes(instrument_id);

CREATE INDEX IF NOT EXISTS idx_trade_outcomes_entry_time
ON trade_outcomes(entry_time DESC);

CREATE INDEX IF NOT EXISTS idx_trade_outcomes_model_version
ON trade_outcomes(model_version);

CREATE INDEX IF NOT EXISTS idx_trade_outcomes_status
ON trade_outcomes(status);

CREATE INDEX IF NOT EXISTS idx_trade_outcomes_regime_success
ON trade_outcomes(market_regime_at_entry, is_successful);

CREATE INDEX IF NOT EXISTS idx_trade_outcomes_symbol_time
ON trade_outcomes(symbol, entry_time DESC);

COMMENT ON TABLE trade_outcomes IS
'Tracks AI predicted trade outcomes for continuous learning';

-- ==========================================================
-- 2. AI MODEL VERSIONS
-- ==========================================================

CREATE TABLE IF NOT EXISTS ai_model_versions (

    id SERIAL PRIMARY KEY,

    version VARCHAR(20) NOT NULL,
    model_type VARCHAR(50) NOT NULL,

    training_date TIMESTAMPTZ NOT NULL,

    training_dataset_size INTEGER,
    validation_dataset_size INTEGER,

    training_duration VARCHAR(100),

    hyperparameters_json JSONB,
    feature_importance_json JSONB,

    training_accuracy REAL,
    validation_accuracy REAL,

    win_rate REAL,
    profit_factor REAL,
    sharpe_ratio REAL,
    max_drawdown REAL,

    average_prediction_error REAL,

    total_predictions INTEGER DEFAULT 0,
    successful_predictions INTEGER DEFAULT 0,

    production_accuracy REAL DEFAULT 0,
    production_sharpe_ratio REAL DEFAULT 0,

    total_pnl NUMERIC(18,4) DEFAULT 0,

    status VARCHAR(20) NOT NULL DEFAULT 'TRAINING',
    is_active BOOLEAN DEFAULT FALSE,

    deprecation_reason TEXT,

    model_file_path TEXT,
    checkpoint_path TEXT,

    change_log TEXT,
    improvement_notes JSONB,

    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    activated_at TIMESTAMPTZ,
    deprecated_at TIMESTAMPTZ,

    CONSTRAINT UQ_ai_model_versions UNIQUE (version, model_type),

    CONSTRAINT CK_ai_model_versions_status
        CHECK (status IN ('TRAINING','TESTING','PRODUCTION','DEPRECATED'))
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_ai_model_versions_active
ON ai_model_versions(is_active);

CREATE INDEX IF NOT EXISTS idx_ai_model_versions_status
ON ai_model_versions(status);

CREATE INDEX IF NOT EXISTS idx_ai_model_versions_training_date
ON ai_model_versions(training_date DESC);

COMMENT ON TABLE ai_model_versions IS
'Tracks AI model versions, performance metrics, and deployment history';

-- ==========================================================
-- 3. FACTOR PERFORMANCE TRACKING
-- ==========================================================

CREATE TABLE IF NOT EXISTS factor_performance_tracking (

    id BIGSERIAL PRIMARY KEY,

    period_start TIMESTAMPTZ NOT NULL,
    period_end TIMESTAMPTZ NOT NULL,

    momentum_weight REAL,
    trend_weight REAL,
    volatility_weight REAL,
    liquidity_weight REAL,
    relative_strength_weight REAL,
    sentiment_weight REAL,
    risk_weight REAL,

    momentum_win_rate REAL,
    momentum_avg_return REAL,
    momentum_trade_count INTEGER,

    trend_win_rate REAL,
    trend_avg_return REAL,
    trend_trade_count INTEGER,

    sentiment_win_rate REAL,
    sentiment_avg_return REAL,
    sentiment_trade_count INTEGER,

    total_trades INTEGER,
    overall_win_rate REAL,
    overall_sharpe_ratio REAL,

    recommended_adjustments_json JSONB,

    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT CK_factor_win_rate
        CHECK (overall_win_rate IS NULL OR (overall_win_rate >= 0 AND overall_win_rate <= 1))
);

-- Index
CREATE INDEX IF NOT EXISTS idx_factor_performance_period
ON factor_performance_tracking(period_start, period_end);

COMMENT ON TABLE factor_performance_tracking IS
'Tracks factor performance to enable reinforcement-learning adjustments';