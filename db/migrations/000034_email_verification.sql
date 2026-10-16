-- +goose Up
ALTER TABLE users
    ADD COLUMN email_verification_token UUID NULL,
    ADD COLUMN email_verification_expires_at TIMESTAMPTZ NULL,
    ADD COLUMN email_verification_last_sent_at TIMESTAMPTZ NULL;

CREATE UNIQUE INDEX idx_users_email_verification_token ON users (email_verification_token)
    WHERE email_verification_token IS NOT NULL;

-- +goose Down
DROP INDEX IF EXISTS idx_users_email_verification_token;
ALTER TABLE users
    DROP COLUMN IF EXISTS email_verification_token,
    DROP COLUMN IF EXISTS email_verification_expires_at,
    DROP COLUMN IF EXISTS email_verification_last_sent_at;
