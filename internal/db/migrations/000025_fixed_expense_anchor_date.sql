-- +goose Up

-- Explicit interval anchor + scheduling day, overriding created_at and
-- day_of_month when set. Lets a fixed expense be created now but not
-- actually become due (and spawn its first transaction) until a specific
-- future date. NULL preserves today's behavior exactly.
ALTER TABLE fixed_expense
    ADD COLUMN anchor_date DATE NULL;

-- +goose Down

ALTER TABLE fixed_expense DROP COLUMN IF EXISTS anchor_date;
