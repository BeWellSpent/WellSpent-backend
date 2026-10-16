-- +goose Up
ALTER TABLE payment_methods ADD COLUMN alias TEXT NULL;

-- +goose Down
ALTER TABLE payment_methods DROP COLUMN IF EXISTS alias;
