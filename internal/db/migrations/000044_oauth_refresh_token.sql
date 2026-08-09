-- +goose Up
-- Apple requires apps offering Sign in with Apple to revoke the user's tokens
-- when their account is deleted (App Store Review 5.1.1(v)). Revocation needs
-- the refresh token Apple returns when the one-time authorization_code is
-- exchanged, so it has to be persisted at link time.
--
-- Stored encrypted (AES-256-GCM via internal/crypto, keyed by ENCRYPTION_KEY),
-- the same treatment plaid_item.access_token already gets. Nullable because
-- Google rows never have one, and because the exchange is best-effort — a
-- failed exchange must not block sign-in.
ALTER TABLE oauth_account
    ADD COLUMN refresh_token TEXT NULL;

-- +goose Down
ALTER TABLE oauth_account
    DROP COLUMN IF EXISTS refresh_token;
