/*
  # Undo Partition instrument_prices Table

  This script reverses the changes made by 007_partition_instrument_prices.sql
  and restores the instrument_prices table to a regular (non-partitioned) table.

  WARNING: This will DROP all data in the instrument_prices table and its partitions.
  Only run this if you want to completely revert to the non-partitioned structure.

  Steps:
    1. Drop all partition child tables
    2. Drop the partitioned parent table
    3. Drop helper functions
    4. Drop trigger function
    5. Recreate non-partitioned instrument_prices table with original structure
    6. Recreate indexes
    7. Reapply RLS policies
*/

-- =====================================================
-- STEP 1: Drop trigger (must be done before dropping functions)
-- =====================================================

DROP TRIGGER IF EXISTS trigger_auto_create_partition ON instrument_prices;

-- =====================================================
-- STEP 2: Drop all partition child tables
-- =====================================================

DO $$
DECLARE
    partition_record RECORD;
BEGIN
    FOR partition_record IN
        SELECT c.relname AS partition_name
        FROM pg_class c
        JOIN pg_inherits i ON i.inhrelid = c.oid
        JOIN pg_class parent ON parent.oid = i.inhparent
        WHERE parent.relname = 'instrument_prices'
        AND c.relkind = 'r'
    LOOP
        EXECUTE format('DROP TABLE IF EXISTS %I CASCADE', partition_record.partition_name);
        RAISE NOTICE 'Dropped partition: %', partition_record.partition_name;
    END LOOP;
END $$;

-- =====================================================
-- STEP 3: Drop partitioned parent table
-- =====================================================

DROP TABLE IF EXISTS instrument_prices CASCADE;

-- =====================================================
-- STEP 4: Drop helper functions
-- =====================================================

DROP FUNCTION IF EXISTS create_monthly_partition(INTEGER, INTEGER) CASCADE;
DROP FUNCTION IF EXISTS create_next_n_months_partitions(INTEGER) CASCADE;
DROP FUNCTION IF EXISTS auto_create_partition_on_insert() CASCADE;

-- =====================================================
-- STEP 5: Recreate non-partitioned instrument_prices table
-- =====================================================

CREATE TABLE IF NOT EXISTS instrument_prices (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    open DECIMAL(18, 4) NOT NULL,
    high DECIMAL(18, 4) NOT NULL,
    low DECIMAL(18, 4) NOT NULL,
    close DECIMAL(18, 4) NOT NULL,
    volume BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    CONSTRAINT fk_instrument_prices_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

-- =====================================================
-- STEP 6: Recreate indexes on non-partitioned table
-- =====================================================

CREATE INDEX IF NOT EXISTS idx_instrument_prices_instrument_timestamp
    ON instrument_prices (instrument_id, timestamp DESC);

CREATE INDEX IF NOT EXISTS idx_instrument_prices_timestamp
    ON instrument_prices (timestamp);

-- =====================================================
-- STEP 7: Enable RLS and recreate policies
-- =====================================================

ALTER TABLE instrument_prices ENABLE ROW LEVEL SECURITY;

CREATE POLICY "Authenticated users can read instrument prices"
    ON instrument_prices
    FOR SELECT
    TO authenticated
    USING (true);

CREATE POLICY "Authenticated users can insert instrument prices"
    ON instrument_prices
    FOR INSERT
    TO authenticated
    WITH CHECK (true);

CREATE POLICY "Authenticated users can update instrument prices"
    ON instrument_prices
    FOR UPDATE
    TO authenticated
    USING (true)
    WITH CHECK (true);

CREATE POLICY "Authenticated users can delete instrument prices"
    ON instrument_prices
    FOR DELETE
    TO authenticated
    USING (true);

-- =====================================================
-- VERIFICATION
-- =====================================================

-- Verify table is no longer partitioned:
-- SELECT
--     tablename,
--     pg_get_partkeydef(c.oid) AS partition_key
-- FROM pg_tables t
-- JOIN pg_class c ON c.relname = t.tablename
-- WHERE schemaname = 'public' AND tablename = 'instrument_prices';
-- (partition_key should be NULL for non-partitioned table)
