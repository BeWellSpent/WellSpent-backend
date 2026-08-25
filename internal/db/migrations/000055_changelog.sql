-- +goose Up
-- Per-release notes, kept per shipping component and shown to a reader the
-- first time they open a version.
--
-- Two tables rather than one row per item carrying its own version: a release
-- is a real thing with a version and a ship date, and splitting it lets
-- UNIQUE (component, version) make a duplicated release impossible rather than
-- something to notice later. Flattening it would leave "v1.27.0 web" spelled
-- twice with two different dates as a representable state.
CREATE TABLE changelog_release (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Which shipping artifact this describes. Background jobs are absent on
    -- purpose: cycle-budgets and plaid-sync ship inside the server image and
    -- have no separately visible version.
    component   TEXT NOT NULL CHECK (component IN ('web', 'ios', 'server')),

    -- As that component spells it. Web and server use semver; iOS uses its
    -- MARKETING_VERSION, not the build number — a changelog entry describes a
    -- release a reader was given, and the build number moves every feature
    -- while the marketing version deliberately does not.
    version     TEXT NOT NULL CHECK (version <> '' AND char_length(version) <= 40),

    -- When the version reached users, which is not when the row was written:
    -- notes are authored in the repo alongside the code and published when the
    -- build actually ships.
    released_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    -- RESTRICT, not CASCADE, matching status_banner: deleting an operator
    -- account must not erase the record of what was announced.
    created_by  UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT changelog_release_unique_version UNIQUE (component, version)
);

-- Every client asks for "my component plus server, newest first" on load.
CREATE INDEX changelog_release_lookup ON changelog_release (component, released_at DESC);

CREATE TABLE changelog_item (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- CASCADE here, unlike created_by above: an item has no meaning apart from
    -- the release it belongs to, so it should never outlive one.
    release_id  UUID NOT NULL REFERENCES changelog_release(id) ON DELETE CASCADE,

    change_type TEXT NOT NULL CHECK (change_type IN ('added', 'fixed', 'changed')),

    -- Both languages on the row, same as status_banner. summary_es may be
    -- empty; clients fall back to English rather than showing nothing.
    summary_en  TEXT NOT NULL CHECK (summary_en <> '' AND char_length(summary_en) <= 300),
    summary_es  TEXT NOT NULL DEFAULT '' CHECK (char_length(summary_es) <= 300),

    -- Preserves the order the operator wrote them in. Grouping by change_type
    -- is left to the clients, since that is presentation.
    position    INTEGER NOT NULL DEFAULT 0,

    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX changelog_item_release ON changelog_item (release_id, position);

-- +goose Down
DROP TABLE IF EXISTS changelog_item;
DROP TABLE IF EXISTS changelog_release;
