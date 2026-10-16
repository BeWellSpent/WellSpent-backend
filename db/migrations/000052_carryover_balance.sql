-- Carryover balance between budget periods (issue #53).
--
-- When a period closes, its ending balance (income - actual spend) can be
-- carried into the next one instead of evaporating: a leftover becomes a
-- Savings transaction, a shortfall becomes Debt transactions split across the
-- payment methods it was spent on.
--
-- carryover_enabled is off by default. The cycling job runs unattended, so
-- "carry my balance forward" has to be a stored decision — there is nobody to
-- prompt at 06:00 UTC.
--
-- carried_from_budget_period_id serves two purposes: it names the period a row
-- came from (so a client can caption it), and it is the idempotency key.
-- createNextPeriod has no transaction wrapper and is called both by the daily
-- job and by a client-callable RPC, so the carryover step has to be able to
-- tell that it already ran for a given closing period.
--
-- ON DELETE SET NULL, not CASCADE: deleting an old period must not delete the
-- transactions that carried its balance forward — those live in the *new*
-- period and are the user's current obligations.
--
-- Debt is the counterpart to the existing Savings system category. A shortfall
-- has had nowhere to go until now.

-- +goose Up
ALTER TABLE budget_profile
    ADD COLUMN IF NOT EXISTS carryover_enabled BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE transaction
    ADD COLUMN IF NOT EXISTS carried_from_budget_period_id UUID NULL
        REFERENCES budget_period(id) ON DELETE SET NULL;

-- Supports the idempotency lookup: "does this period already hold rows carried
-- from that one?" Partial, since the column is null for every ordinary
-- transaction and those are the overwhelming majority.
CREATE INDEX IF NOT EXISTS idx_transaction_carried_from
    ON transaction (budget_period_id, carried_from_budget_period_id)
    WHERE carried_from_budget_period_id IS NOT NULL;

INSERT INTO category (name, is_system)
SELECT v, TRUE FROM (VALUES ('Debt')) AS t(v)
WHERE v NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down
DROP INDEX IF EXISTS idx_transaction_carried_from;

ALTER TABLE transaction DROP COLUMN IF EXISTS carried_from_budget_period_id;

ALTER TABLE budget_profile DROP COLUMN IF EXISTS carryover_enabled;

DELETE FROM category WHERE name = 'Debt' AND is_system = TRUE;
