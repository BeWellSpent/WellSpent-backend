-- +goose Up

-- Optional payment plan fields on fixed_expense:
--   end_date       — the date of the last payment; createNextPeriod auto-deactivates the template
--                    when the new period's start date is past this date
--   total_payments — informational; the total count of payments in the plan (used by the UI
--                    to display progress like "18 of 60 payments")
ALTER TABLE fixed_expense
    ADD COLUMN end_date DATE NULL,
    ADD COLUMN total_payments INTEGER NULL;

-- +goose Down

ALTER TABLE fixed_expense
    DROP COLUMN IF EXISTS end_date,
    DROP COLUMN IF EXISTS total_payments;
