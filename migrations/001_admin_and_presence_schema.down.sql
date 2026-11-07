-- ==============================================================================
-- LARPIANO CONTROL CENTER — MIGRATION 001 (DOWN)
-- Safe Rollback for Migration 001
-- ==============================================================================

BEGIN;

DROP TABLE IF EXISTS playground_events CASCADE;
DROP TABLE IF EXISTS admin_audit_logs CASCADE;
DROP TABLE IF EXISTS daily_analytics CASCADE;
DROP TABLE IF EXISTS announcements CASCADE;
DROP TABLE IF EXISTS user_preferences CASCADE;
DROP TABLE IF EXISTS app_sessions CASCADE;
DROP TABLE IF EXISTS users CASCADE;

DELETE FROM schema_migrations WHERE version = '001_admin_and_presence_schema';

COMMIT;
