-- Create feature_store table
CREATE TABLE feature_store (
    id BIGSERIAL PRIMARY KEY,
    instrument_id INTEGER NOT NULL,
    symbol VARCHAR(50) NOT NULL,
    timestamp TIMESTAMP WITH TIME ZONE NOT NULL,
    features_json JSONB NOT NULL,
    feature_count INTEGER NOT NULL,
    feature_version VARCHAR(20) NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    CONSTRAINT FK_feature_store_instruments_instrument_id 
        FOREIGN KEY (instrument_id) 
        REFERENCES instruments(id) 
        ON DELETE CASCADE
);

-- Create indexes for performance
CREATE INDEX idx_feature_store_instrument_time ON feature_store(instrument_id, timestamp);
CREATE INDEX idx_feature_store_symbol ON feature_store(symbol);
CREATE INDEX idx_feature_store_timestamp ON feature_store(timestamp);
CREATE INDEX idx_feature_store_version ON feature_store(feature_version);

-- Add comment for documentation
COMMENT ON TABLE feature_store IS 'Stores pre-computed machine learning feature vectors (120+ quantitative factors)';
COMMENT ON COLUMN feature_store.features_json IS 'JSON serialized feature dictionary containing all computed indicators';
COMMENT ON COLUMN feature_store.feature_version IS 'Feature schema version for tracking changes over time';