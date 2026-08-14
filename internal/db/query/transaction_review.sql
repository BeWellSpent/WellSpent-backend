-- name: CreateTransactionReview :one
INSERT INTO transaction_review (budget_period_id, transaction_id, matched_transaction_id, match_score)
VALUES ($1, $2, $3, $4)
ON CONFLICT (transaction_id) DO NOTHING
RETURNING id, budget_period_id, transaction_id, matched_transaction_id, match_score, status, created_at;

-- ListTransactionReviews joins transaction twice: once for the
-- flagged/imported transaction (t), once for the matched Fixed-type
-- transaction (mt) — which may be spawned from a FixedExpense template or a
-- SavingsSource, no distinction needed since both are just transactions.
-- Returns pending and confirmed reviews (not dismissed). The frontend filters
-- by status to separate the To-Review queue from already-linked transactions.
-- name: ListTransactionReviews :many
SELECT
    tr.id, tr.budget_period_id, tr.transaction_id, tr.matched_transaction_id,
    tr.match_score, tr.status, tr.created_at,
    t.name  AS transaction_name,
    t.amount AS transaction_amount,
    mt.name AS matched_transaction_name
FROM transaction_review tr
JOIN transaction t  ON t.id  = tr.transaction_id
JOIN transaction mt ON mt.id = tr.matched_transaction_id
JOIN budget_period bp ON bp.id = tr.budget_period_id
WHERE bp.budget_profile_id = $1
  AND tr.status != 'dismissed'
  -- Confirmed reviews are returned for every period, archived included: they
  -- are what the clients use to hide the imported duplicate and draw the link
  -- on the fixed row, so filtering them by period left archived months showing
  -- unlinked duplicates and chevron-less paid rows. Pending ones stay
  -- live-only — an archived period is read-only, so there is nothing the user
  -- could do with one.
  AND (tr.status = 'confirmed' OR bp.is_archived = FALSE)
ORDER BY tr.match_score DESC;

-- name: UpsertTransactionReview :one
INSERT INTO transaction_review (budget_period_id, transaction_id, matched_transaction_id, match_score, status)
VALUES ($1, $2, $3, $4, 'pending')
ON CONFLICT (transaction_id) DO UPDATE SET
    matched_transaction_id = EXCLUDED.matched_transaction_id,
    match_score            = EXCLUDED.match_score,
    status                 = 'pending'
RETURNING id, budget_period_id, transaction_id, matched_transaction_id, match_score, status, created_at;

-- name: GetTransactionReview :one
SELECT id, budget_period_id, transaction_id, matched_transaction_id, match_score, status, created_at
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

-- name: GetConfirmedReviewByMatchedTransaction :one
SELECT id, budget_period_id, transaction_id, matched_transaction_id, match_score, status, created_at
FROM transaction_review
WHERE matched_transaction_id = $1
  AND status = 'confirmed'
LIMIT 1;

-- name: DeleteFixedExpenseAlias :exec
DELETE FROM fixed_expense_alias
WHERE fixed_expense_id = $1 AND alias = $2;

-- name: ResetConfirmedReviewByMatchedTransaction :exec
UPDATE transaction_review
SET status = 'pending'
WHERE matched_transaction_id = $1
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
