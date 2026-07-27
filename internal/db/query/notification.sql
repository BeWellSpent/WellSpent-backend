-- ─── Alert subscriptions ──────────────────────────────────────────────────────

-- name: ListAlertSubscriptions :many
SELECT id, user_id, budget_profile_id, alert_type, channel,
       threshold_pct, threshold_scope, category_id, notify_all_members, created_at
FROM alert_subscription
WHERE user_id = $1 AND budget_profile_id = $2
ORDER BY created_at;

-- name: GetAlertSubscription :one
SELECT id, user_id, budget_profile_id, alert_type, channel,
       threshold_pct, threshold_scope, category_id, notify_all_members, created_at
FROM alert_subscription
WHERE id = $1 AND user_id = $2;

-- name: UpsertAlertSubscription :one
INSERT INTO alert_subscription
    (user_id, budget_profile_id, alert_type, channel, threshold_pct, threshold_scope, category_id, notify_all_members)
VALUES
    (sqlc.arg('user_id'), sqlc.arg('budget_profile_id'), sqlc.arg('alert_type'), sqlc.arg('channel'),
     sqlc.arg('threshold_pct'), sqlc.arg('threshold_scope'), sqlc.arg('category_id'), sqlc.arg('notify_all_members'))
ON CONFLICT (user_id, budget_profile_id, alert_type, COALESCE(category_id, -1))
DO UPDATE SET
    channel            = EXCLUDED.channel,
    threshold_pct      = EXCLUDED.threshold_pct,
    threshold_scope    = EXCLUDED.threshold_scope,
    notify_all_members = EXCLUDED.notify_all_members
RETURNING id, user_id, budget_profile_id, alert_type, channel,
          threshold_pct, threshold_scope, category_id, notify_all_members, created_at;

-- name: DeleteAlertSubscription :exec
DELETE FROM alert_subscription WHERE id = $1 AND user_id = $2;

-- Get all subscribers for an event on a budget (for notification fan-out).
-- name: GetBudgetAlertSubscribers :many
SELECT id, user_id, budget_profile_id, alert_type, channel,
       threshold_pct, threshold_scope, category_id, notify_all_members, created_at
FROM alert_subscription
WHERE budget_profile_id = $1 AND alert_type = $2;

-- ─── Notifications ────────────────────────────────────────────────────────────

-- name: CreateNotification :one
INSERT INTO notification (user_id, budget_profile_id, alert_type, title, body)
VALUES ($1, $2, $3, $4, $5)
RETURNING id, user_id, budget_profile_id, alert_type, title, body, is_read, created_at;

-- name: ListNotifications :many
SELECT id, user_id, budget_profile_id, alert_type, title, body, is_read, created_at
FROM notification
WHERE user_id = $1
  AND (sqlc.arg('budget_profile_id')::uuid IS NULL OR budget_profile_id = sqlc.arg('budget_profile_id'))
ORDER BY created_at DESC
LIMIT sqlc.arg('limit_val')::int;

-- name: GetUnreadCount :one
SELECT COUNT(*)::int AS count
FROM notification
WHERE user_id = $1 AND is_read = FALSE;

-- name: MarkNotificationsRead :exec
UPDATE notification
SET is_read = TRUE
WHERE user_id = $1
  AND (ARRAY_LENGTH(sqlc.arg('ids')::uuid[], 1) IS NULL OR id = ANY(sqlc.arg('ids')::uuid[]));
