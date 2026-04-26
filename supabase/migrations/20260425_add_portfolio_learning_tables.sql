-- Migration: Add Portfolio Performance and Fusion Learning Config tables
-- Date: 2026-04-25
-- Purpose: Support Phase 4 - Self-learning portfolio system

-- Table: portfolio_performance_histories
-- Stores historical performance metrics for portfolio sessions
-- Used for tracking trends and as baseline for learning decisions
CREATE TABLE IF NOT EXISTS portfolio_performance_histories (
    id bigserial PRIMARY KEY,
    session_id bigint NOT NULL,
    win_rate numeric(8,4) NOT NULL DEFAULT 0,
    sharpe_ratio numeric(10,4) NOT NULL DEFAULT 0,
    max_drawdown numeric(8,4) NOT NULL DEFAULT 0,
    profit_factor numeric(10,4) NOT NULL DEFAULT 0,
    average_hold_days numeric(8,2) NOT NULL DEFAULT 0,
    average_hold_efficiency numeric(12,4) NOT NULL DEFAULT 0,
    average_fusion_score numeric(8,4) NOT NULL DEFAULT 0,
    veto_rejection_rate numeric(8,4) NOT NULL DEFAULT 0,
    total_trades integer NOT NULL DEFAULT 0,
    winning_trades integer NOT NULL DEFAULT 0,
    losing_trades integer NOT NULL DEFAULT 0,
    total_pnl numeric(18,2) NOT NULL DEFAULT 0,
    active_fusion_learning_config_iteration integer,
    recorded_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_portfolio_perf_history_session
        FOREIGN KEY (session_id)
        REFERENCES portfolio_manager_sessions(id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_portfolio_perf_history_session_id 
    ON portfolio_performance_histories(session_id);
CREATE INDEX IF NOT EXISTS idx_portfolio_perf_history_recorded_at 
    ON portfolio_performance_histories(recorded_at DESC);

-- Table: fusion_learning_configs
-- Stores all fusion algorithm tuning iterations (audit trail)
-- Each learning decision creates a new config record
CREATE TABLE IF NOT EXISTS fusion_learning_configs (
    id bigserial PRIMARY KEY,
    iteration integer NOT NULL UNIQUE,
    created_at timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    applied_at timestamp with time zone,
    technical_weight numeric(5,4) NOT NULL DEFAULT 0.5000,
    news_weight numeric(5,4) NOT NULL DEFAULT 0.3500,
    sector_weight numeric(5,4) NOT NULL DEFAULT 0.1500,
    minimum_fusion_score numeric(8,4) NOT NULL DEFAULT 0.5500,
    news_negative_boundary numeric(5,4) NOT NULL DEFAULT -0.3500,
    news_positive_boundary numeric(5,4) NOT NULL DEFAULT 0.3500,
    prior_performance_metrics_json jsonb,
    prior_config_json jsonb,
    reasoning_text text,
    sessions_analyzed integer NOT NULL DEFAULT 0,
    risk_assessment varchar(20) NOT NULL DEFAULT 'MODERATE',
    status varchar(20) NOT NULL DEFAULT 'CANDIDATE',
    rolled_back_at timestamp with time zone,
    sessions_completed_under_this_config integer NOT NULL DEFAULT 0,
    performance_under_this_config_json jsonb
);

CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_iteration 
    ON fusion_learning_configs(iteration);
CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_status 
    ON fusion_learning_configs(status);
CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_applied_at 
    ON fusion_learning_configs(applied_at DESC);
CREATE INDEX IF NOT EXISTS idx_fusion_learning_config_created_at 
    ON fusion_learning_configs(created_at DESC);

-- Comment for documentation
COMMENT ON TABLE portfolio_performance_histories IS 
    'Historical performance metrics for portfolio sessions; used to track trends and determine learning needs';
COMMENT ON TABLE fusion_learning_configs IS 
    'Audit trail of all fusion algorithm tuning iterations; immutable record of algorithm evolution';
