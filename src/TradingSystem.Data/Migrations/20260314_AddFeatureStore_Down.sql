-- =============================================
-- Migration Rollback: Drop feature_store table
-- Date: 2026-03-14
-- =============================================

-- Drop indexes first
DROP INDEX IF EXISTS idx_feature_store_version;
DROP INDEX IF EXISTS idx_feature_store_timestamp;
DROP INDEX IF EXISTS idx_feature_store_symbol;
DROP INDEX IF EXISTS idx_feature_store_instrument_time;

-- Drop table
DROP TABLE IF EXISTS feature_store;