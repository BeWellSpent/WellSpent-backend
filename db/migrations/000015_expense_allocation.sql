-- +goose Up
CREATE TABLE expense_allocation (
  id               SERIAL PRIMARY KEY,
  budget_profile_id UUID NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
  category_id      INT NOT NULL REFERENCES category(id) ON DELETE CASCADE,
  budget_person_id INT REFERENCES budget_to_profile_mapping(id) ON DELETE SET NULL,
  planned_amount   NUMERIC NOT NULL DEFAULT 0
);

CREATE UNIQUE INDEX idx_expense_allocation_unique
  ON expense_allocation (budget_profile_id, category_id, COALESCE(budget_person_id, -1));

-- +goose Down
DROP TABLE IF EXISTS expense_allocation;
