-- Drop all tables in reverse dependency order
DROP TABLE IF EXISTS user_profiles CASCADE;
DROP TABLE IF EXISTS recommendations CASCADE;
DROP TABLE IF EXISTS scan_snapshots CASCADE;
DROP TABLE IF EXISTS trades CASCADE;
DROP TABLE IF EXISTS indicator_snapshots CASCADE;
DROP TABLE IF EXISTS market_candles CASCADE;
DROP TABLE IF EXISTS instrument_prices CASCADE;
DROP TABLE IF EXISTS instruments CASCADE;
DROP TABLE IF EXISTS sectors CASCADE;

-- Drop partition helper functions
DROP FUNCTION IF EXISTS create_next_n_months_market_candles_partitions(INTEGER);
DROP FUNCTION IF EXISTS create_market_candles_monthly_partition(INTEGER, INTEGER);

-- Now run your updated 001_initial_migration.sql script