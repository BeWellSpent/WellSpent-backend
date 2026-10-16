-- +goose Up
-- Operator-authored banners shown at the top of every client (web + iOS).
--
-- Deliberately not folded into the `notification` table: a notification is
-- addressed to one user, about their own budget, and is read once. A banner is
-- global, readable without a token, and true for everyone until it expires.
-- Sharing a table would mean every read of one carrying a NULL-heavy filter for
-- the other.
CREATE TABLE status_banner (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    severity   TEXT NOT NULL CHECK (severity IN ('info', 'warning', 'critical')),

    -- Both languages live on the row rather than one string being translated at
    -- read time. Capped at 300 characters to match the clients, which show one
    -- line and expand the rest behind a "learn more" toggle. message_es may be
    -- empty; clients fall back to English.
    message_en TEXT NOT NULL CHECK (message_en <> '' AND char_length(message_en) <= 300),
    message_es TEXT NOT NULL DEFAULT '' CHECK (char_length(message_es) <= 300),

    -- The active window. A banner is live when starts_at <= NOW() < ends_at, so
    -- it takes itself down rather than relying on someone remembering to.
    starts_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    ends_at    TIMESTAMPTZ NOT NULL,

    -- Who posted it. RESTRICT, not CASCADE: deleting an operator account must
    -- not silently erase the record of what was announced during an incident.
    created_by UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- >=, not >, so an empty window is representable. Taking a *scheduled*
    -- banner down before it ever starts collapses ends_at onto starts_at; a
    -- strict > would reject that and leave no way to cancel one early.
    CONSTRAINT status_banner_window CHECK (ends_at >= starts_at)
);

-- Every unauthenticated client hits the active-banner lookup on load, so it is
-- by far the hottest query here. Indexed on the window columns it filters on.
CREATE INDEX status_banner_active ON status_banner (ends_at, starts_at);

-- +goose Down
DROP TABLE IF EXISTS status_banner;
