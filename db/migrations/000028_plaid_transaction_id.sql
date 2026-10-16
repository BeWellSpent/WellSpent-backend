-- +goose Up

ALTER TABLE transaction
    ADD COLUMN plaid_transaction_id TEXT UNIQUE;

-- +goose Down

ALTER TABLE transaction
    DROP COLUMN IF EXISTS plaid_transaction_id;
