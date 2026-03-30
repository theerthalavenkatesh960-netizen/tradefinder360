-- =============================================
-- Migration Rollback: Drop Self-Learning AI System Tables
-- Date: 2023-03-16
-- =============================================

-- Drop indexes first
DROP INDEX IF EXISTS idx_factor_performance_period;

DROP INDEX IF EXISTS idx_ai_model_versions_training_date;
DROP INDEX IF EXISTS idx_ai_model_versions_status;
DROP INDEX IF EXISTS idx_ai_model_versions_active;

DROP INDEX IF EXISTS idx_trade_outcomes_symbol_time;
DROP INDEX IF EXISTS idx_trade_outcomes_regime_success;
DROP INDEX IF EXISTS idx_trade_outcomes_status;
DROP INDEX IF EXISTS idx_trade_outcomes_model_version;
DROP INDEX IF EXISTS idx_trade_outcomes_entry_time;
DROP INDEX IF EXISTS idx_trade_outcomes_instrument;

-- Drop tables in reverse order
DROP TABLE IF EXISTS factor_performance_tracking CASCADE;
DROP TABLE IF EXISTS ai_model_versions CASCADE;
DROP TABLE IF EXISTS trade_outcomes CASCADE;