-- +goose Up

-- Per-person, per-budget chart preferences. `budget_to_profile_mapping` is
-- already the user↔budget join, so this is the natural home: one row per
-- person per budget, and removing someone discards their preferences with the
-- mapping.
--
-- Stored as text rather than the enum's integer, matching `role`, `status` and
-- `plan` elsewhere in this schema — it stays readable in psql.
--
-- NULL means "use the client's built-in default". Every existing member gets
-- NULL, so nobody's view changes until they pick something.
ALTER TABLE budget_to_profile_mapping
  ADD COLUMN plan_chart_type TEXT NULL
    CHECK (plan_chart_type IN ('pie', 'bar')),
  ADD COLUMN overview_chart_type TEXT NULL
    CHECK (overview_chart_type IN ('pie', 'bar'));

-- +goose Down
ALTER TABLE budget_to_profile_mapping
  DROP COLUMN IF EXISTS plan_chart_type,
  DROP COLUMN IF EXISTS overview_chart_type;
