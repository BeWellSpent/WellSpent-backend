-- +goose Up

-- Explicit scheduled pay-day anchor for this income source. NULL means no
-- regular schedule set (irregular income), which later period-generation
-- logic treats as "falls back to calendar month".
ALTER TABLE income_source
  ADD COLUMN anchor_date DATE NULL;

-- How many days before the scheduled date a deposit still counts as "on
-- time" for period-funding purposes. 0 = no tolerance.
ALTER TABLE income_source
  ADD COLUMN early_arrival_tolerance_days INTEGER NOT NULL DEFAULT 0;

-- When this schedule version takes effect. NULL = effective since creation.
ALTER TABLE income_source
  ADD COLUMN effective_from DATE NULL;

-- When this schedule version stops being current, set when a later edit
-- supersedes it. NULL = currently effective, no upper bound.
ALTER TABLE income_source
  ADD COLUMN effective_to DATE NULL;

-- Set by a manual override command; any (re)assignment/backfill logic must
-- never touch a row where this is true.
ALTER TABLE transaction
  ADD COLUMN period_overridden BOOLEAN NOT NULL DEFAULT FALSE;

-- +goose Down
ALTER TABLE income_source
  DROP COLUMN IF EXISTS anchor_date;
ALTER TABLE income_source
  DROP COLUMN IF EXISTS early_arrival_tolerance_days;
ALTER TABLE income_source
  DROP COLUMN IF EXISTS effective_from;
ALTER TABLE income_source
  DROP COLUMN IF EXISTS effective_to;
ALTER TABLE transaction
  DROP COLUMN IF EXISTS period_overridden;
