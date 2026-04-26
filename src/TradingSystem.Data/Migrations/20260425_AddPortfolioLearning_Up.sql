-- =============================================
-- Migration: Add portfolio learning tables
-- Date: 2026-04-25
-- =============================================

-- Stores historical portfolio performance snapshots for learning cycles
CREATE TABLE IF NOT EXISTS portfolio_performance_histories (
    id BIGSERIAL PRIMARY KEY,
    session_id BIGINT NOT NULL,
    win_rate NUMERIC(8,4) NOT NULL DEFAULT 0,
    sharpe_ratio NUMERIC(10,4) NOT NULL DEFAULT 0,
    max_drawdown NUMERIC(8,4) NOT NULL DEFAULT 0,
    profit_factor NUMERIC(10,4) NOT NULL DEFAULT 0,
    average_hold_days NUMERIC(8,2) NOT NULL DEFAULT 0,
    average_hold_efficiency NUMERIC(12,4) NOT NULL DEFAULT 0,
    average_fusion_score NUMERIC(8,4) NOT NULL DEFAULT 0,
    veto_rejection_rate NUMERIC(8,4) NOT NULL DEFAULT 0,
    total_trades INTEGER NOT NULL DEFAULT 0,
    winning_trades INTEGER NOT NULL DEFAULT 0,
    losing_trades INTEGER NOT NULL DEFAULT 0,
    total_pnl NUMERIC(18,2) NOT NULL DEFAULT 0,
    active_fusion_learning_config_iteration INTEGER,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_portfolio_perf_history_session
        FOREIGN KEY (session_id)
        REFERENCES portfolio_manager_sessions(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_portfolio_perf_history_session_id
ON portfolio_performance_histories(session_id);

CREATE INDEX IF NOT EXISTS idx_portfolio_perf_history_recorded_at
ON portfolio_performance_histories(recorded_at DESC);

-- Stores all fusion config iterations as immutable learning/audit records
CREATE TABLE IF NOT EXISTS fusion_learning_configs (
    id BIGSERIAL PRIMARY KEY,
    iteration INTEGER NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    applied_at TIMESTAMPTZ,
    technical_weight NUMERIC(5,4) NOT NULL DEFAULT 0.5000,
    news_weight NUMERIC(5,4) NOT NULL DEFAULT 0.3500,
    sector_weight NUMERIC(5,4) NOT NULL DEFAULT 0.1500,
    minimum_fusion_score NUMERIC(8,4) NOT NULL DEFAULT 0.5500,
    news_negative_boundary NUMERIC(5,4) NOT NULL DEFAULT -0.3500,
    news_positive_boundary NUMERIC(5,4) NOT NULL DEFAULT 0.3500,
    prior_performance_metrics_json JSONB,
    prior_config_json JSONB,
    reasoning_text TEXT,
    sessions_analyzed INTEGER NOT NULL DEFAULT 0,
    risk_assessment VARCHAR(20) NOT NULL DEFAULT 'MODERATE',
    status VARCHAR(20) NOT NULL DEFAULT 'CANDIDATE',
    rolled_back_at TIMESTAMPTZ,
    sessions_completed_under_this_config INTEGER NOT NULL DEFAULT 0,
    performance_under_this_config_json JSONB
);

CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_iteration
ON fusion_learning_configs(iteration);

CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_status
ON fusion_learning_configs(status);

CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_applied_at
ON fusion_learning_configs(applied_at DESC);

CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_created_at
ON fusion_learning_configs(created_at DESC);

COMMENT ON TABLE portfolio_performance_histories
IS 'Historical performance snapshots used as learning input for portfolio fusion tuning';

COMMENT ON TABLE fusion_learning_configs
IS 'Audit trail of all fusion algorithm tuning iterations and activation status';
