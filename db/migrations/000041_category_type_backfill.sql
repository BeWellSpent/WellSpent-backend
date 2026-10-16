-- +goose Up

-- Assign the correct type to the two special system categories.
UPDATE category
SET type_id = (SELECT id FROM category_type WHERE name = 'Income')
WHERE is_system = true AND name = 'Income';

UPDATE category
SET type_id = (SELECT id FROM category_type WHERE name = 'Saving')
WHERE is_system = true AND name = 'Savings';

-- Everything else (all Expense-type system categories and existing user categories) defaults to Expense.
UPDATE category
SET type_id = (SELECT id FROM category_type WHERE name = 'Expense')
WHERE type_id IS NULL;

-- +goose Down

UPDATE category SET type_id = NULL;
