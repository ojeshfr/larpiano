-- ==============================================================================
-- LARPIANO AUTH — MIGRATION 002 (UP)
-- Additive schema: auth sessions, email verification, password reset, auth audit
-- ==============================================================================

BEGIN;

-- 1. Add updated_at to users if it doesn't exist
ALTER TABLE users ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP;

-- 2. User authentication sessions (rotating refresh tokens)
--    Raw tokens are NEVER stored. Only SHA-256 hashes are persisted.
CREATE TABLE IF NOT EXISTS user_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    -- SHA-256 hash of the opaque refresh token (never the token itself)
    refresh_token_hash VARCHAR(64) NOT NULL UNIQUE,
    -- Access token jti for revocation tracking
    access_token_jti VARCHAR(64),
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    last_used_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    revoked_at TIMESTAMPTZ,
    -- Client metadata for session list display
    user_agent TEXT,
    ip_address VARCHAR(45)
);

CREATE INDEX IF NOT EXISTS idx_user_sessions_user_id ON user_sessions(user_id) WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_user_sessions_expires ON user_sessions(expires_at) WHERE revoked_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_user_sessions_hash ON user_sessions(refresh_token_hash);

-- 3. Email verification codes
--    Only the SHA-256 hash of the 6-digit code is stored.
--    Single-use, 10-minute expiry, attempt-limited.
CREATE TABLE IF NOT EXISTS email_verification_codes (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash VARCHAR(64) NOT NULL,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ,
    attempts INTEGER DEFAULT 0 NOT NULL,
    -- Last resend timestamp for cooldown enforcement
    last_resent_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_email_ver_user_id ON email_verification_codes(user_id) WHERE used_at IS NULL;

-- 4. Password reset tokens
--    Only the SHA-256 hash of the opaque token is stored.
--    Single-use, 15-minute expiry.
CREATE TABLE IF NOT EXISTS password_reset_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(64) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_pwd_reset_user_id ON password_reset_tokens(user_id) WHERE used_at IS NULL;
CREATE INDEX IF NOT EXISTS idx_pwd_reset_hash ON password_reset_tokens(token_hash);

-- 5. Auth audit log (immutable; never delete rows)
CREATE TABLE IF NOT EXISTS auth_audit_logs (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES users(id) ON DELETE SET NULL,
    email VARCHAR(255),
    action VARCHAR(100) NOT NULL,
    ip_address VARCHAR(45),
    user_agent TEXT,
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL,
    metadata JSONB DEFAULT '{}'::jsonb NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_auth_audit_user_id ON auth_audit_logs(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_auth_audit_created ON auth_audit_logs(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_auth_audit_action ON auth_audit_logs(action);

-- Record this migration
INSERT INTO schema_migrations (version) VALUES ('002_auth_schema')
ON CONFLICT (version) DO NOTHING;

COMMIT;
