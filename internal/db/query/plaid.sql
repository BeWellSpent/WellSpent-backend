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

-- Returns all active items due for a sync (never synced, or last sync older than 1 day).
-- name: ListActivePlaidItemsForSync :many
SELECT id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
       status, cursor, last_synced_at, created_at
FROM plaid_item
WHERE status = 'active'
  AND (last_synced_at IS NULL OR last_synced_at < NOW() - INTERVAL '1 day')
ORDER BY last_synced_at ASC NULLS FIRST;

-- name: UpdatePlaidItemStatus :one
UPDATE plaid_item
SET status = $2
WHERE id = $1
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at;

-- name: UpdatePlaidItemSync :one
UPDATE plaid_item
SET cursor = sqlc.arg('cursor'), last_synced_at = NOW()
WHERE id = sqlc.arg('id')::uuid
RETURNING id, user_id, budget_profile_id, access_token, item_id, institution_id, institution_name,
          status, cursor, last_synced_at, created_at;

-- name: DeletePlaidItem :exec
DELETE FROM plaid_item WHERE id = $1;
