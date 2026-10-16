-- Adds the two system categories Plaid data needs but the seed set never had.
--
-- Transportation: TRANSPORTATION previously defaulted to Misc, so public
-- transit, ride shares, parking and bikes were all filed as miscellaneous —
-- only gas and tolls broke out (to Gas).
--
-- Utilities: RENT_AND_UTILITIES defaulted to Rent, so electricity, internet,
-- phone, water and waste were all filed as housing rent.
--
-- No backfill of existing transactions is possible: `transaction` doesn't
-- store the Plaid personal_finance_category it was imported under, so there's
-- no way to tell which existing Misc/Rent rows came from these primaries.
-- Already-imported transactions keep their current category.

-- +goose Up
INSERT INTO category (name, is_system)
SELECT v, TRUE FROM (VALUES ('Transportation'), ('Utilities')) AS t(v)
WHERE v NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down
DELETE FROM category WHERE name IN ('Transportation', 'Utilities') AND is_system = TRUE;
