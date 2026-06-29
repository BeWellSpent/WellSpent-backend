-- name: ListTransactions :many
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
FROM transaction
WHERE budget_period_id = sqlc.arg('budget_period_id')::uuid
  AND (sqlc.narg('category_id')::int IS NULL OR category_id = sqlc.narg('category_id'))
  AND (sqlc.narg('transaction_type_id')::int IS NULL OR transaction_type_id = sqlc.narg('transaction_type_id'))
ORDER BY date DESC NULLS LAST;

-- name: ListFixedRecurringTransactionsByPeriod :many
SELECT t.id, t.name, t.amount, t.planned_amount, t.date, t.renewal_date, t.recurring,
       t.budget_period_id, t.category_id, t.payment_method_id, t.transaction_frequency_id, t.transaction_type_id
FROM transaction t
WHERE t.budget_period_id = $1
  AND t.transaction_type_id = (SELECT tt.id FROM transaction_type tt WHERE tt.name = 'Fixed' LIMIT 1)
  AND t.recurring = TRUE;

-- name: GetTransactionByID :one
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
FROM transaction
WHERE id = $1
LIMIT 1;

-- name: CreateTransaction :one
INSERT INTO transaction (
    name, amount, planned_amount, date, renewal_date, recurring,
    budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id;

-- name: UpdateTransaction :one
UPDATE transaction
SET name = $2, amount = $3, planned_amount = $4, date = $5, recurring = $6,
    category_id = $7, payment_method_id = $8, transaction_frequency_id = $9, transaction_type_id = $10
WHERE id = $1
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_period_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id;

-- name: DeleteTransaction :exec
DELETE FROM transaction
WHERE id = $1 AND budget_period_id = $2;

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

-- Reassigns all transactions with this category to the replacement, then soft-deletes.
-- No budget scoping: categories are user-scoped so reassignment spans all periods.
-- name: DeleteCategoryAndReassign :exec
WITH moved AS (
    UPDATE transaction SET category_id = sqlc.arg('replacement_id')
    WHERE category_id = sqlc.arg('id')
)
UPDATE category
SET is_active = FALSE
WHERE category.id = sqlc.arg('id') AND category.user_id = sqlc.arg('user_id')::uuid AND category.is_system = FALSE;

-- name: GetPaymentMethod :one
SELECT id, name, payment_type_id, user_id, is_active, budget_person_id, color
FROM payment_methods
WHERE id = $1
LIMIT 1;

-- name: ListPaymentMethods :many
SELECT pm.id, pm.name, pm.payment_type_id, pm.user_id, pt.name AS type_name, pm.budget_person_id, pm.color
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
RETURNING id, name, payment_type_id, user_id, is_active, budget_person_id, color;

-- name: UpdatePaymentMethod :one
UPDATE payment_methods
SET name = sqlc.arg('name'), color = sqlc.arg('color')
WHERE id = sqlc.arg('id') AND user_id = sqlc.arg('user_id')::uuid
RETURNING id, name, payment_type_id, user_id, is_active, budget_person_id, color;

-- Reassigns all transactions referencing this method within the profile's periods, then soft-deletes.
-- name: DeletePaymentMethodAndReassign :exec
WITH moved AS (
    UPDATE transaction SET payment_method_id = sqlc.arg('replacement_id')::uuid
    WHERE payment_method_id = sqlc.arg('id')::uuid
      AND budget_period_id IN (
        SELECT bp.id FROM budget_period bp WHERE bp.budget_profile_id = sqlc.arg('budget_profile_id')::uuid
      )
)
UPDATE payment_methods
SET is_active = FALSE
WHERE payment_methods.id = sqlc.arg('id')::uuid AND payment_methods.user_id = sqlc.arg('user_id')::uuid;

-- name: ListTransactionTypes :many
SELECT id, name FROM transaction_type ORDER BY id;

-- name: ListTransactionFrequencies :many
SELECT id, name FROM transaction_frequency ORDER BY id;
