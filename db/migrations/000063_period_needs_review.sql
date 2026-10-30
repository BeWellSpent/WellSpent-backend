-- +goose Up

-- True right after a genuine cycle (not a profile's first-ever period) until
-- the user confirms or retriggers the carried-over review (issue #74).
ALTER TABLE budget_period
  ADD COLUMN needs_review BOOLEAN NOT NULL DEFAULT FALSE;

-- +goose Down
ALTER TABLE budget_period
  DROP COLUMN IF EXISTS needs_review;
