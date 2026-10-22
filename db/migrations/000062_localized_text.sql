-- +goose Up

-- First JSONB column in this codebase (issue #89): message/summary become a
-- single locale-keyed map instead of fixed en/es columns, so adding a third
-- language needs no migration. "en" is enforced required in the app layer,
-- not via a DB CHECK, same low-write/no-admin-UI posture as before.
ALTER TABLE status_banner ADD COLUMN message JSONB;
UPDATE status_banner SET message =
    jsonb_build_object('en', message_en) ||
    CASE WHEN message_es IS NOT NULL AND message_es <> '' THEN jsonb_build_object('es', message_es) ELSE '{}'::jsonb END;
ALTER TABLE status_banner ALTER COLUMN message SET NOT NULL;
ALTER TABLE status_banner DROP COLUMN message_en;
ALTER TABLE status_banner DROP COLUMN message_es;

ALTER TABLE changelog_item ADD COLUMN summary JSONB;
UPDATE changelog_item SET summary =
    jsonb_build_object('en', summary_en) ||
    CASE WHEN summary_es IS NOT NULL AND summary_es <> '' THEN jsonb_build_object('es', summary_es) ELSE '{}'::jsonb END;
ALTER TABLE changelog_item ALTER COLUMN summary SET NOT NULL;
ALTER TABLE changelog_item DROP COLUMN summary_en;
ALTER TABLE changelog_item DROP COLUMN summary_es;

-- +goose Down
ALTER TABLE changelog_item ADD COLUMN summary_en TEXT NOT NULL DEFAULT '';
ALTER TABLE changelog_item ADD COLUMN summary_es TEXT NOT NULL DEFAULT '';
UPDATE changelog_item SET summary_en = COALESCE(summary->>'en', ''), summary_es = COALESCE(summary->>'es', '');
ALTER TABLE changelog_item DROP COLUMN summary;

ALTER TABLE status_banner ADD COLUMN message_en TEXT NOT NULL DEFAULT '';
ALTER TABLE status_banner ADD COLUMN message_es TEXT NOT NULL DEFAULT '';
UPDATE status_banner SET message_en = COALESCE(message->>'en', ''), message_es = COALESCE(message->>'es', '');
ALTER TABLE status_banner DROP COLUMN message;
