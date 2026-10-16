-- +goose Up
CREATE TABLE IF NOT EXISTS transaction_review (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    budget_period_id UUID NOT NULL REFERENCES budget_period(id) ON DELETE CASCADE,
    transaction_id  UUID NOT NULL REFERENCES transaction(id) ON DELETE CASCADE,
    fixed_expense_id UUID NOT NULL REFERENCES fixed_expense(id) ON DELETE CASCADE,
    match_score     NUMERIC(5,2) NOT NULL,
    status          TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'confirmed', 'dismissed')),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (transaction_id)
);

CREATE TABLE IF NOT EXISTS fixed_expense_alias (
    id               SERIAL PRIMARY KEY,
    fixed_expense_id UUID NOT NULL REFERENCES fixed_expense(id) ON DELETE CASCADE,
    alias            TEXT NOT NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (fixed_expense_id, alias)
);

-- +goose Down
DROP TABLE IF EXISTS fixed_expense_alias;
DROP TABLE IF EXISTS transaction_review;
