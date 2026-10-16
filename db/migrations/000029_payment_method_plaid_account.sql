-- +goose Up
ALTER TABLE payment_methods ADD COLUMN IF NOT EXISTS plaid_account_id TEXT;
-- Partial unique index so multiple null values are allowed (non-Plaid methods).
CREATE UNIQUE INDEX IF NOT EXISTS payment_methods_plaid_account_id_idx
    ON payment_methods (plaid_account_id) WHERE plaid_account_id IS NOT NULL;

-- +goose Down
DROP INDEX IF EXISTS payment_methods_plaid_account_id_idx;
ALTER TABLE payment_methods DROP COLUMN IF EXISTS plaid_account_id;
