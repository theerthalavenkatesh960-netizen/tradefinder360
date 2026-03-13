-- =============================================
-- Migration: Create feature_store table
-- Date: 2026-03-14
-- =============================================

-- Create table
CREATE TABLE IF NOT EXISTS feature_store (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    symbol VARCHAR(50) NOT NULL,
    timestamp TIMESTAMPTZ NOT NULL,
    features_json JSONB NOT NULL,
    feature_count INTEGER NOT NULL,
    feature_version VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT fk_feature_store_instruments
        FOREIGN KEY (instrument_id)
        REFERENCES instruments(id)
        ON DELETE CASCADE
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_feature_store_instrument_time
ON feature_store(instrument_id, timestamp);

CREATE INDEX IF NOT EXISTS idx_feature_store_symbol
ON feature_store(symbol);

CREATE INDEX IF NOT EXISTS idx_feature_store_timestamp
ON feature_store(timestamp);

CREATE INDEX IF NOT EXISTS idx_feature_store_version
ON feature_store(feature_version);

-- Documentation
COMMENT ON TABLE feature_store 
IS 'Stores pre-computed machine learning feature vectors (120+ quantitative factors)';

COMMENT ON COLUMN feature_store.features_json 
IS 'JSON serialized feature dictionary containing all computed indicators';

COMMENT ON COLUMN feature_store.feature_version 
IS 'Feature schema version for tracking changes over time';