-- ==============================================================================
-- LARPIANO CONTROL CENTER — MIGRATION 001 (DOWN)
-- Safe Rollback for Migration 001
-- Explicit reverse-dependency order without unsafe CASCADE
-- ==============================================================================

BEGIN;

DROP TABLE IF EXISTS playground_events;
DROP TABLE IF EXISTS admin_audit_logs;
DROP TABLE IF EXISTS daily_analytics;
DROP TABLE IF EXISTS announcements;
DROP TABLE IF EXISTS user_preferences;
DROP TABLE IF EXISTS app_sessions;
DROP TABLE IF EXISTS users;

DELETE FROM schema_migrations WHERE version = '001_admin_and_presence_schema';

COMMIT;
