-- +goose Up
ALTER TABLE payment_methods
    ADD COLUMN IF NOT EXISTS budget_person_id INT REFERENCES budget_to_user_mapping(id);

-- +goose Down
ALTER TABLE payment_methods
    DROP COLUMN IF EXISTS budget_person_id;
