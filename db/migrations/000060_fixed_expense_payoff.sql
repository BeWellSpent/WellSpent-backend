-- +goose Up

-- Lasting record of a payment plan settled early (issue #55): paid_off_date
-- is stamped the moment a payment brings remaining_balance to zero, since
-- the template disappears from listFixedExpenses once deactivated. The
-- transaction flag is set on exactly the one payment that crossed the
-- threshold, and only when that happened strictly before end_date.
ALTER TABLE fixed_expense ADD COLUMN paid_off_date DATE NULL;
ALTER TABLE transaction ADD COLUMN fixed_expense_paid_off_early BOOLEAN NOT NULL DEFAULT FALSE;

-- +goose Down
ALTER TABLE transaction DROP COLUMN IF EXISTS fixed_expense_paid_off_early;
ALTER TABLE fixed_expense DROP COLUMN IF EXISTS paid_off_date;
