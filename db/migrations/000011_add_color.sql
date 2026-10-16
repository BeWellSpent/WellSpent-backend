-- +goose Up
ALTER TABLE category ADD COLUMN IF NOT EXISTS color TEXT NOT NULL DEFAULT '';
ALTER TABLE payment_methods ADD COLUMN IF NOT EXISTS color TEXT NOT NULL DEFAULT '';
ALTER TABLE budget_to_profile_mapping ADD COLUMN IF NOT EXISTS color TEXT NOT NULL DEFAULT '';

-- +goose Down
ALTER TABLE category DROP COLUMN IF EXISTS color;
ALTER TABLE payment_methods DROP COLUMN IF EXISTS color;
ALTER TABLE budget_to_profile_mapping DROP COLUMN IF EXISTS color;
