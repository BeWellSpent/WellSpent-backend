-- +goose Up
INSERT INTO category (name, is_system)
SELECT v, TRUE FROM (VALUES ('Shopping'), ('Family')) AS t(v)
WHERE v NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down
DELETE FROM category WHERE name IN ('Shopping', 'Family') AND is_system = TRUE;
