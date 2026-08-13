-- name: CreatePlaidItem :one
INSERT INTO plaid_item (user_id, budget_profile_id, access_token, item_id, institution_id, institution_name)
VALUES ($1, $2, $3, $4, $5, $6)
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at;

-- name: GetPlaidItemByID :one
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at
FROM plaid_item
WHERE id = $1
LIMIT 1;

-- name: GetPlaidItemByItemID :one
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at
FROM plaid_item
WHERE item_id = $1
LIMIT 1;

-- name: ListPlaidItemsByUser :many
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at
FROM plaid_item
WHERE user_id = $1
ORDER BY created_at DESC;

-- name: ListPlaidItemsByBudgetProfile :many
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at
FROM plaid_item
WHERE budget_profile_id = $1
ORDER BY created_at DESC;

-- Returns all active or previously-errored items due for a sync (never
-- synced, or last sync older than 1 day). 'error' is included so a failed
-- item keeps retrying on schedule instead of being silently abandoned —
-- only an explicit disconnect (status='disconnected') stops future syncs.
--
-- Items on a budget with no live period are excluded: their transactions
-- would have nowhere to land anyway, so calling Plaid for them just burns
-- API quota. Note the cursor is left untouched in that case, so nothing is
-- lost — the backlog arrives on the first sync after a period exists,
-- though transactions dated inside the gap will still find no period.
--
-- Ordered by profile so the job can process a budget's connections
-- together and report per-profile rather than per-disconnected-item.
-- name: ListActivePlaidItemsForSync :many
SELECT pi.id, pi.user_id, pi.budget_profile_id, pi.access_token, pi.item_id,
       pi.institution_id, pi.institution_name, pi.status, pi.cursor,
       pi.last_synced_at, pi.created_at
FROM plaid_item pi
WHERE pi.status IN ('active', 'error')
  AND (pi.last_synced_at IS NULL OR pi.last_synced_at < NOW() - INTERVAL '1 day')
  AND EXISTS (
    SELECT 1
    FROM budget_period bp
    WHERE bp.budget_profile_id = pi.budget_profile_id
      AND bp.is_archived = FALSE
  )
ORDER BY pi.budget_profile_id, pi.last_synced_at ASC NULLS FIRST;

-- Connections on budgets the caller owns or belongs to whose owner is on the
-- free plan, and which the sync job therefore skips on every run. Grouped by
-- budget and member so the clients can warn without exposing which
-- institutions anyone banks with.
--
-- Excludes disconnected items: a connection the owner already removed isn't
-- something to warn about.
-- name: ListUnsyncableConnectionsForUser :many
SELECT pi.budget_profile_id,
       bp.name::text          AS budget_name,
       owner.id::uuid         AS member_user_id,
       COALESCE(NULLIF(TRIM(CONCAT(owner.first_name, ' ', owner.last_name)), ''), owner.email)::text AS member_name,
       COUNT(*)::int          AS connection_count
FROM plaid_item pi
JOIN budget_profile bp ON bp.id = pi.budget_profile_id
JOIN users owner ON owner.id = pi.user_id
WHERE owner.plan = 'free'
  AND pi.status <> 'disconnected'
  AND (
    bp.user_id = sqlc.arg('user_id')
    OR EXISTS (
      SELECT 1
      FROM budget_to_profile_mapping m
      WHERE m.budget_profile_id = bp.id
        AND m.user_id = sqlc.arg('user_id')
        AND m.is_active = TRUE
    )
  )
GROUP BY pi.budget_profile_id, bp.name, owner.id, owner.first_name, owner.last_name, owner.email
ORDER BY bp.name, member_name;

-- name: UpdatePlaidItemStatus :one
UPDATE plaid_item
SET status = $2
WHERE id = $1
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at;

-- UpdatePlaidItemSync is only called after a successful SyncTransactions
-- call, so it also clears a prior 'error' status back to 'active'.
-- name: UpdatePlaidItemSync :one
UPDATE plaid_item
SET cursor = sqlc.arg('cursor'), last_synced_at = NOW(), status = 'active'
WHERE id = sqlc.arg('id')::uuid
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at;

-- name: DeletePlaidItem :exec
DELETE FROM plaid_item WHERE id = $1;
