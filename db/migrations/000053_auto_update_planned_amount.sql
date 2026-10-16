-- Whether marking a fixed expense paid at a different amount should rewrite its
-- template, so the next period is planned at what the bill actually costs.
--
-- DEFAULT TRUE because that is what MarkTransactionAsPaid already did
-- unconditionally, and silently — existing budgets keep the behaviour they
-- have. What changes is that it becomes visible, switchable, and consistent:
-- ConfirmTransactionReview and the Plaid auto-confirm never updated the
-- template, so the same bill paid three different ways produced two different
-- plans for the next period.

-- +goose Up
ALTER TABLE budget_profile
    ADD COLUMN IF NOT EXISTS auto_update_planned_amount BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE budget_profile DROP COLUMN IF EXISTS auto_update_planned_amount;
