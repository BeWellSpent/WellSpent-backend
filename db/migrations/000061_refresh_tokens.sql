-- +goose Up

-- Device-scoped refresh tokens backing the short-lived (15min) JWT access
-- token. token_hash is a SHA-256 hex digest — the raw token is never stored.
-- lifetime_seconds is carried forward on every rotation (the sliding window).
CREATE TABLE refresh_token (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id TEXT NOT NULL,
    token_hash TEXT NOT NULL,
    lifetime_seconds INTEGER NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ NULL,
    last_used_at TIMESTAMPTZ NULL
);

CREATE UNIQUE INDEX idx_refresh_token_hash ON refresh_token (token_hash);
CREATE INDEX idx_refresh_token_user_device ON refresh_token (user_id, device_id);
CREATE INDEX idx_refresh_token_user ON refresh_token (user_id);

-- +goose Down
DROP TABLE IF EXISTS refresh_token;
