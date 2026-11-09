-- ==============================================================================
-- LARPIANO CONTROL CENTER — MIGRATION 004 (UP)
-- Authoritative Releases Schema & Historical Seed
-- Decouples admin releases and client downloads from GitHub availability.
-- ==============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS app_releases (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tag_name VARCHAR(64) UNIQUE NOT NULL,
    version VARCHAR(64) NOT NULL,
    channel VARCHAR(32) NOT NULL DEFAULT 'Stable',
    title VARCHAR(255) NOT NULL,
    release_notes TEXT NOT NULL DEFAULT '',
    prerelease BOOLEAN NOT NULL DEFAULT FALSE,
    published_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    storage_key VARCHAR(255),
    file_name VARCHAR(255),
    file_size BIGINT,
    sha256 VARCHAR(64),
    download_url VARCHAR(500),
    github_url VARCHAR(500),
    github_download_url VARCHAR(500),
    is_current_latest BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_app_releases_published_at ON app_releases(published_at DESC);
CREATE INDEX IF NOT EXISTS idx_app_releases_tag_name ON app_releases(tag_name);
CREATE INDEX IF NOT EXISTS idx_app_releases_latest ON app_releases(is_current_latest);

-- Seed authoritative historical releases
INSERT INTO app_releases (
    tag_name, version, channel, title, release_notes, prerelease,
    published_at, storage_key, file_name, file_size, sha256,
    download_url, github_url, github_download_url, is_current_latest
) VALUES
(
    'v1.1.0',
    '1.1.0',
    'Stable',
    'Larpiano v1.1.0 — Official Production Release',
    '# Larpiano v1.1.0 — Official Production Release\n\n- Permanent Privacy & Telemetry platform defaults with non-interactive UI status rows\n- Shared Cloud MIDI Library with 218 curated arrangements and one-click play\n- Authoritative Portways deployment and Fleet Management integration',
    FALSE,
    '2026-11-09 10:14:45+00',
    'releases/v1.1.0/larpiano-windows-x64.zip',
    'larpiano-windows-x64.zip',
    60602878,
    '98c66d95af60e3b0a93ea82248bd82236232641ce7d404bb1c0767167a5f8ecd',
    '/api/releases/latest/download',
    'https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0',
    'https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0/larpiano-windows-x64.zip',
    TRUE
),
(
    'v1.1.0-beta.2',
    '1.1.0-beta.2',
    'Beta',
    'Larpiano v1.1.0-beta.2',
    'Beta candidate with initial cloud catalog integration and fleet telemetry tests.',
    TRUE,
    '2026-11-09 04:36:00+00',
    NULL,
    'Larpiano-v1.1.0-beta.2-win-x64.zip',
    60600564,
    'd94aa6be130a0bf05b6348ef533c39d73caeb31498ec5b8c9c615a6b0c268700',
    NULL,
    'https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.2',
    'https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0-beta.2/Larpiano-v1.1.0-beta.2-win-x64.zip',
    FALSE
),
(
    'v1.1.0-beta.1',
    '1.1.0-beta.1',
    'Beta',
    'Larpiano v1.1.0-beta.1',
    'Initial v1.1.0 beta testing opt-in telemetry and presence architecture.',
    TRUE,
    '2026-11-07 18:31:00+00',
    NULL,
    'larpiano-v1.1.0-beta.1-windows-x64.zip',
    NULL,
    NULL,
    NULL,
    'https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.1',
    'https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0-beta.1/larpiano-v1.1.0-beta.1-windows-x64.zip',
    FALSE
),
(
    'v1.0.35',
    '1.0.35',
    'Stable',
    'Larpiano v1.0.35',
    'Visual reconstruction of the desktop application and floating overlay menu polish.',
    FALSE,
    '2026-11-08 12:00:00+00',
    NULL,
    'larpiano-v1.0.35-windows-x64.zip',
    NULL,
    NULL,
    NULL,
    'https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.35',
    'https://github.com/ojeshfrrr/larpiano/releases/download/v1.0.35/larpiano-v1.0.35-windows-x64.zip',
    FALSE
),
(
    'v1.0.0',
    '1.0.0',
    'Stable',
    'Larpiano v1.0.0 — Initial Release',
    'Initial stable public release of Larpiano MIDI playback engine for Roblox.',
    FALSE,
    '2026-11-03 14:00:00+00',
    NULL,
    'larpiano-v1.0.0-windows-x64.zip',
    NULL,
    NULL,
    NULL,
    'https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.0',
    'https://github.com/ojeshfrrr/larpiano/releases/download/v1.0.0/larpiano-v1.0.0-windows-x64.zip',
    FALSE
)
ON CONFLICT (tag_name) DO NOTHING;

INSERT INTO schema_migrations (version) VALUES ('004_releases_schema')
ON CONFLICT (version) DO NOTHING;

COMMIT;
