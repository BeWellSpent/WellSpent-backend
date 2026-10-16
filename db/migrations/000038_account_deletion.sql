-- +goose Up
ALTER TABLE users
  ADD COLUMN status      VARCHAR NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'blocked', 'disabled')),
  ADD COLUMN active_until TIMESTAMPTZ NULL;

-- +goose Down
ALTER TABLE users
  DROP COLUMN IF EXISTS active_until,
  DROP COLUMN IF EXISTS status;
