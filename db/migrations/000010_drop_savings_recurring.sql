-- +goose Up
ALTER TABLE savings_source DROP COLUMN IF EXISTS recurring;

-- +goose Down
ALTER TABLE savings_source ADD COLUMN recurring BOOLEAN NOT NULL DEFAULT TRUE;
