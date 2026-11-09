-- ==============================================================================
-- LARPIANO CONTROL CENTER — MIGRATION 001 (UP)
-- Additive Schema: Users, Sessions, Preferences, Announcements, Analytics, Audit Logs
-- ==============================================================================

BEGIN;

-- Enable UUID extension if not already available
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- 1. Users Table
CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    email VARCHAR(255) UNIQUE NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    password_hash VARCHAR(255),
    email_verified BOOLEAN DEFAULT FALSE NOT NULL,
    role VARCHAR(50) DEFAULT 'user' NOT NULL,
    account_status VARCHAR(50) DEFAULT 'active' NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_login_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_users_email ON users(email);
CREATE INDEX IF NOT EXISTS idx_users_role ON users(role);
CREATE INDEX IF NOT EXISTS idx_users_status ON users(account_status);

-- 2. App Sessions (Heartbeats, Online Presence & Telemetry)
CREATE TABLE IF NOT EXISTS app_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    session_identifier VARCHAR(100) UNIQUE NOT NULL,
    app_version VARCHAR(50) NOT NULL,
    started_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_seen_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    ended_at TIMESTAMPTZ,
    current_status VARCHAR(50) DEFAULT 'online' NOT NULL,
    active_song VARCHAR(255),
    piano_layout VARCHAR(100),
    tempo VARCHAR(20)
);

CREATE INDEX IF NOT EXISTS idx_app_sessions_last_seen ON app_sessions(last_seen_at);
CREATE INDEX IF NOT EXISTS idx_app_sessions_status ON app_sessions(current_status);
CREATE INDEX IF NOT EXISTS idx_app_sessions_user_id ON app_sessions(user_id);

-- 3. User Preferences & Consents
CREATE TABLE IF NOT EXISTS user_preferences (
    user_id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
    telemetry_consent BOOLEAN DEFAULT TRUE NOT NULL,
    presence_consent BOOLEAN DEFAULT TRUE NOT NULL,
    playground_effects_enabled BOOLEAN DEFAULT TRUE NOT NULL,
    updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL
);

-- 4. Announcements
CREATE TABLE IF NOT EXISTS announcements (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    title VARCHAR(255) NOT NULL,
    message TEXT NOT NULL,
    audience VARCHAR(50) DEFAULT 'all' NOT NULL,
    status VARCHAR(50) DEFAULT 'draft' NOT NULL,
    published_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ,
    created_by UUID REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_announcements_status_dates ON announcements(status, published_at, expires_at);

-- 5. Daily Aggregated Analytics
CREATE TABLE IF NOT EXISTS daily_analytics (
    date DATE PRIMARY KEY,
    daily_active_users INTEGER DEFAULT 0 NOT NULL,
    weekly_active_users INTEGER DEFAULT 0 NOT NULL,
    new_accounts INTEGER DEFAULT 0 NOT NULL,
    active_sessions INTEGER DEFAULT 0 NOT NULL,
    download_events BIGINT DEFAULT 0 NOT NULL,
    notes_played BIGINT DEFAULT 0 NOT NULL
);

-- 6. Administrative Audit Logs
CREATE TABLE IF NOT EXISTS admin_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    admin_user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    admin_email VARCHAR(255),
    action VARCHAR(100) NOT NULL,
    target_type VARCHAR(100),
    target_id VARCHAR(255),
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    metadata JSONB DEFAULT '{}'::jsonb NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_admin_audit_logs_created ON admin_audit_logs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_admin_audit_logs_action ON admin_audit_logs(action);

-- 7. Playground Visual Effects Events
CREATE TABLE IF NOT EXISTS playground_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    target_user_id UUID REFERENCES users(id) ON DELETE CASCADE,
    effect_type VARCHAR(50) NOT NULL,
    created_by UUID REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    dismissed_at TIMESTAMPTZ,
    metadata JSONB DEFAULT '{}'::jsonb
);

CREATE INDEX IF NOT EXISTS idx_playground_events_active ON playground_events(target_user_id, expires_at) WHERE dismissed_at IS NULL;

-- Schema version tracking
CREATE TABLE IF NOT EXISTS schema_migrations (
    version VARCHAR(50) PRIMARY KEY,
    applied_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL
);

INSERT INTO schema_migrations (version) VALUES ('001_admin_and_presence_schema')
ON CONFLICT (version) DO NOTHING;

COMMIT;
