-- Keep Plaid's own classification of an imported transaction alongside the
-- category we resolved from it.
--
-- Today the mapping in internal/plaid/category.go is applied once at import and
-- the source is discarded, so no improvement to that mapping can ever reach a
-- transaction already in the database — every fix is forward-only, forever.
-- Storing the personal_finance_category pair makes a later re-classification
-- possible rather than impossible.
--
-- reference_number and ppd_id come from Plaid's payment_meta, which is
-- populated for inter-bank transfers (all-null otherwise, and no element is
-- guaranteed). They are the normalized form of identifiers that currently exist
-- only inside the transaction NAME — a Zelle transfer carries the same
-- reference on both legs, and a payroll ACH its PPD ID — so anything wanting to
-- pair or recognise those has a real column to read instead of a string to
-- parse.
--
-- Deliberately NOT backfilled: existing rows keep NULL. Re-deriving them would
-- mean replaying every connection's full history, and the value here is in what
-- gets imported from now on.

-- +goose Up
ALTER TABLE transaction
    ADD COLUMN IF NOT EXISTS plaid_pfc_primary TEXT NULL,
    ADD COLUMN IF NOT EXISTS plaid_pfc_detailed TEXT NULL,
    ADD COLUMN IF NOT EXISTS plaid_reference_number TEXT NULL,
    ADD COLUMN IF NOT EXISTS plaid_ppd_id TEXT NULL;

-- +goose Down
ALTER TABLE transaction
    DROP COLUMN IF EXISTS plaid_pfc_primary,
    DROP COLUMN IF EXISTS plaid_pfc_detailed,
    DROP COLUMN IF EXISTS plaid_reference_number,
    DROP COLUMN IF EXISTS plaid_ppd_id;
