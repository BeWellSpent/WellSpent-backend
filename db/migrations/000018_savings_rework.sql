-- +goose Up
ALTER TABLE savings_source
    ADD COLUMN IF NOT EXISTS payment_method_id UUID REFERENCES payment_methods(id);

INSERT INTO category (name, is_system)
SELECT 'Savings', TRUE
WHERE 'Savings' NOT IN (SELECT name FROM category WHERE is_system = TRUE);

-- +goose Down
DELETE FROM category WHERE name = 'Savings' AND is_system = TRUE;

ALTER TABLE savings_source
    DROP COLUMN IF EXISTS payment_method_id;
