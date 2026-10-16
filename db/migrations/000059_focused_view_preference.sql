-- +goose Up

-- Per-person, per-budget: whether Plan/Overview/Transactions/Income/Savings
-- are scoped to this person's own data. Defaults false — Full View stays default.
ALTER TABLE budget_to_profile_mapping
  ADD COLUMN focused_view_enabled BOOLEAN NOT NULL DEFAULT FALSE;

-- +goose Down
ALTER TABLE budget_to_profile_mapping
  DROP COLUMN IF EXISTS focused_view_enabled;
