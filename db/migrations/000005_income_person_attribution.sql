-- +goose Up
ALTER TABLE income_to_budget_mapping
    ADD COLUMN IF NOT EXISTS budget_person_id INT REFERENCES budget_to_user_mapping(id);

-- +goose Down
ALTER TABLE income_to_budget_mapping
    DROP COLUMN IF EXISTS budget_person_id;
