-- Migration: Add Self-Learning AI System Tables

-- 1. Trade Outcomes Table
CREATE TABLE trade_outcomes (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    symbol VARCHAR(50) NOT NULL,
    entry_time TIMESTAMP WITH TIME ZONE NOT NULL,
    exit_time TIMESTAMP WITH TIME ZONE,
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
    status VARCHAR(20) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT FK_trade_outcomes_instruments FOREIGN KEY (instrument_id) 
        REFERENCES instruments(id) ON DELETE CASCADE
);

CREATE INDEX idx_trade_outcomes_instrument ON trade_outcomes(instrument_id);
CREATE INDEX idx_trade_outcomes_entry_time ON trade_outcomes(entry_time);
CREATE INDEX idx_trade_outcomes_model_version ON trade_outcomes(model_version);
CREATE INDEX idx_trade_outcomes_status ON trade_outcomes(status);
CREATE INDEX idx_trade_outcomes_regime_success ON trade_outcomes(market_regime_at_entry, is_successful);

COMMENT ON TABLE trade_outcomes IS 'Tracks AI-predicted trade outcomes for continuous learning';

-- 2. AI Model Versions Table
CREATE TABLE ai_model_versions (
    id SERIAL PRIMARY KEY,
    version VARCHAR(20) NOT NULL,
    model_type VARCHAR(50) NOT NULL,
    training_date TIMESTAMP WITH TIME ZONE NOT NULL,
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
    status VARCHAR(20) NOT NULL,
    is_active BOOLEAN DEFAULT FALSE,
    deprecation_reason TEXT,
    model_file_path TEXT,
    checkpoint_path TEXT,
    change_log TEXT,
    improvement_notes JSONB,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    activated_at TIMESTAMP WITH TIME ZONE,
    deprecated_at TIMESTAMP WITH TIME ZONE,
    CONSTRAINT UQ_ai_model_versions UNIQUE (version, model_type)
);

CREATE INDEX idx_ai_model_versions_active ON ai_model_versions(is_active);
CREATE INDEX idx_ai_model_versions_status ON ai_model_versions(status);

COMMENT ON TABLE ai_model_versions IS 'Tracks AI model versions, performance, and deployment history';

-- 3. Factor Performance Tracking Table
CREATE TABLE factor_performance_tracking (
    id BIGSERIAL PRIMARY KEY,
    period_start TIMESTAMP WITH TIME ZONE NOT NULL,
    period_end TIMESTAMP WITH TIME ZONE NOT NULL,
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
    total_trades REAL,
    overall_win_rate REAL,
    overall_sharpe_ratio REAL,
    recommended_adjustments_json JSONB,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL
);

CREATE INDEX idx_factor_performance_period ON factor_performance_tracking(period_start, period_end);

COMMENT ON TABLE factor_performance_tracking IS 'Tracks meta-factor performance for reinforcement learning';