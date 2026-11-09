-- ==============================================================================
-- LARPIANO CONTROL CENTER — MIGRATION 004 (DOWN)
-- Revert Authoritative Releases Schema
-- ==============================================================================

BEGIN;

DROP TABLE IF EXISTS app_releases;

COMMIT;
