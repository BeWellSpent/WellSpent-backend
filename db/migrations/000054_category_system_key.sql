-- Give every seeded system category a stable, language-independent key
-- (issue #49).
--
-- Until now the only way to identify a system category was its English name,
-- and 20 places across the two clients and this backend did exactly that --
-- `name == "Income"`, `name == "Savings"`, and a 40-entry Plaid mapping keyed
-- on names. That is what made the names impossible to localize: the moment
-- `name` came back translated, every one of those comparisons would silently
-- stop matching. Income would stop being excluded from spending totals and
-- Savings would lose its special-casing, with no error raised anywhere.
--
-- Stored as TEXT rather than the proto enum's integer so it stays readable in
-- psql during an incident, and so renumbering the enum can never silently
-- repoint existing rows. `convert.go` maps it to v1.SystemCategory.
--
-- `name` deliberately keeps its English value. It is the fallback a client
-- renders when it receives a system_key it does not recognise -- a category
-- seeded after that client shipped still displays sensibly instead of as a
-- raw key.

-- +goose Up

ALTER TABLE category ADD COLUMN IF NOT EXISTS system_key TEXT NULL;

-- Backfill by name. This is the last time these English names are used as an
-- identifier; from here on system_key is the identity and name is display.
UPDATE category SET system_key = v.key
FROM (VALUES
    ('Entertainment',  'entertainment'),
    ('Insurance',      'insurance'),
    ('Loan',           'loan'),
    ('Wellness',       'wellness'),
    ('Services',       'services'),
    ('Subscription',   'subscription'),
    ('Rent',           'rent'),
    ('Travel',         'travel'),
    ('Eating Out',     'eating_out'),
    ('Groceries',      'groceries'),
    ('Baby',           'baby'),
    ('Pet',            'pet'),
    ('Misc',           'misc'),
    ('House',          'house'),
    ('Gas',            'gas'),
    ('Auto',           'auto'),
    ('Savings',        'savings'),
    ('Shopping',       'shopping'),
    ('Family',         'family'),
    ('Income',         'income'),
    ('Payment',        'payment'),
    ('Transfer',       'transfer'),
    ('Transportation', 'transportation'),
    ('Utilities',      'utilities'),
    ('Debt',           'debt')
) AS v(name, key)
WHERE category.is_system = TRUE AND category.name = v.name;

-- One row per key. Partial, because the column is NULL for every user-created
-- category and those are the overwhelming majority. This is also what stops a
-- future seed migration from quietly creating a second "income" category:
-- the duplicate insert fails loudly instead.
CREATE UNIQUE INDEX IF NOT EXISTS idx_category_system_key
    ON category (system_key)
    WHERE system_key IS NOT NULL;

-- +goose Down

DROP INDEX IF EXISTS idx_category_system_key;
ALTER TABLE category DROP COLUMN IF EXISTS system_key;
