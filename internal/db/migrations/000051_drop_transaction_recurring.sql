-- +goose Up
-- transaction.recurring predates the fixed_expense template table (migration
-- 000021), which is what actually carries a recurring expense across periods.
-- Traced end to end before removing: no query filtered on it, no service
-- branched on it, and createNextPeriod's comment claiming it drove rollover was
-- stale. It was an editable checkbox on web and absent on iOS entirely.
--
-- The proto field survives (removing one is breaking) and is now always false.
-- IncomeSource.recurring is a different column and is still real.
ALTER TABLE transaction DROP COLUMN recurring;

-- +goose Down
ALTER TABLE transaction ADD COLUMN recurring BOOLEAN;
