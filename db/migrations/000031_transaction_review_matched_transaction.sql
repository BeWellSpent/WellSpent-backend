-- +goose Up

-- This feature is still under active development with no real user data
-- riding on it, so the cleanest path for repointing the match target from a
-- FixedExpense template to a specific transaction is to reset existing rows
-- rather than backfill a mapping that doesn't exist (a template has no single
-- "this row" to point at).
DELETE FROM transaction_review;

ALTER TABLE transaction_review
    DROP COLUMN fixed_expense_id,
    ADD COLUMN matched_transaction_id UUID NOT NULL REFERENCES transaction(id) ON DELETE CASCADE;

-- +goose Down

DELETE FROM transaction_review;

ALTER TABLE transaction_review
    DROP COLUMN matched_transaction_id,
    ADD COLUMN fixed_expense_id UUID NOT NULL REFERENCES fixed_expense(id) ON DELETE CASCADE;
