-- +goose Up

-- Repairs FixedExpense templates whose payment_method_id was left pointing
-- at a payment method that has since been soft-deleted. Before this
-- migration's companion query fix (DeletePaymentMethodAndReassign now also
-- reassigns fixed_expense, not just transaction/savings_source), deleting a
-- payment method left every FixedExpense template still referencing it —
-- so every future period cycle spawned from that template kept copying the
-- now-inactive method's id onto its new transaction, which then rendered
-- blank once ListPaymentMethods filtered the inactive method out.
--
-- Best-effort recovery: backfill from the template's own most recently
-- spawned transaction that still has an active payment method (the
-- replacement chosen when the method was originally deleted); otherwise
-- null it out so the user must reattach one manually.
WITH latest_active_tx AS (
    SELECT DISTINCT ON (t.fixed_expense_id) t.fixed_expense_id, t.payment_method_id
    FROM transaction t
    JOIN payment_methods pm ON pm.id = t.payment_method_id
    WHERE t.fixed_expense_id IS NOT NULL
      AND pm.is_active = TRUE
    ORDER BY t.fixed_expense_id, t.date DESC
)
UPDATE fixed_expense fe
SET payment_method_id = latest_active_tx.payment_method_id
FROM latest_active_tx
WHERE fe.id = latest_active_tx.fixed_expense_id
  AND fe.payment_method_id IS NOT NULL
  AND fe.payment_method_id != latest_active_tx.payment_method_id
  AND EXISTS (
    SELECT 1 FROM payment_methods pm2
    WHERE pm2.id = fe.payment_method_id AND pm2.is_active = FALSE
  );

-- Any remaining affected template (no usable transaction history to recover
-- from) is nulled out rather than left silently pointing at a dead method.
UPDATE fixed_expense fe
SET payment_method_id = NULL
WHERE fe.payment_method_id IS NOT NULL
  AND EXISTS (
    SELECT 1 FROM payment_methods pm
    WHERE pm.id = fe.payment_method_id AND pm.is_active = FALSE
  );

-- +goose Down

-- Data-only backfill; not reversible (the original dangling references
-- aren't recoverable once repaired).
