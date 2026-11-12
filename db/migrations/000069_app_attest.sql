-- +goose Up

-- Issue #121: Apple App Attest. A key is attested once per app install; later requests
-- sign a one-time challenge with it. sign_count only ever rises (replay protection).
CREATE TABLE app_attest_key (
  key_id       TEXT PRIMARY KEY,
  public_key   BYTEA NOT NULL,
  sign_count   BIGINT NOT NULL DEFAULT 0,
  environment  TEXT NOT NULL CHECK (environment IN ('production', 'development')),
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_used_at TIMESTAMPTZ
);

-- Shared by every API instance, so a challenge can't be redeemed twice across them.
CREATE TABLE app_attest_challenge (
  challenge  TEXT PRIMARY KEY,
  expires_at TIMESTAMPTZ NOT NULL,
  used_at    TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX app_attest_challenge_expires_at ON app_attest_challenge (expires_at);

-- False when the account was created without attestation (a device that couldn't attest).
ALTER TABLE users ADD COLUMN attested BOOLEAN NOT NULL DEFAULT TRUE;

-- +goose Down
ALTER TABLE users DROP COLUMN IF EXISTS attested;
DROP TABLE IF EXISTS app_attest_challenge;
DROP TABLE IF EXISTS app_attest_key;
