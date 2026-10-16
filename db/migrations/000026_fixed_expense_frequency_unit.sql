-- +goose Up

-- Weekly cadence support. frequency_unit mirrors the FrequencyUnit proto enum
-- (0=unspecified, 1=month, 2=week); 1 (month) is the default so every
-- existing row keeps behaving exactly as it does today. interval_weeks and
-- day_of_week are the week-cadence analogues of interval_months/day_of_month
-- and are only meaningful when frequency_unit = 2 (week). day_of_week uses
-- ISO 8601 numbering: 1 = Monday ... 7 = Sunday.
ALTER TABLE fixed_expense
    ADD COLUMN frequency_unit SMALLINT NOT NULL DEFAULT 1,
    ADD COLUMN interval_weeks INTEGER NOT NULL DEFAULT 1,
    ADD COLUMN day_of_week SMALLINT NOT NULL DEFAULT 1;

-- +goose Down

ALTER TABLE fixed_expense
    DROP COLUMN IF EXISTS frequency_unit,
    DROP COLUMN IF EXISTS interval_weeks,
    DROP COLUMN IF EXISTS day_of_week;
