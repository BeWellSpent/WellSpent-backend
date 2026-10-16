-- +goose Up

ALTER TABLE income_source
    ADD COLUMN payment_frequency VARCHAR(20) NOT NULL DEFAULT 'monthly';

CREATE TABLE savings_source (
    id                SERIAL         PRIMARY KEY,
    budget_profile_id UUID           NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    budget_person_id  INTEGER        REFERENCES budget_to_profile_mapping(id),
    name              VARCHAR(100)   NOT NULL,
    amount            NUMERIC(15, 4) NOT NULL DEFAULT 0,
    frequency         VARCHAR(20)    NOT NULL DEFAULT 'monthly',
    recurring         BOOLEAN        NOT NULL DEFAULT TRUE,
    created_at        TIMESTAMPTZ    NOT NULL DEFAULT NOW()
);

-- +goose Down

DROP TABLE IF EXISTS savings_source;

ALTER TABLE income_source
    DROP COLUMN IF EXISTS payment_frequency;
