-- name: GetActiveStatusBanner :one
-- At most one banner is ever shown, so the tie-break is part of the contract
-- rather than left to the client: highest severity first, then most recently
-- created. Severity is stored as text for readability in psql during an
-- incident, so the ordering is spelled out here.
SELECT id, severity, message_en, message_es, starts_at, ends_at, created_by, created_at
FROM status_banner
WHERE starts_at <= NOW()
  AND ends_at > NOW()
ORDER BY
    CASE severity
        WHEN 'critical' THEN 3
        WHEN 'warning' THEN 2
        ELSE 1
    END DESC,
    created_at DESC
LIMIT 1;

-- name: CreateStatusBanner :one
-- starts_at is always supplied by the caller (defaulted to now in the service)
-- rather than relying on the column default, so one code path decides it.
INSERT INTO status_banner (severity, message_en, message_es, starts_at, ends_at, created_by)
VALUES ($1, $2, $3, $4, $5, $6)
RETURNING id, severity, message_en, message_es, starts_at, ends_at, created_by, created_at;

-- name: ListStatusBanners :many
-- The operator's history, including expired rows — not the client-facing view.
SELECT id, severity, message_en, message_es, starts_at, ends_at, created_by, created_at
FROM status_banner
ORDER BY created_at DESC
LIMIT $1;

-- name: ExpireStatusBanner :one
-- Takes a banner down early. LEAST/GREATEST keeps this correct for all three
-- states without a branch in Go, and never violates the ends_at >= starts_at
-- constraint:
--   live      → ends_at = NOW()      (stops showing now)
--   scheduled → ends_at = starts_at  (empty window, never shows)
--   expired   → unchanged            (history is not rewritten)
UPDATE status_banner
SET ends_at = LEAST(ends_at, GREATEST(starts_at, NOW()))
WHERE id = $1
RETURNING id, severity, message_en, message_es, starts_at, ends_at, created_by, created_at;
