-- +goose Up

-- The plan as it stood when a period closed, so the plan scorecard can compare
-- a past period against what was planned then, not against today's plan.
-- budget_person_id NULL is the whole category's planned total.
CREATE TABLE budget_period_plan (
  id               BIGSERIAL PRIMARY KEY,
  budget_period_id UUID NOT NULL REFERENCES budget_period (id) ON DELETE CASCADE,
  category_id      INTEGER NOT NULL REFERENCES category (id) ON DELETE CASCADE,
  budget_person_id INTEGER NULL REFERENCES budget_to_profile_mapping (id) ON DELETE CASCADE,
  planned_amount   NUMERIC(15,4) NOT NULL,
  created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX budget_period_plan_unique
  ON budget_period_plan (budget_period_id, category_id, COALESCE(budget_person_id, -1));

-- +goose Down
DROP TABLE IF EXISTS budget_period_plan;
