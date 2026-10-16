-- +goose Up
ALTER TABLE savings_source
    ADD COLUMN IF NOT EXISTS payment_days INTEGER[];

-- +goose Down
ALTER TABLE savings_source DROP COLUMN IF EXISTS payment_days;
