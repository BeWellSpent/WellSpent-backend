-- +goose Up
-- Converting a variable transaction into an installment plan excludes it from
-- totals. is_excluded alone can't say *why*, so a client can't explain the
-- exclusion or stop a user toggling it back on — which would double-count the
-- purchase alongside the plan created from it.
ALTER TABLE transaction
    ADD COLUMN installment_fixed_expense_id UUID NULL REFERENCES fixed_expense(id) ON DELETE SET NULL;

-- ON DELETE SET NULL, not CASCADE: deleting the plan must not delete the
-- purchase it was derived from. The transaction reverts to a plain excluded
-- row, which is recoverable; the reverse is not.

-- Card installments are settled inside the card's total balance and never
-- appear as their own line item on a bank feed, so a Plaid transaction that
-- scores against one of these templates is a false positive by construction.
-- scoreBestMatch skips them.
ALTER TABLE fixed_expense
    ADD COLUMN is_installment_plan BOOLEAN NOT NULL DEFAULT FALSE;

-- +goose Down
ALTER TABLE fixed_expense DROP COLUMN is_installment_plan;
ALTER TABLE transaction DROP COLUMN installment_fixed_expense_id;
