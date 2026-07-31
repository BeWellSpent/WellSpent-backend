-- name: UpsertDeviceToken :one
INSERT INTO device_token (user_id, platform, token)
VALUES (sqlc.arg('user_id'), sqlc.arg('platform'), sqlc.arg('token'))
ON CONFLICT (token)
DO UPDATE SET
    user_id    = EXCLUDED.user_id,
    platform   = EXCLUDED.platform,
    updated_at = NOW()
RETURNING id, user_id, platform, token, created_at, updated_at;

-- name: ListDeviceTokensForUser :many
SELECT id, user_id, platform, token, created_at, updated_at
FROM device_token
WHERE user_id = $1;
