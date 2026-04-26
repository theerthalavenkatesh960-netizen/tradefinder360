-- =============================================
-- Migration Rollback: Remove portfolio learning tables
-- Date: 2026-04-25
-- =============================================

-- Drop indexes first
DROP INDEX IF EXISTS idx_fusion_learning_config_created_at;
DROP INDEX IF EXISTS idx_fusion_learning_config_applied_at;
DROP INDEX IF EXISTS idx_fusion_learning_config_status;
DROP INDEX IF EXISTS idx_fusion_learning_config_iteration;

DROP INDEX IF EXISTS idx_portfolio_perf_history_recorded_at;
DROP INDEX IF EXISTS idx_portfolio_perf_history_session_id;

-- Drop tables
DROP TABLE IF EXISTS fusion_learning_configs;
DROP TABLE IF EXISTS portfolio_performance_histories;
