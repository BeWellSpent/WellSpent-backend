-- +goose Up
ALTER TABLE users
    ADD COLUMN plan VARCHAR NOT NULL DEFAULT 'free'
        CHECK (plan IN ('free', 'pro', 'lifetime'));

UPDATE users SET plan = 'lifetime' WHERE email = 'mauro.afa91@gmail.com';

-- +goose Down
ALTER TABLE users DROP COLUMN plan;
