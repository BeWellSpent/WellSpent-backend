-- +goose Up
ALTER TABLE payment_methods
    ADD COLUMN plaid_item_id UUID NULL REFERENCES plaid_item(id) ON DELETE SET NULL;

CREATE INDEX idx_payment_methods_plaid_item_id ON payment_methods (plaid_item_id)
    WHERE plaid_item_id IS NOT NULL;

-- +goose Down
DROP INDEX IF EXISTS idx_payment_methods_plaid_item_id;
ALTER TABLE payment_methods
    DROP COLUMN IF EXISTS plaid_item_id;
