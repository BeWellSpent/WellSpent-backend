-- name: ListTransactions :many
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
       is_paid, paid_date, fixed_expense_id, plaid_transaction_id
FROM transaction
WHERE budget_period_id = sqlc.arg('budget_period_id')::uuid
  AND (sqlc.narg('category_id')::int IS NULL OR category_id = sqlc.narg('category_id'))
  AND (sqlc.narg('transaction_type_id')::int IS NULL OR transaction_type_id = sqlc.narg('transaction_type_id'))
  AND NOT EXISTS (
    SELECT 1 FROM transaction_review tr
    WHERE tr.transaction_id = transaction.id AND tr.status = 'confirmed'
  )
ORDER BY date DESC NULLS LAST;

-- name: GetTransactionByID :one
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
       is_paid, paid_date, fixed_expense_id, plaid_transaction_id
FROM transaction
WHERE id = $1
LIMIT 1;

-- name: CreateTransaction :one
INSERT INTO transaction (
    name, amount, planned_amount, date, renewal_date, recurring,
    budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
    fixed_expense_id
) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
          is_paid, paid_date, fixed_expense_id, plaid_transaction_id;

-- name: UpdateTransaction :one
UPDATE transaction
SET name = $2, amount = $3, planned_amount = $4, date = $5, recurring = $6,
    category_id = $7, payment_method_id = $8, transaction_frequency_id = $9, transaction_type_id = $10
WHERE id = $1
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
          is_paid, paid_date, fixed_expense_id, plaid_transaction_id;

-- name: DeleteTransaction :exec
DELETE FROM transaction
WHERE id = $1 AND budget_period_id = $2;

-- name: MarkTransactionAsPaid :one
UPDATE transaction
SET is_paid = TRUE,
    paid_date = sqlc.arg('paid_date')::date,
    amount = sqlc.arg('amount')
WHERE id = sqlc.arg('id')::uuid
  AND budget_period_id = sqlc.arg('budget_period_id')::uuid
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
          is_paid, paid_date, fixed_expense_id, plaid_transaction_id;

-- name: UnmarkTransactionAsPaid :one
UPDATE transaction
SET is_paid = FALSE,
    paid_date = NULL,
    amount = planned_amount
WHERE id = sqlc.arg('id')::uuid
  AND budget_period_id = sqlc.arg('budget_period_id')::uuid
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
          is_paid, paid_date, fixed_expense_id, plaid_transaction_id;

-- Deletes auto-created savings transactions when a savings source is removed.
-- Matches by name, payment method, and category within non-archived periods.
-- name: DeleteSavingsSourceTransactions :exec
DELETE FROM transaction
WHERE budget_period_id IN (
    SELECT id FROM budget_period
    WHERE budget_profile_id = sqlc.arg('budget_profile_id')::uuid AND is_archived = FALSE
)
AND name = sqlc.arg('name')
AND payment_method_id = sqlc.arg('payment_method_id')::uuid
AND category_id = sqlc.arg('category_id');

-- name: GetCategory :one
SELECT id, name, type_id, is_system, user_id, color
FROM category
WHERE id = $1
LIMIT 1;

-- name: ListCategories :many
SELECT id, name, type_id, is_system, user_id, color
FROM category
WHERE (user_id = $1::uuid AND is_active = TRUE) OR user_id IS NULL
ORDER BY name;

-- name: ListCategoriesForBudget :many
-- Returns the same set as ListCategories but also includes categories referenced
-- by transactions or fixed expenses in the given budget, so collaborators/viewers
-- can see categories created by the budget owner.
SELECT DISTINCT c.id, c.name, c.type_id, c.is_system, c.user_id, c.color
FROM category c
WHERE (
  (c.user_id = sqlc.arg('user_id')::uuid AND c.is_active = TRUE)
  OR c.user_id IS NULL
  OR c.id IN (
    SELECT DISTINCT t.category_id
    FROM transaction t
    JOIN budget_period bp ON t.budget_period_id = bp.id
    WHERE bp.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      AND t.category_id IS NOT NULL
  )
  OR c.id IN (
    SELECT DISTINCT fe.category_id
    FROM fixed_expense fe
    WHERE fe.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      AND fe.category_id IS NOT NULL
  )
)
ORDER BY c.name;

-- name: CreateCategory :one
INSERT INTO category (name, user_id, color)
VALUES (sqlc.arg('name'), sqlc.arg('user_id')::uuid, sqlc.arg('color'))
RETURNING id, name, type_id, is_system, user_id, color;

-- name: UpdateCategory :one
UPDATE category
SET name = sqlc.arg('name'), color = sqlc.arg('color')
WHERE id = sqlc.arg('id') AND user_id = sqlc.arg('user_id')::uuid AND is_system = FALSE
RETURNING id, name, type_id, is_system, user_id, color;

-- name: UpdateSystemCategoryColor :one
UPDATE category
SET color = sqlc.arg('color')
WHERE id = sqlc.arg('id') AND is_system = TRUE
RETURNING id, name, type_id, is_system, user_id, color;

-- Reassigns all transactions and fixed-expense templates referencing this
-- category to the replacement, then soft-deletes the category. Fixed expenses
-- must be reassigned too, not just transactions: otherwise an orphaned
-- template keeps the "deleted" category alive in ListCategoriesForBudget
-- (which surfaces any category referenced by a fixed_expense row) and future
-- transactions spawned from that template would carry the dead category_id.
-- No budget scoping: categories are user-scoped so reassignment spans all periods.
-- name: DeleteCategoryAndReassign :exec
WITH moved_tx AS (
    UPDATE transaction SET category_id = sqlc.arg('replacement_id')
    WHERE category_id = sqlc.arg('id')
), moved_fe AS (
    UPDATE fixed_expense SET category_id = sqlc.arg('replacement_id')
    WHERE category_id = sqlc.arg('id')
)
UPDATE category
SET is_active = FALSE
WHERE category.id = sqlc.arg('id') AND category.user_id = sqlc.arg('user_id')::uuid AND category.is_system = FALSE;

-- Returns all system (global) categories. Used by the Plaid sync job to
-- resolve category names to IDs without a user context.
-- name: ListSystemCategories :many
SELECT id, name FROM category WHERE is_system = TRUE ORDER BY name;

-- name: GetPaymentMethod :one
SELECT id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias
FROM payment_methods
WHERE id = $1
LIMIT 1;

-- name: ListPaymentMethods :many
SELECT pm.id, pm.name, pm.payment_type_id, pm.user_id, pt.name AS type_name, pm.budget_person_id, pm.color, pm.alias
FROM payment_methods pm
LEFT JOIN payment_type pt ON pm.payment_type_id = pt.id
WHERE pm.budget_person_id IN (
    SELECT id FROM budget_to_profile_mapping WHERE budget_profile_id = $1::uuid
)
AND pm.is_active = TRUE
ORDER BY pm.name;

-- name: CreatePaymentMethod :one
INSERT INTO payment_methods (name, payment_type_id, user_id, budget_person_id, color)
VALUES ($1, $2, $3, $4, $5)
RETURNING id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias;

-- name: UpdatePaymentMethod :one
UPDATE payment_methods
SET name = sqlc.arg('name'), color = sqlc.arg('color'), alias = sqlc.narg('alias')
WHERE payment_methods.id = sqlc.arg('id')
RETURNING id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias;

-- Reassigns all transactions and savings sources referencing this method, then soft-deletes.
-- name: DeletePaymentMethodAndReassign :exec
WITH moved_tx AS (
    UPDATE transaction SET payment_method_id = sqlc.arg('replacement_id')::uuid
    WHERE payment_method_id = sqlc.arg('id')::uuid
      AND budget_period_id IN (
        SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      )
), moved_savings AS (
    UPDATE savings_source SET payment_method_id = sqlc.arg('replacement_id')::uuid
    WHERE payment_method_id = sqlc.arg('id')::uuid
      AND budget_profile_id = sqlc.arg('budget_profile_id')::uuid
)
UPDATE payment_methods
SET is_active = FALSE
WHERE payment_methods.id = sqlc.arg('id')::uuid;

-- Creates a payment method linked to a Plaid account. plaid_account_id is
-- stored so the sync job can route imported transactions to the right method.
-- name: CreatePaymentMethodFromPlaid :one
INSERT INTO payment_methods (name, payment_type_id, user_id, budget_person_id, plaid_account_id)
VALUES ($1, $2, $3, $4, $5)
RETURNING id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias;

-- name: GetPaymentMethodByPlaidAccountID :one
SELECT id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias
FROM payment_methods
WHERE plaid_account_id = $1 AND is_active = TRUE
LIMIT 1;

-- name: GetPaymentMethodByUserAndName :one
SELECT id, name, payment_type_id, user_id, is_active, budget_person_id, color, plaid_account_id, alias
FROM payment_methods
WHERE user_id = $1 AND name = $2 AND is_active = TRUE
LIMIT 1;

-- name: UpdatePaymentMethodPlaidAccountID :exec
UPDATE payment_methods
SET plaid_account_id = $1
WHERE id = $2;

-- name: ListTransactionTypes :many
SELECT id, name FROM transaction_type ORDER BY id;

-- name: ListTransactionFrequencies :many
SELECT id, name FROM transaction_frequency ORDER BY id;

-- ── Plaid ─────────────────────────────────────────────────────────────────────

-- name: CreateTransactionFromPlaid :one
INSERT INTO transaction (
    name, amount, planned_amount, date, renewal_date, recurring,
    budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
    fixed_expense_id, plaid_transaction_id
) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
          is_paid, paid_date, fixed_expense_id, plaid_transaction_id;

-- name: GetTransactionByPlaidID :one
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id,
       is_paid, paid_date, fixed_expense_id, plaid_transaction_id
FROM transaction
WHERE plaid_transaction_id = $1
LIMIT 1;

-- name: ExistsTransactionByPlaidID :one
SELECT EXISTS (
    SELECT 1 FROM transaction WHERE plaid_transaction_id = $1
) AS exists;

-- Updates name and amount for a Plaid-imported transaction.
-- name: UpdateTransactionFromPlaid :exec
UPDATE transaction
SET name = $2, amount = $3
WHERE plaid_transaction_id = $1;

-- name: DeleteTransactionByPlaidID :exec
DELETE FROM transaction
WHERE plaid_transaction_id = $1;
