-- +goose Up
ALTER TABLE transaction
    ADD COLUMN is_paid  BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN paid_date DATE;

-- +goose Down
ALTER TABLE transaction
    DROP COLUMN is_paid,
    DROP COLUMN paid_date;
