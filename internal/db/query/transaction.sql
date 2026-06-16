-- name: ListTransactions :many
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
FROM transaction
WHERE budget_id = sqlc.arg('budget_id')::uuid
  AND (sqlc.narg('category_id')::int IS NULL OR category_id = sqlc.narg('category_id'))
  AND (sqlc.narg('transaction_type_id')::int IS NULL OR transaction_type_id = sqlc.narg('transaction_type_id'))
ORDER BY date DESC NULLS LAST;

-- name: GetTransactionByID :one
SELECT id, name, amount, planned_amount, date, renewal_date, recurring,
       budget_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
FROM transaction
WHERE id = $1
LIMIT 1;

-- name: CreateTransaction :one
INSERT INTO transaction (
    name, amount, planned_amount, date, renewal_date, recurring,
    budget_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id
) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id;

-- name: UpdateTransaction :one
UPDATE transaction
SET name = $2, amount = $3, planned_amount = $4, date = $5, recurring = $6,
    category_id = $7, payment_method_id = $8, transaction_frequency_id = $9, transaction_type_id = $10
WHERE id = $1
RETURNING id, name, amount, planned_amount, date, renewal_date, recurring,
          budget_id, category_id, payment_method_id, transaction_frequency_id, transaction_type_id;

-- name: DeleteTransaction :exec
DELETE FROM transaction
WHERE id = $1 AND budget_id = $2;

-- name: ListCategories :many
SELECT id, name, type_id, user_id
FROM category
WHERE user_id = $1::uuid OR user_id IS NULL
ORDER BY name;

-- name: CreateCategory :one
INSERT INTO category (name, type_id, user_id)
VALUES ($1, $2, $3)
RETURNING id, name, type_id, user_id;

-- name: ListPaymentMethods :many
SELECT pm.id, pm.name, pm.payment_type_id, pm.user_id, pt.name AS type_name
FROM payment_methods pm
LEFT JOIN payment_type pt ON pm.payment_type_id = pt.id
WHERE pm.user_id = $1::uuid
ORDER BY pm.name;

-- name: CreatePaymentMethod :one
INSERT INTO payment_methods (name, payment_type_id, user_id)
VALUES ($1, $2, $3)
RETURNING id, name, payment_type_id, user_id;

-- name: UpdatePaymentMethod :one
UPDATE payment_methods
SET name = sqlc.arg('name')
WHERE id = sqlc.arg('id') AND user_id = sqlc.arg('user_id')::uuid
RETURNING id, name, payment_type_id, user_id;

-- name: ListTransactionTypes :many
SELECT id, name FROM transaction_type ORDER BY id;

-- name: ListTransactionFrequencies :many
SELECT id, name FROM transaction_frequency ORDER BY id;
