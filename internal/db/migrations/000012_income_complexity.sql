-- +goose Up

-- Countries and per-country feature flags
CREATE TABLE countries (
    code       VARCHAR(2)   PRIMARY KEY,
    name       VARCHAR(100) NOT NULL,
    is_enabled BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

CREATE TABLE country_features (
    country_code VARCHAR(2)   NOT NULL REFERENCES countries(code) ON DELETE CASCADE,
    feature_name VARCHAR(100) NOT NULL,
    is_enabled   BOOLEAN      NOT NULL DEFAULT TRUE,
    PRIMARY KEY (country_code, feature_name)
);

-- Seed countries
INSERT INTO countries (code, name, is_enabled) VALUES
    ('AR', 'Argentina',     TRUE),
    ('ES', 'Spain',         TRUE),
    ('US', 'United States', TRUE);

-- Seed country-feature mappings
INSERT INTO country_features (country_code, feature_name, is_enabled) VALUES
    ('US', 'before_tax_income', TRUE);

-- User profile: country, state, tax filing preferences
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS country_code          VARCHAR(2)  REFERENCES countries(code),
    ADD COLUMN IF NOT EXISTS state_code            VARCHAR(10),
    ADD COLUMN IF NOT EXISTS filing_status         VARCHAR(50) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS tax_payment_frequency INTEGER     NOT NULL DEFAULT 0;

-- Income source: before-tax flag
ALTER TABLE income_source
    ADD COLUMN IF NOT EXISTS before_tax BOOLEAN NOT NULL DEFAULT FALSE;

-- Budget profile: country propagated from owner at creation
ALTER TABLE budget_profile
    ADD COLUMN IF NOT EXISTS country_code VARCHAR(2) REFERENCES countries(code);

-- Savings source: system-managed tax reserve flag
ALTER TABLE savings_source
    ADD COLUMN IF NOT EXISTS is_tax_reserve BOOLEAN NOT NULL DEFAULT FALSE;

-- Partial unique index: at most one tax-reserve savings source per budget profile.
-- Required for the ON CONFLICT upsert in UpsertTaxReserveSavingsSource.
CREATE UNIQUE INDEX IF NOT EXISTS idx_savings_source_tax_reserve
    ON savings_source (budget_profile_id) WHERE is_tax_reserve = TRUE;

-- +goose Down

DROP INDEX IF EXISTS idx_savings_source_tax_reserve;
ALTER TABLE savings_source    DROP COLUMN IF EXISTS is_tax_reserve;
ALTER TABLE budget_profile    DROP COLUMN IF EXISTS country_code;
ALTER TABLE income_source     DROP COLUMN IF EXISTS before_tax;
ALTER TABLE users             DROP COLUMN IF EXISTS tax_payment_frequency,
                              DROP COLUMN IF EXISTS filing_status,
                              DROP COLUMN IF EXISTS state_code,
                              DROP COLUMN IF EXISTS country_code;
DROP TABLE IF EXISTS country_features;
DROP TABLE IF EXISTS countries;
