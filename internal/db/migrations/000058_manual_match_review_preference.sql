-- +goose Up

-- Per-person, per-budget: whether this person's transactions get scored
-- against fixed expenses for a match. Defaults true, matching prior behavior.
ALTER TABLE budget_to_profile_mapping
  ADD COLUMN manual_match_review_enabled BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE budget_to_profile_mapping
  DROP COLUMN IF EXISTS manual_match_review_enabled;
