-- +goose Up

CREATE TABLE alert_subscription (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id             UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    budget_profile_id   UUID NOT NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    alert_type          TEXT NOT NULL CHECK (alert_type IN ('spending_threshold', 'new_transaction', 'review_pending', 'period_created')),
    channel             TEXT NOT NULL DEFAULT 'in_app' CHECK (channel IN ('email', 'in_app', 'both')),
    threshold_pct       NUMERIC NULL,   -- 0-100; spending_threshold only
    threshold_scope     TEXT NULL CHECK (threshold_scope IN ('budget', 'category')),  -- spending_threshold only
    category_id         INTEGER NULL REFERENCES category(id) ON DELETE CASCADE,       -- category-scope only
    notify_all_members  BOOLEAN NOT NULL DEFAULT FALSE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- One subscription per (user, budget, type, category-or-budget-scope).
-- COALESCE maps NULL category_id (budget-scope) to -1 so the expression is unique.
CREATE UNIQUE INDEX alert_subscription_unique
    ON alert_subscription (user_id, budget_profile_id, alert_type, COALESCE(category_id, -1));

CREATE TABLE notification (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id           UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    budget_profile_id UUID NULL REFERENCES budget_profile(id) ON DELETE CASCADE,
    alert_type        TEXT NOT NULL,
    title             TEXT NOT NULL,
    body              TEXT NOT NULL,
    is_read           BOOLEAN NOT NULL DEFAULT FALSE,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX notification_user_unread ON notification (user_id, is_read, created_at DESC);

-- +goose Down
DROP TABLE IF EXISTS notification;
DROP TABLE IF EXISTS alert_subscription;
