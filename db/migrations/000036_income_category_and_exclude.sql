-- +goose Up
ALTER TABLE transaction ADD COLUMN is_excluded BOOLEAN NOT NULL DEFAULT FALSE;

INSERT INTO category (name, is_system)
SELECT v, TRUE FROM (VALUES ('Income')) AS t(v)
WHERE v NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down
DELETE FROM category WHERE name = 'Income' AND is_system = TRUE;
ALTER TABLE transaction DROP COLUMN is_excluded;
