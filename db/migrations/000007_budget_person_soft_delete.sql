-- +goose Up
ALTER TABLE budget_to_user_mapping ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE budget_to_user_mapping DROP COLUMN is_active;
