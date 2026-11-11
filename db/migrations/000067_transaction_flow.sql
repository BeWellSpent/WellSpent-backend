-- +goose Up

-- What a movement of money is (issue #117), separate from its category. Backfilled from
-- the system category that carried that meaning until now, so every total stays identical:
-- spend/refund/transfer count toward spending, income/payment never do.
ALTER TABLE transaction
  ADD COLUMN flow TEXT NOT NULL DEFAULT 'spend'
  CHECK (flow IN ('spend', 'refund', 'income', 'transfer', 'payment'));

UPDATE transaction t
SET flow = c.system_key
FROM category c
WHERE t.category_id = c.id
  AND c.system_key IN ('payment', 'income', 'transfer');

UPDATE transaction SET flow = 'refund' WHERE flow = 'spend' AND amount < 0;

-- +goose Down
ALTER TABLE transaction DROP COLUMN IF EXISTS flow;
