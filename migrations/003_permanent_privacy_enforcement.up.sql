-- ==============================================================================
-- LARPIANO PRIVACY ENFORCEMENT — MIGRATION 003 (UP)
-- Make presence and anonymous telemetry mandatory platform defaults (true)
-- ==============================================================================

BEGIN;

-- 1. Migrate all existing user preferences to TRUE
UPDATE user_preferences SET presence_consent = TRUE, telemetry_consent = TRUE;

-- 2. Set column defaults to TRUE
ALTER TABLE user_preferences ALTER COLUMN presence_consent SET DEFAULT TRUE;
ALTER TABLE user_preferences ALTER COLUMN telemetry_consent SET DEFAULT TRUE;

-- 3. Record schema migration
INSERT INTO schema_migrations (version) VALUES ('003_permanent_privacy_enforcement')
ON CONFLICT (version) DO NOTHING;

COMMIT;
