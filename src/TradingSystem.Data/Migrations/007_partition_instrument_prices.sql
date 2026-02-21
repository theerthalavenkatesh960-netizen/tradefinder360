/*
  # Partition instrument_prices Table by Month

  1. Overview
    - Transforms the `instrument_prices` table into a partitioned table using RANGE partitioning by timestamp
    - Partitions data monthly for better query performance and data management
    - Includes auto-partition creation mechanism via trigger
    - Provides helper functions for manual and automated partition management

  2. Changes
    - DROP existing empty `instrument_prices` table
    - CREATE new partitioned `instrument_prices` table (PARTITION BY RANGE on timestamp)
    - CREATE helper function: `create_monthly_partition(year, month)` for manual partition creation
    - CREATE helper function: `create_next_n_months_partitions(n)` for automated partition creation
    - CREATE initial partitions for current month + next 3 months
    - CREATE BEFORE INSERT trigger to auto-create missing partitions
    - RECREATE all indexes on partitioned table
    - REAPPLY RLS policies

  3. Partitioning Strategy
    - Monthly partitions: Each partition holds one month of data
    - Partition naming: `instrument_prices_{year}_{month}` (e.g., instrument_prices_2026_02)
    - Auto-creation: Trigger creates partition on-the-fly if missing during INSERT
    - Scheduled creation: Quartz job creates partitions monthly in advance (see PartitionMaintenanceJob)

  4. Security
    - RLS enabled on parent table
    - Policy: Authenticated users can read all instrument prices
    - Policy: Authenticated users can insert instrument prices
    - Policy: Authenticated users can update instrument prices
    - Policy: Authenticated users can delete instrument prices

  5. Performance
    - Indexes created on parent table template (auto-inherited by partitions)
    - Composite index on (instrument_id, timestamp DESC) for time-series queries
    - Index on timestamp for partition pruning efficiency
    - Foreign key constraint on instrument_id maintained

  6. Important Notes
    - This migration assumes `instrument_prices` table is EMPTY
    - Partitions are created for current month + 3 future months initially
    - Monthly Quartz job will maintain future partitions (runs 28th of each month)
    - Trigger acts as safety net if job fails or timing issues occur
    - No data migration needed - clean start
*/

-- =====================================================
-- STEP 1: Drop existing empty table
-- =====================================================

DROP TABLE IF EXISTS instrument_prices CASCADE;

-- =====================================================
-- STEP 2: Create helper function to create a specific monthly partition
-- =====================================================

CREATE OR REPLACE FUNCTION create_monthly_partition(
    partition_year INTEGER,
    partition_month INTEGER
) RETURNS TEXT AS $$
DECLARE
    partition_name TEXT;
    start_date DATE;
    end_date DATE;
BEGIN
    -- Calculate partition date range
    start_date := make_date(partition_year, partition_month, 1);
    end_date := start_date + INTERVAL '1 month';

    -- Generate partition name
    partition_name := 'instrument_prices_' || partition_year || '_' || LPAD(partition_month::TEXT, 2, '0');

    -- Check if partition already exists
    IF EXISTS (
        SELECT 1 FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE c.relname = partition_name
        AND n.nspname = 'public'
    ) THEN
        RETURN 'Partition ' || partition_name || ' already exists';
    END IF;

    -- Create partition
    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS %I PARTITION OF instrument_prices
         FOR VALUES FROM (%L) TO (%L)',
        partition_name,
        start_date,
        end_date
    );

    RETURN 'Created partition: ' || partition_name || ' for date range [' || start_date || ', ' || end_date || ')';
END;
$$ LANGUAGE plpgsql;

-- =====================================================
-- STEP 3: Create helper function to create next N months of partitions
-- =====================================================

CREATE OR REPLACE FUNCTION create_next_n_months_partitions(months_ahead INTEGER DEFAULT 2)
RETURNS TABLE(result TEXT) AS $$
DECLARE
    current_month_start DATE;
    target_year INTEGER;
    target_month INTEGER;
    i INTEGER;
    partition_result TEXT;
BEGIN
    -- Start from current month
    current_month_start := date_trunc('month', CURRENT_DATE)::DATE;

    -- Create partitions for next N months (including current month)
    FOR i IN 0..months_ahead LOOP
        target_year := EXTRACT(YEAR FROM current_month_start + (i || ' months')::INTERVAL)::INTEGER;
        target_month := EXTRACT(MONTH FROM current_month_start + (i || ' months')::INTERVAL)::INTEGER;

        SELECT create_monthly_partition(target_year, target_month) INTO partition_result;

        result := partition_result;
        RETURN NEXT;
    END LOOP;

    RETURN;
END;
$$ LANGUAGE plpgsql;

-- =====================================================
-- STEP 4: Create partitioned instrument_prices table
-- =====================================================

CREATE TABLE instrument_prices (
    id BIGSERIAL,
    instrument_id INTEGER NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    open DECIMAL(18, 4) NOT NULL,
    high DECIMAL(18, 4) NOT NULL,
    low DECIMAL(18, 4) NOT NULL,
    close DECIMAL(18, 4) NOT NULL,
    volume BIGINT NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (id, timestamp),
    CONSTRAINT fk_instrument_prices_instrument
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
) PARTITION BY RANGE (timestamp);

-- =====================================================
-- STEP 5: Create initial partitions (current month + next 3 months)
-- =====================================================

SELECT create_next_n_months_partitions(3);

-- =====================================================
-- STEP 6: Create indexes on parent table (auto-inherited by partitions)
-- =====================================================

-- Index for time-series queries by instrument
CREATE INDEX IF NOT EXISTS idx_instrument_prices_instrument_timestamp
    ON instrument_prices (instrument_id, timestamp DESC);

-- Index for partition pruning efficiency
CREATE INDEX IF NOT EXISTS idx_instrument_prices_timestamp
    ON instrument_prices (timestamp);

-- =====================================================
-- STEP 7: Create BEFORE INSERT trigger for auto-partition creation
-- =====================================================

CREATE OR REPLACE FUNCTION auto_create_partition_on_insert()
RETURNS TRIGGER AS $$
DECLARE
    partition_year INTEGER;
    partition_month INTEGER;
    partition_result TEXT;
BEGIN
    -- Extract year and month from the timestamp being inserted
    partition_year := EXTRACT(YEAR FROM NEW.timestamp)::INTEGER;
    partition_month := EXTRACT(MONTH FROM NEW.timestamp)::INTEGER;

    -- Try to create the partition (function handles duplicate check)
    SELECT create_monthly_partition(partition_year, partition_month) INTO partition_result;

    -- Log partition creation (optional, can be removed if too verbose)
    RAISE NOTICE '%', partition_result;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trigger_auto_create_partition
    BEFORE INSERT ON instrument_prices
    FOR EACH ROW
    EXECUTE FUNCTION auto_create_partition_on_insert();

-- =====================================================
-- STEP 8: Enable RLS and create policies
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
-- VERIFICATION QUERIES (for testing)
-- =====================================================

-- To verify partitions created:
-- SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename LIKE 'instrument_prices_%' ORDER BY tablename;

-- To manually create a specific partition:
-- SELECT create_monthly_partition(2026, 6);

-- To create next 2 months of partitions:
-- SELECT create_next_n_months_partitions(2);
