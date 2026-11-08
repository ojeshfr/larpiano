-- ==============================================================================
-- LARPIANO AUTH — MIGRATION 002 (DOWN)
-- Rolls back auth schema additions safely
-- ==============================================================================

BEGIN;

DROP TABLE IF EXISTS auth_audit_logs;
DROP TABLE IF EXISTS password_reset_tokens;
DROP TABLE IF EXISTS email_verification_codes;
DROP TABLE IF EXISTS user_sessions;

ALTER TABLE users DROP COLUMN IF EXISTS updated_at;

DELETE FROM schema_migrations WHERE version = '002_auth_schema';

COMMIT;
