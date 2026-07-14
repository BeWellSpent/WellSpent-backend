-- name: CreateTransactionReview :one
INSERT INTO transaction_review (budget_period_id, transaction_id, fixed_expense_id, match_score)
VALUES ($1, $2, $3, $4)
ON CONFLICT (transaction_id) DO NOTHING
RETURNING id, budget_period_id, transaction_id, fixed_expense_id, match_score, status, created_at;

-- name: ListPendingTransactionReviews :many
SELECT
    tr.id, tr.budget_period_id, tr.transaction_id, tr.fixed_expense_id,
    tr.match_score, tr.status, tr.created_at,
    t.name  AS transaction_name,
    t.amount AS transaction_amount,
    fe.name AS fixed_expense_name
FROM transaction_review tr
JOIN transaction t  ON t.id  = tr.transaction_id
JOIN fixed_expense fe ON fe.id = tr.fixed_expense_id
JOIN budget_period bp ON bp.id = tr.budget_period_id
WHERE bp.budget_profile_id = $1
  AND bp.is_archived = FALSE
  AND tr.status = 'pending'
ORDER BY tr.match_score DESC;

-- name: UpsertTransactionReview :one
INSERT INTO transaction_review (budget_period_id, transaction_id, fixed_expense_id, match_score, status)
VALUES ($1, $2, $3, $4, 'pending')
ON CONFLICT (transaction_id) DO UPDATE SET
    fixed_expense_id = EXCLUDED.fixed_expense_id,
    match_score      = EXCLUDED.match_score,
    status           = 'pending'
RETURNING id, budget_period_id, transaction_id, fixed_expense_id, match_score, status, created_at;

-- name: GetTransactionReview :one
SELECT id, budget_period_id, transaction_id, fixed_expense_id, match_score, status, created_at
FROM transaction_review
WHERE id = $1
LIMIT 1;

-- name: UpdateTransactionReviewStatus :exec
UPDATE transaction_review SET status = $2 WHERE id = $1;

-- name: CreateFixedExpenseAlias :exec
INSERT INTO fixed_expense_alias (fixed_expense_id, alias)
VALUES ($1, $2)
ON CONFLICT (fixed_expense_id, alias) DO NOTHING;

-- name: ListFixedExpenseAliases :many
SELECT alias FROM fixed_expense_alias WHERE fixed_expense_id = $1;

-- name: GetConfirmedReviewByFixedExpense :one
SELECT id, budget_period_id, transaction_id, fixed_expense_id, match_score, status, created_at
FROM transaction_review
WHERE fixed_expense_id = $1
  AND budget_period_id = $2
  AND status = 'confirmed'
LIMIT 1;

-- name: DeleteFixedExpenseAlias :exec
DELETE FROM fixed_expense_alias
WHERE fixed_expense_id = $1 AND alias = $2;

-- name: ResetConfirmedReviewByFixedExpense :exec
UPDATE transaction_review
SET status = 'pending'
WHERE fixed_expense_id = $1
  AND budget_period_id = $2
  AND status = 'confirmed';

-- name: GetFixedExpenseByAlias :one
SELECT fe.id, fe.budget_profile_id, fe.name, fe.planned_amount, fe.category_id,
       fe.payment_method_id, fe.day_of_month, fe.is_active, fe.created_at
FROM fixed_expense fe
JOIN fixed_expense_alias fea ON fea.fixed_expense_id = fe.id
WHERE fea.alias = $1
  AND fe.budget_profile_id = $2
  AND fe.is_active = TRUE
LIMIT 1;
