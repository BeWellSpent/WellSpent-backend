-- +goose Up
ALTER TABLE payment_methods ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE payment_methods DROP COLUMN is_active;
