-- name: ListChangelogReleases :many
-- Newest first per component. The caller passes the components it wants — a
-- client asks for its own plus 'server'; the Help browser asks for all three.
-- An empty array means all, so the "browse everything" case needs no second
-- query.
SELECT id, component, version, released_at, created_by, created_at
FROM changelog_release
WHERE cardinality(@components::text[]) = 0
   OR component = ANY (@components::text[])
ORDER BY component, released_at DESC, created_at DESC;

-- name: ListChangelogItemsForReleases :many
-- Every item for a set of releases in one round trip, rather than one query
-- per release — the Help browser can ask for dozens at a time.
SELECT id, release_id, change_type, summary_en, summary_es, position, created_at
FROM changelog_item
WHERE release_id = ANY (@release_ids::uuid[])
ORDER BY release_id, position, created_at;

-- name: CreateChangelogRelease :one
-- released_at is always supplied by the caller (defaulted to now in the
-- service) rather than relying on the column default, so one code path decides
-- it. A duplicate (component, version) violates the unique constraint, which
-- the service maps to a Duplicate error.
INSERT INTO changelog_release (component, version, released_at, created_by)
VALUES ($1, $2, $3, $4)
RETURNING id, component, version, released_at, created_by, created_at;

-- name: CreateChangelogItem :one
INSERT INTO changelog_item (release_id, change_type, summary_en, summary_es, position)
VALUES ($1, $2, $3, $4, $5)
RETURNING id, release_id, change_type, summary_en, summary_es, position, created_at;
