-- +goose Up
ALTER TABLE category ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE category DROP COLUMN is_active;
