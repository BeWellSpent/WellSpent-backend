-- +goose Up
-- Marks accounts that are exempt from the email-verification gate both
-- clients enforce (see docs/features/block-all-applications-until-email-is-confirmed.md).
--
-- 'test' exists so automated and manual QA accounts can reach the app without
-- a real inbox. It is set by hand, never by registration — there is
-- deliberately no pattern or env var that can mark an account automatically,
-- since a stray wildcard in production config would silently switch
-- verification off for real signups.
--
-- To exempt an account:
--   UPDATE users SET account_type = 'test' WHERE email = 'qa@example.com';
ALTER TABLE users
    ADD COLUMN account_type VARCHAR NOT NULL DEFAULT 'standard'
        CHECK (account_type IN ('standard', 'test'));

-- +goose Down
ALTER TABLE users DROP COLUMN account_type;
