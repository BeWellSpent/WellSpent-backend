-- +goose Up

-- When true (the default), a monthly/yearly anchor_date that lands on a
-- Saturday or Sunday rolls back to the preceding Friday before being used as
-- a pay date anywhere. Meaningless for weekly/bi-weekly sources.
ALTER TABLE income_source
  ADD COLUMN adjust_for_weekends BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE income_source
  DROP COLUMN IF EXISTS adjust_for_weekends;
