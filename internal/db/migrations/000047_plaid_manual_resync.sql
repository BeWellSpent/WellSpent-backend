-- +goose Up
-- Records when a user last asked for a manual resync of a connection, which is
-- what the once-a-day cooldown on ResyncPlaidConnection is measured against.
--
-- This cannot reuse last_synced_at for two independent reasons:
--   1. A resync clears last_synced_at (that is most of what a resync *is*), so
--      it is gone at exactly the moment the cooldown would need to read it.
--   2. last_synced_at is also what the scheduled job's own "due for a sync"
--      filter uses. Sharing one column would mean a Monday-morning automatic
--      sync silently blocked a Monday-afternoon manual one, even though the
--      two are answering completely different questions.
--
-- NULL means no manual resync has ever been requested, so one is allowed.
ALTER TABLE plaid_item
    ADD COLUMN last_manual_resync_at TIMESTAMPTZ NULL;

-- +goose Down
ALTER TABLE plaid_item DROP COLUMN last_manual_resync_at;
