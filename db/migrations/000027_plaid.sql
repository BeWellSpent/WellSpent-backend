-- +goose Up

CREATE TABLE plaid_item (
    id                UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id           UUID        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    budget_profile_id UUID        NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    access_token      TEXT        NOT NULL,
    item_id           TEXT        NOT NULL UNIQUE,
    institution_id    TEXT,
    institution_name  TEXT,
    status            TEXT        NOT NULL DEFAULT 'active'
                                    CHECK (status IN ('active', 'disconnected', 'error')),
    cursor            TEXT,
    last_synced_at    TIMESTAMPTZ,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_plaid_item_user_id ON plaid_item(user_id);
CREATE INDEX idx_plaid_item_budget_profile_id ON plaid_item(budget_profile_id);

-- +goose Down

DROP TABLE IF EXISTS plaid_item;
