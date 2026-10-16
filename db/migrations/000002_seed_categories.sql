-- +goose Up

ALTER TABLE category ADD COLUMN IF NOT EXISTS is_system BOOLEAN NOT NULL DEFAULT FALSE;

INSERT INTO category (name, is_system)
    SELECT v, TRUE FROM (VALUES
        ('Entertainment'), ('Insurance'), ('Loan'), ('Wellness'),
        ('Services'), ('Subscription'), ('Rent'), ('Travel'),
        ('Eating Out'), ('Groceries'), ('Baby'), ('Pet'), ('Misc'), ('House')
    ) AS t(v)
    WHERE v NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down

DELETE FROM category WHERE is_system = TRUE;
ALTER TABLE category DROP COLUMN IF EXISTS is_system;
