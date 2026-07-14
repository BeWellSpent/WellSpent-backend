-- name: CreateFixedExpense :one
INSERT INTO fixed_expense (budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, interval_months, anchor_date, frequency_unit, interval_weeks, day_of_week, end_date, total_payments)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
RETURNING id, budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, is_active, created_at, interval_months, anchor_date, frequency_unit, interval_weeks, day_of_week, end_date, total_payments;

-- name: GetFixedExpense :one
SELECT id, budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, is_active, created_at, interval_months, anchor_date, frequency_unit, interval_weeks, day_of_week, end_date, total_payments
FROM fixed_expense
WHERE id = $1
LIMIT 1;

-- name: ListFixedExpenses :many
SELECT id, budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, is_active, created_at, interval_months, anchor_date, frequency_unit, interval_weeks, day_of_week, end_date, total_payments
FROM fixed_expense
WHERE budget_profile_id = $1 AND is_active = TRUE
ORDER BY name;

-- name: UpdateFixedExpense :one
UPDATE fixed_expense
SET name              = sqlc.arg('name'),
    planned_amount    = sqlc.arg('planned_amount'),
    category_id       = sqlc.arg('category_id'),
    payment_method_id = sqlc.arg('payment_method_id'),
    day_of_month      = sqlc.arg('day_of_month'),
    interval_months   = sqlc.arg('interval_months'),
    anchor_date       = sqlc.arg('anchor_date'),
    frequency_unit    = sqlc.arg('frequency_unit'),
    interval_weeks    = sqlc.arg('interval_weeks'),
    day_of_week       = sqlc.arg('day_of_week'),
    end_date          = sqlc.arg('end_date'),
    total_payments    = sqlc.arg('total_payments')
WHERE id = sqlc.arg('id')::uuid
  AND budget_profile_id = sqlc.arg('budget_profile_id')::uuid
RETURNING id, budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, is_active, created_at, interval_months, anchor_date, frequency_unit, interval_weeks, day_of_week, end_date, total_payments;

-- name: FixedExpenseHasTransactionInMonth :one
SELECT EXISTS (
    SELECT 1 FROM transaction
    WHERE fixed_expense_id = sqlc.arg('fixed_expense_id')::uuid
      AND date >= sqlc.arg('month_start')::date
      AND date < sqlc.arg('month_end')::date
) AS exists;

-- name: FixedExpenseHasTransactionOnDate :one
SELECT EXISTS (
    SELECT 1 FROM transaction
    WHERE fixed_expense_id = sqlc.arg('fixed_expense_id')::uuid
      AND date = sqlc.arg('target_date')::date
) AS exists;

-- name: UpdateFixedExpensePlannedAmount :exec
UPDATE fixed_expense
SET planned_amount = sqlc.arg('planned_amount')
WHERE id = sqlc.arg('id')::uuid;

-- name: DeactivateFixedExpense :exec
UPDATE fixed_expense
SET is_active = FALSE
WHERE id = sqlc.arg('id')::uuid
  AND budget_profile_id = sqlc.arg('budget_profile_id')::uuid;

-- name: GetUnpaidTransactionByFixedExpense :one
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
       is_paid, paid_date, fixed_expense_id, plaid_transaction_id
FROM transaction
WHERE fixed_expense_id = sqlc.arg('fixed_expense_id')::uuid
  AND is_paid = FALSE
  AND budget_period_id IN (
      SELECT id FROM budget_period
      WHERE budget_profile_id = sqlc.arg('budget_profile_id')::uuid
        AND is_archived = FALSE
  )
ORDER BY date DESC NULLS LAST
LIMIT 1;

-- name: DeleteUnpaidTransactionByFixedExpense :exec
DELETE FROM transaction
WHERE fixed_expense_id = sqlc.arg('fixed_expense_id')::uuid
  AND is_paid = FALSE
  AND budget_period_id IN (
      SELECT id FROM budget_period
      WHERE budget_profile_id = sqlc.arg('budget_profile_id')::uuid
        AND is_archived = FALSE
  );

-- name: UpdateTransactionFromFixedExpense :exec
UPDATE transaction
SET name              = sqlc.arg('name'),
    planned_amount    = sqlc.arg('planned_amount'),
    amount            = sqlc.arg('planned_amount'),
    category_id       = sqlc.arg('category_id'),
    payment_method_id = sqlc.arg('payment_method_id'),
    date              = sqlc.arg('date')
WHERE fixed_expense_id = sqlc.arg('fixed_expense_id')::uuid
  AND is_paid = FALSE
  AND budget_period_id IN (
      SELECT id FROM budget_period
      WHERE budget_profile_id = sqlc.arg('budget_profile_id')::uuid
        AND is_archived = FALSE
  );
