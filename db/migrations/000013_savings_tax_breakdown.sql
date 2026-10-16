-- +goose Up
ALTER TABLE savings_source
  ADD COLUMN IF NOT EXISTS federal_amount NUMERIC,
  ADD COLUMN IF NOT EXISTS state_amount   NUMERIC;

-- +goose Down
ALTER TABLE savings_source
  DROP COLUMN IF EXISTS federal_amount,
  DROP COLUMN IF EXISTS state_amount;
