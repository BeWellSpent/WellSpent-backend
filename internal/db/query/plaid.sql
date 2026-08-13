-- name: CreatePlaidItem :one
INSERT INTO plaid_item (user_id, budget_profile_id, access_token, item_id, institution_id, institution_name)
VALUES ($1, $2, $3, $4, $5, $6)
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at, last_manual_resync_at;

-- name: GetPlaidItemByID :one
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at, last_manual_resync_at
FROM plaid_item
WHERE id = $1
LIMIT 1;

-- name: GetPlaidItemByItemID :one
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at, last_manual_resync_at
FROM plaid_item
WHERE item_id = $1
LIMIT 1;

-- name: ListPlaidItemsByUser :many
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at, last_manual_resync_at
FROM plaid_item
WHERE user_id = $1
ORDER BY created_at DESC;

-- name: ListPlaidItemsByBudgetProfile :many
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at, last_manual_resync_at
FROM plaid_item
WHERE budget_profile_id = $1
ORDER BY created_at DESC;

-- Every live connection feeding one budget, with the member who linked it.
--
-- Unlike ListPlaidItemsByBudgetProfile this crosses into `users` for the
-- owner's display name and plan, because a budget's manage view shows several
-- members' banks side by side: a bare "Error" row is unactionable without
-- knowing whose connection it is, and a perfectly healthy row that imports
-- nothing (free-plan owner, skipped by the sync job on every run) is
-- indistinguishable from a working one without the plan.
--
-- Disconnected items are excluded — the owner already removed them, and they
-- would otherwise accumulate in a shared view nobody else can act on.
-- name: ListActivePlaidItemsWithOwnerByBudgetProfile :many
SELECT pi.id, pi.user_id, pi.budget_profile_id, pi.access_token, pi.item_id,
       pi.institution_id, pi.institution_name, pi.status, pi.cursor,
       pi.last_synced_at, pi.created_at, pi.last_manual_resync_at,
       COALESCE(NULLIF(TRIM(CONCAT(owner.first_name, ' ', owner.last_name)), ''), owner.email)::text AS owner_name,
       owner.plan::text AS owner_plan
FROM plaid_item pi
JOIN users owner ON owner.id = pi.user_id
WHERE pi.budget_profile_id = $1
  AND pi.status <> 'disconnected'
ORDER BY owner_name, pi.created_at DESC;

-- Clears the sync cursor so the next sync replays the item's full history
-- from Plaid, and stamps the manual-resync time the cooldown is measured
-- against.
--
-- last_synced_at is cleared too, so the scheduled job treats the item as never
-- synced and picks it up even if this run's immediate sync fails. Status
-- returns to 'active' for the same reason UpdatePlaidItemSync resets it: a
-- resync is an explicit "try again" on a connection that may have been left
-- in 'error', and the sync that follows immediately will set it back if the
-- underlying problem is still there.
-- name: ResetPlaidItemCursor :one
UPDATE plaid_item
SET cursor = NULL, last_synced_at = NULL, last_manual_resync_at = NOW(), status = 'active'
WHERE id = $1
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at, last_manual_resync_at;

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
       pi.last_synced_at, pi.created_at, pi.last_manual_resync_at
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
          status, cursor, last_synced_at, created_at, last_manual_resync_at;

-- UpdatePlaidItemSync is only called after a successful SyncTransactions
-- call, so it also clears a prior 'error' status back to 'active'.
-- name: UpdatePlaidItemSync :one
UPDATE plaid_item
SET cursor = sqlc.arg('cursor'), last_synced_at = NOW(), status = 'active'
WHERE id = sqlc.arg('id')::uuid
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at, last_manual_resync_at;

-- name: DeletePlaidItem :exec
DELETE FROM plaid_item WHERE id = $1;
