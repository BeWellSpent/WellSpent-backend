-- +goose Up

-- Profile-level template for a recurring fixed expense.
-- Spawns one transaction per budget period.
CREATE TABLE fixed_expense (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    budget_profile_id  UUID         NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    name               VARCHAR(100) NOT NULL,
    planned_amount     NUMERIC(15,4) NOT NULL DEFAULT 0,
    category_id        INTEGER      REFERENCES category(id) ON DELETE SET NULL,
    payment_method_id  UUID         REFERENCES payment_methods(id) ON DELETE SET NULL,
    day_of_month       INTEGER      NOT NULL DEFAULT 1,
    is_active          BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at         TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Link period transactions back to their source template.
ALTER TABLE transaction
    ADD COLUMN fixed_expense_id UUID REFERENCES fixed_expense(id) ON DELETE SET NULL;

-- Migrate existing fixed transactions into fixed_expense records.
-- Groups by (profile_id, name, payment_method_id); takes values from the most recent period's transaction.
WITH ranked AS (
    SELECT
        t.name,
        t.planned_amount,
        t.category_id,
        t.payment_method_id,
        COALESCE(EXTRACT(DAY FROM t.date)::int, 1) AS day_of_month,
        bp.id AS profile_id,
        ROW_NUMBER() OVER (
            PARTITION BY bp.id, t.name, t.payment_method_id
            ORDER BY period.start_date DESC
        ) AS rn
    FROM transaction t
    JOIN budget_period period ON period.id = t.budget_period_id
    JOIN budget_profile bp ON bp.id = period.budget_profile_id
    WHERE t.transaction_type_id = (SELECT id FROM transaction_type WHERE name = 'Fixed' LIMIT 1)
),
inserted AS (
    INSERT INTO fixed_expense (budget_profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, is_active)
    SELECT profile_id, name, planned_amount, category_id, payment_method_id, day_of_month, TRUE
    FROM ranked
    WHERE rn = 1
    RETURNING id, budget_profile_id, name, payment_method_id
)
UPDATE transaction t
SET fixed_expense_id = i.id
FROM inserted i
JOIN budget_period period ON period.budget_profile_id = i.budget_profile_id
WHERE t.budget_period_id = period.id
  AND t.name = i.name
  AND (
      t.payment_method_id = i.payment_method_id
      OR (t.payment_method_id IS NULL AND i.payment_method_id IS NULL)
  )
  AND t.transaction_type_id = (SELECT id FROM transaction_type WHERE name = 'Fixed' LIMIT 1);

-- +goose Down

ALTER TABLE transaction DROP COLUMN IF EXISTS fixed_expense_id;
DROP TABLE IF EXISTS fixed_expense;
