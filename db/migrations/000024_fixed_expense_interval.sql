-- +goose Up

-- Recurrence interval in months: 1 = every month (default, matches today's
-- behavior for all existing rows), 3 = quarterly, 6 = semi-annual, 12 =
-- yearly, etc. Combined with created_at as the implicit anchor month, this
-- lets period generation skip periods a fixed expense isn't actually due in.
ALTER TABLE fixed_expense
    ADD COLUMN interval_months INTEGER NOT NULL DEFAULT 1;

-- +goose Down

ALTER TABLE fixed_expense DROP COLUMN IF EXISTS interval_months;
