-- +goose Up

-- Issue #119: Income, Payment and Transfer stop being categories; their meaning is
-- transaction.flow (000067). Fixed-expense templates get a flow of their own, which
-- every bill they spawn copies, so a template no longer needs one of those categories.
ALTER TABLE fixed_expense
  ADD COLUMN flow TEXT NOT NULL DEFAULT 'spend'
  CHECK (flow IN ('spend', 'refund', 'income', 'transfer', 'payment'));

-- Templates filed under a retired category keep its meaning as their flow.
UPDATE fixed_expense fe
SET flow = c.system_key, category_id = NULL
FROM category c
WHERE fe.category_id = c.id AND c.system_key IN ('income', 'payment', 'transfer');

-- Transactions already carry the flow (backfilled by 000067, set on every save since);
-- the category goes. A transfer becomes uncategorized, to be filed properly.
UPDATE transaction t
SET category_id = NULL
FROM category c
WHERE t.category_id = c.id AND c.system_key IN ('income', 'payment', 'transfer');

-- Planned amounts and category alerts on these categories can't be shown or edited
-- once the categories are hidden; spending never landed in Income/Payment anyway.
DELETE FROM expense_allocation
WHERE category_id IN (SELECT id FROM category WHERE system_key IN ('income', 'payment', 'transfer'));
DELETE FROM alert_subscription
WHERE category_id IN (SELECT id FROM category WHERE system_key IN ('income', 'payment', 'transfer'));

-- Hidden, not deleted: system_key stays so old snapshots and logs still resolve.
UPDATE category SET is_active = FALSE WHERE system_key IN ('income', 'payment', 'transfer');

-- +goose Down
UPDATE category SET is_active = TRUE WHERE system_key IN ('income', 'payment', 'transfer');
ALTER TABLE fixed_expense DROP COLUMN IF EXISTS flow;
