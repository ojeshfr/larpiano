-- ==============================================================================
-- LARPIANO PRIVACY ENFORCEMENT — MIGRATION 003 (DOWN)
-- Revert mandatory presence and anonymous telemetry defaults
-- ==============================================================================

BEGIN;

ALTER TABLE user_preferences ALTER COLUMN presence_consent SET DEFAULT FALSE;
ALTER TABLE user_preferences ALTER COLUMN telemetry_consent SET DEFAULT FALSE;

DELETE FROM schema_migrations WHERE version = '003_permanent_privacy_enforcement';

COMMIT;
