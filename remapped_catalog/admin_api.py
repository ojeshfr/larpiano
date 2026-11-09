"""Admin Control Center and App Presence API Backend.

Provides authenticated administration endpoints, real-time presence heartbeats,
announcement broadcasting, telemetry aggregation, audit logging, and object storage mediation.
Supports both PostgreSQL (production) and SQLite (development/testing).
"""

from __future__ import annotations

import contextlib
import csv
import datetime
import hashlib
import hmac
import io
import json
import logging
import os
from pathlib import Path
import re
import secrets
import sqlite3
from typing import Any, Optional
import urllib.request
import uuid

import storage

log = logging.getLogger("admin_api")

ROOT = Path(__file__).resolve().parent
DEV_DB_PATH = ROOT / "larpiano_admin_dev.db"

# Admin Authentication Secret
ADMIN_TOKEN = os.environ.get("ADMIN_TOKEN", "")
ACTIVE_TOKENS: dict[str, dict] = {}

# GitHub releases cache
_RELEASES_CACHE: dict[str, Any] = {"timestamp": 0, "data": []}
RELEASES_TTL_SECONDS = 900  # 15 minutes


def _is_pg() -> bool:
    url = os.environ.get("DATABASE_URL", "")
    return url.startswith("postgres://") or url.startswith("postgresql://")


@contextlib.contextmanager
def get_db():
    """Get database connection (PostgreSQL in production, SQLite in dev/test)."""
    if _is_pg():
        import psycopg
        from psycopg.rows import dict_row
        conn = psycopg.connect(os.environ["DATABASE_URL"], connect_timeout=5, row_factory=dict_row)
        try:
            with conn:
                yield conn
        finally:
            conn.close()
    else:
        conn = sqlite3.connect(str(DEV_DB_PATH), timeout=20.0, isolation_level=None)
        conn.row_factory = sqlite3.Row
        try:
            conn.execute("PRAGMA journal_mode = WAL")
            conn.execute("PRAGMA busy_timeout = 20000")
        except Exception:
            pass
        try:
            yield conn
        finally:
            conn.close()


def _ensure_sqlite_schema(conn: sqlite3.Connection):
    """Ensure SQLite schema exists in local development mode."""
    cur = conn.cursor()
    try:
        cur.execute("PRAGMA journal_mode = WAL")
    except Exception:
        pass
    cur.executescript("""
        CREATE TABLE IF NOT EXISTS users (
            id TEXT PRIMARY KEY,
            email TEXT NOT NULL UNIQUE,
            display_name TEXT NOT NULL,
            password_hash TEXT,
            email_verified INTEGER NOT NULL DEFAULT 0,
            role TEXT NOT NULL DEFAULT 'user',
            account_status TEXT NOT NULL DEFAULT 'active',
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_login_at TEXT
        );

        CREATE TABLE IF NOT EXISTS app_sessions (
            id TEXT PRIMARY KEY,
            user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
            session_identifier TEXT NOT NULL UNIQUE,
            app_version TEXT NOT NULL,
            started_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_seen_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            ended_at TEXT,
            current_status TEXT NOT NULL DEFAULT 'online'
        );

        CREATE TABLE IF NOT EXISTS user_preferences (
            user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
            telemetry_consent INTEGER NOT NULL DEFAULT 1,
            presence_consent INTEGER NOT NULL DEFAULT 1,
            playground_effects_enabled INTEGER NOT NULL DEFAULT 1,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS announcements (
            id TEXT PRIMARY KEY,
            title TEXT NOT NULL,
            message TEXT NOT NULL,
            audience TEXT NOT NULL DEFAULT 'all',
            status TEXT NOT NULL DEFAULT 'draft',
            published_at TEXT,
            expires_at TEXT,
            created_by TEXT REFERENCES users(id) ON DELETE SET NULL,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
        );

        CREATE TABLE IF NOT EXISTS daily_analytics (
            date TEXT PRIMARY KEY,
            daily_active_users INTEGER NOT NULL DEFAULT 0,
            weekly_active_users INTEGER NOT NULL DEFAULT 0,
            new_accounts INTEGER NOT NULL DEFAULT 0,
            active_sessions INTEGER NOT NULL DEFAULT 0,
            download_events INTEGER NOT NULL DEFAULT 0,
            notes_played INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS admin_audit_logs (
            id TEXT PRIMARY KEY,
            admin_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
            action TEXT NOT NULL,
            target_type TEXT,
            target_id TEXT,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            metadata TEXT NOT NULL DEFAULT '{}'
        );

        CREATE TABLE IF NOT EXISTS playground_events (
            id TEXT PRIMARY KEY,
            target_user_id TEXT REFERENCES users(id) ON DELETE CASCADE,
            effect_type TEXT NOT NULL,
            created_by TEXT REFERENCES users(id) ON DELETE SET NULL,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at TEXT,
            dismissed_at TEXT
        );

        CREATE TABLE IF NOT EXISTS app_releases (
            id TEXT PRIMARY KEY,
            tag_name TEXT UNIQUE NOT NULL,
            version TEXT NOT NULL,
            channel TEXT NOT NULL DEFAULT 'Stable',
            title TEXT NOT NULL,
            release_notes TEXT NOT NULL DEFAULT '',
            prerelease INTEGER NOT NULL DEFAULT 0,
            published_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            storage_key TEXT,
            file_name TEXT,
            file_size INTEGER,
            sha256 TEXT,
            download_url TEXT,
            github_url TEXT,
            github_download_url TEXT,
            is_current_latest INTEGER NOT NULL DEFAULT 0
        );
    """)
    conn.commit()


def _ensure_pg_releases_schema(conn):
    """Ensure PostgreSQL app_releases table exists."""
    with conn.cursor() as cur:
        cur.execute("""
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
        """)
        conn.commit()


def _seed_releases_if_empty(conn):
    """Seed authoritative historical releases if app_releases table is empty."""
    try:
        cur = conn.cursor()
        cur.execute("SELECT COUNT(*) as count FROM app_releases")
        row = cur.fetchone()
        count = row["count"] if isinstance(row, dict) or isinstance(row, sqlite3.Row) else row[0]
        if count == 0:
            releases = [
                (
                    str(uuid.uuid4()),
                    "v1.1.0",
                    "1.1.0",
                    "Stable",
                    "Larpiano v1.1.0 — Official Production Release",
                    "# Larpiano v1.1.0 — Official Production Release\n\n- Permanent Privacy & Telemetry platform defaults with non-interactive UI status rows\n- Shared Cloud MIDI Library with 218 curated arrangements and one-click play\n- Authoritative Portways deployment and Fleet Management integration",
                    0 if not _is_pg() else False,
                    "2026-11-09 10:14:45+00",
                    "releases/v1.1.0/larpiano-windows-x64.zip",
                    "larpiano-windows-x64.zip",
                    60602878,
                    "98c66d95af60e3b0a93ea82248bd82236232641ce7d404bb1c0767167a5f8ecd",
                    "/api/releases/latest/download",
                    "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0",
                    "https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0/larpiano-windows-x64.zip",
                    1 if not _is_pg() else True
                ),
                (
                    str(uuid.uuid4()),
                    "v1.1.0-beta.2",
                    "1.1.0-beta.2",
                    "Beta",
                    "Larpiano v1.1.0-beta.2",
                    "Beta candidate with initial cloud catalog integration and fleet telemetry tests.",
                    1 if not _is_pg() else True,
                    "2026-11-09 04:36:00+00",
                    None,
                    "Larpiano-v1.1.0-beta.2-win-x64.zip",
                    60600564,
                    "d94aa6be130a0bf05b6348ef533c39d73caeb31498ec5b8c9c615a6b0c268700",
                    None,
                    "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.2",
                    "https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0-beta.2/Larpiano-v1.1.0-beta.2-win-x64.zip",
                    0 if not _is_pg() else False
                ),
                (
                    str(uuid.uuid4()),
                    "v1.1.0-beta.1",
                    "1.1.0-beta.1",
                    "Beta",
                    "Larpiano v1.1.0-beta.1",
                    "Initial v1.1.0 beta testing opt-in telemetry and presence architecture.",
                    1 if not _is_pg() else True,
                    "2026-11-07 18:31:00+00",
                    None,
                    "larpiano-v1.1.0-beta.1-windows-x64.zip",
                    None,
                    None,
                    None,
                    "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.1",
                    "https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0-beta.1/larpiano-v1.1.0-beta.1-windows-x64.zip",
                    0 if not _is_pg() else False
                ),
                (
                    str(uuid.uuid4()),
                    "v1.0.35",
                    "1.0.35",
                    "Stable",
                    "Larpiano v1.0.35",
                    "Visual reconstruction of the desktop application and floating overlay menu polish.",
                    0 if not _is_pg() else False,
                    "2026-11-08 12:00:00+00",
                    None,
                    "larpiano-v1.0.35-windows-x64.zip",
                    None,
                    None,
                    None,
                    "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.35",
                    "https://github.com/ojeshfrrr/larpiano/releases/download/v1.0.35/larpiano-v1.0.35-windows-x64.zip",
                    0 if not _is_pg() else False
                ),
                (
                    str(uuid.uuid4()),
                    "v1.0.0",
                    "1.0.0",
                    "Stable",
                    "Larpiano v1.0.0 — Initial Release",
                    "Initial stable public release of Larpiano MIDI playback engine for Roblox.",
                    0 if not _is_pg() else False,
                    "2026-11-03 14:00:00+00",
                    None,
                    "larpiano-v1.0.0-windows-x64.zip",
                    None,
                    None,
                    None,
                    "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.0",
                    "https://github.com/ojeshfrrr/larpiano/releases/download/v1.0.0/larpiano-v1.0.0-windows-x64.zip",
                    0 if not _is_pg() else False
                )
            ]
            ph = "(%s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s, %s)" if _is_pg() else "(?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)"
            sql = f"""INSERT INTO app_releases (
                id, tag_name, version, channel, title, release_notes, prerelease,
                published_at, storage_key, file_name, file_size, sha256,
                download_url, github_url, github_download_url, is_current_latest
            ) VALUES {ph}"""
            for r in releases:
                cur.execute(sql, r)
            conn.commit()
    except Exception as e:
        log.warning("Could not seed app_releases: %s", e)


def init_db_if_needed():
    """Initializes tables and seeds initial administrator and releases if empty."""
    try:
        with get_db() as conn:
            if not _is_pg():
                _ensure_sqlite_schema(conn)
                _seed_initial_data_if_empty(conn)
            else:
                _ensure_pg_releases_schema(conn)
            _seed_releases_if_empty(conn)
    except Exception as e:
        log.warning("Database initialization check: %s", e)


def _hash_password(password: str) -> str:
    from argon2 import PasswordHasher
    return PasswordHasher().hash(password)


def _verify_password(password: str, stored_hash: Optional[str]) -> bool:
    if stored_hash and stored_hash.startswith("$argon2"):
        from argon2 import PasswordHasher
        from argon2.exceptions import InvalidHashError, VerificationError
        try:
            return PasswordHasher().verify(stored_hash, password)
        except (InvalidHashError, VerificationError):
            return False
    if not stored_hash or ":" not in stored_hash:
        return False
    salt, hashed = stored_hash.split(":", 1)
    test_hash = hashlib.sha256((salt + password).encode("utf-8")).hexdigest()
    return hmac.compare_digest(hashed, test_hash)


def _seed_initial_data_if_empty(conn):
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) as count FROM users")
    row = cur.fetchone()
    count = row["count"] if isinstance(row, dict) or isinstance(row, sqlite3.Row) else row[0]
    initial_email = os.environ.get("INITIAL_ADMIN_EMAIL", "").strip()
    initial_password = os.environ.get("INITIAL_ADMIN_PASSWORD", "")
    if count == 0 and initial_email and len(initial_password) >= 16:
        admin_id = str(uuid.uuid4())
        cur.execute(
            """INSERT INTO users (id, email, display_name, password_hash, email_verified, role, account_status)
               VALUES (?, ?, ?, ?, 1, 'admin', 'active')""",
            (admin_id, initial_email, "Admin", _hash_password(initial_password))
        )
        cur.execute(
            """INSERT INTO user_preferences (user_id, telemetry_consent, presence_consent, playground_effects_enabled)
               VALUES (?, 1, 1, 0)""",
            (admin_id,)
        )
        # Seed initial announcement
        ann_id = str(uuid.uuid4())
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        cur.execute(
            """INSERT INTO announcements (id, title, message, audience, status, published_at, created_by)
               VALUES (?, ?, ?, 'all', 'published', ?, ?)""",
            (ann_id, "v1.1.0-beta.1 is now live", "Welcome to Larpiano Control Center. Real-time telemetry is hooked.", now, admin_id)
        )
        # Seed initial audit log
        cur.execute(
            """INSERT INTO admin_audit_logs (id, admin_user_id, action, target_type, target_id, metadata)
               VALUES (?, ?, 'SYSTEM_INIT', 'system', 'cluster', ?)""",
            (str(uuid.uuid4()), admin_id, json.dumps({"note": "Database initialized with admin seed"}))
        )
        conn.commit()


# Initialize database schema and seeds
init_db_if_needed()


# --- Authentication Middleware ---

def verify_admin_token(token: Optional[str]) -> Optional[dict]:
    """Verify administrator Bearer token or session token."""
    if not token:
        return None
    token = token.removeprefix("Bearer ").strip()
    
    # Direct master admin token match
    if ADMIN_TOKEN and hmac.compare_digest(token, ADMIN_TOKEN):
        return {
            "id": None,
            "email": "",
            "display_name": "Operator",
            "role": "admin"
        }
    
    # Check active session cache
    if token in ACTIVE_TOKENS:
        sess = ACTIVE_TOKENS[token]
        if sess["expires_at"] > datetime.datetime.now(datetime.timezone.utc):
            return sess["user"]
        else:
            del ACTIVE_TOKENS[token]
    
    return None


def audit_log(admin_id: Optional[str], action: str, target_type: str, target_id: str, metadata: dict = None):
    """Record an audit trail event."""
    metadata_json = json.dumps(metadata or {})
    try:
        with get_db() as conn:
            cur = conn.cursor()
            log_id = str(uuid.uuid4())
            if _is_pg():
                cur.execute(
                    """INSERT INTO admin_audit_logs (id, admin_user_id, action, target_type, target_id, metadata)
                       VALUES (%s, %s, %s, %s, %s, %s)""",
                    (log_id, admin_id, action, target_type, target_id, metadata_json)
                )
            else:
                cur.execute(
                    """INSERT INTO admin_audit_logs (id, admin_user_id, action, target_type, target_id, metadata)
                       VALUES (?, ?, ?, ?, ?, ?)""",
                    (log_id, admin_id, action, target_type, target_id, metadata_json)
                )
            conn.commit()
    except Exception as exc:
        log.error("Failed to write audit log: %s", exc)


# --- Public / App Presence Endpoints ---

def handle_heartbeat(data: dict) -> dict:
    """Heartbeat endpoint called by desktop app approximately every 45s.
    
    Upserts session record: updates last_seen_at and current_status without append spam.
    """
    session_id = data.get("session_identifier")
    if not session_id or len(session_id) > 128:
        raise ValueError("Invalid session_identifier")
    
    app_version = str(data.get("app_version", "1.1.0"))[:50]
    status = str(data.get("status", "online"))[:50]
    if status not in ("online", "idle", "playing", "offline"):
        status = "online"
    
    user_id = data.get("user_id")
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()

    with get_db() as conn:
        cur = conn.cursor()
        if user_id:
            marker = "%s" if _is_pg() else "?"
            cur.execute(f"SELECT user_id FROM app_sessions WHERE session_identifier = {marker}", (session_id,))
            existing = cur.fetchone()
            if existing and str(existing["user_id"]) != str(user_id):
                raise PermissionError("Session belongs to another user")
        if _is_pg():
            cur.execute(
                """INSERT INTO app_sessions (id, user_id, session_identifier, app_version, started_at, last_seen_at, current_status, ended_at)
                   VALUES (gen_random_uuid(), %s, %s, %s, NOW(), NOW(), %s, NULL)
                   ON CONFLICT (session_identifier) DO UPDATE
                   SET last_seen_at = NOW(),
                       current_status = EXCLUDED.current_status,
                       app_version = COALESCE(EXCLUDED.app_version, app_sessions.app_version),
                       ended_at = NULL
                   WHERE app_sessions.user_id IS NOT DISTINCT FROM EXCLUDED.user_id""",
                (user_id, session_id, app_version, status)
            )
            if cur.rowcount == 0:
                raise PermissionError("Session belongs to another user")
        else:
            cur.execute("SELECT id FROM app_sessions WHERE session_identifier = ?", (session_id,))
            row = cur.fetchone()
            if row:
                cur.execute(
                    """UPDATE app_sessions
                       SET last_seen_at = ?, current_status = ?, app_version = ?, ended_at = NULL
                       WHERE session_identifier = ?""",
                    (now_iso, status, app_version, session_id)
                )
            else:
                cur.execute(
                    """INSERT INTO app_sessions (id, user_id, session_identifier, app_version, started_at, last_seen_at, current_status)
                       VALUES (?, ?, ?, ?, ?, ?, ?)""",
                    (str(uuid.uuid4()), user_id, session_id, app_version, now_iso, now_iso, status)
                )
        conn.commit()

    return {
        "status": "ok",
        "heartbeat_interval_sec": 45,
        "server_time": now_iso
    }


def handle_session_end(data: dict) -> dict:
    """Clean session shutdown called by desktop app on exit or logout."""
    session_id = data.get("session_identifier")
    if not session_id:
        raise ValueError("Missing session_identifier")

    user_id = data.get("user_id")
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    with get_db() as conn:
        cur = conn.cursor()
        if user_id:
            marker = "%s" if _is_pg() else "?"
            cur.execute(f"SELECT user_id FROM app_sessions WHERE session_identifier = {marker}", (session_id,))
            existing = cur.fetchone()
            if existing and str(existing["user_id"]) != str(user_id):
                raise PermissionError("Session belongs to another user")
        if _is_pg():
            cur.execute(
                """UPDATE app_sessions SET ended_at = NOW(), current_status = 'offline'
                   WHERE session_identifier = %s""",
                (session_id,)
            )
        else:
            cur.execute(
                """UPDATE app_sessions SET ended_at = ?, current_status = 'offline'
                   WHERE session_identifier = ?""",
                (now_iso, session_id)
            )
        conn.commit()

    return {"status": "ok"}


def record_telemetry_event(data: dict, user_id: str) -> dict:
    """Record anonymous aggregate note statistics.
    
    Strict Privacy Guarantee:
    - Never accepts raw notes, song titles, filenames, user identifiers, or keystroke timings.
    - Only records aggregate counts: note_count, session_duration_seconds, app_version.
    """
    forbidden_keys = {"title", "song", "file", "filename", "notes", "midi", "raw", "keys", "username", "email", "keystrokes"}
    found_forbidden = [k for k in data.keys() if any(f in k.lower() for f in forbidden_keys)]
    if found_forbidden:
        raise ValueError(f"Forbidden sensitive telemetry fields detected: {found_forbidden}")

    try:
        note_count = max(0, int(data.get("note_count", 0)))
    except (TypeError, ValueError):
        note_count = 0

    today = datetime.datetime.now(datetime.timezone.utc).date()
    today_str = today.isoformat()

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """
                INSERT INTO daily_analytics (date, notes_played)
                VALUES (%s, %s)
                ON CONFLICT (date) DO UPDATE
                SET notes_played = daily_analytics.notes_played + EXCLUDED.notes_played
                """,
                (today, note_count)
            )
        else:
            cur.execute(
                """
                INSERT INTO daily_analytics (date, notes_played)
                VALUES (?, ?)
                ON CONFLICT (date) DO UPDATE
                SET notes_played = daily_analytics.notes_played + excluded.notes_played
                """,
                (today_str, note_count)
            )
        conn.commit()

    return {"status": "ok", "notes_recorded": note_count}


def get_app_announcements() -> list[dict]:
    """Return active published announcements for desktop clients."""
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """SELECT id, title, message, audience, published_at, expires_at
                   FROM announcements
                   WHERE status = 'published'
                     AND published_at <= NOW()
                     AND (expires_at IS NULL OR expires_at > NOW())
                   ORDER BY published_at DESC LIMIT 20"""
            )
        else:
            now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
            cur.execute(
                """SELECT id, title, message, audience, published_at, expires_at
                   FROM announcements
                   WHERE status = 'published'
                     AND published_at <= ?
                     AND (expires_at IS NULL OR expires_at > ?)
                   ORDER BY published_at DESC LIMIT 20""",
                (now_iso, now_iso)
            )
        rows = cur.fetchall()
        return [dict(r) for r in rows]


def get_app_playground_events(user_id: Optional[str] = None) -> list[dict]:
    """Return unexpired, undismissed playground effects for client."""
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """SELECT id, effect_type, created_at, expires_at
                   FROM playground_events
                   WHERE dismissed_at IS NULL
                     AND (expires_at IS NULL OR expires_at > NOW())
                     AND (target_user_id IS NULL OR target_user_id = %s)
                   ORDER BY created_at DESC LIMIT 10""",
                (user_id,)
            )
        else:
            now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
            cur.execute(
                """SELECT id, effect_type, created_at, expires_at
                   FROM playground_events
                   WHERE dismissed_at IS NULL
                     AND (expires_at IS NULL OR expires_at > ?)
                     AND (target_user_id IS NULL OR target_user_id = ?)
                   ORDER BY created_at DESC LIMIT 10""",
                (now_iso, user_id)
            )
        rows = cur.fetchall()
        return [dict(r) for r in rows]


def dismiss_playground_event(event_id: str) -> dict:
    """Dismiss a playground effect for client."""
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("UPDATE playground_events SET dismissed_at = NOW() WHERE id = %s", (event_id,))
        else:
            cur.execute("UPDATE playground_events SET dismissed_at = ? WHERE id = ?", (now_iso, event_id))
        conn.commit()
    return {"status": "ok"}


def update_user_preferences(user_id: str, data: dict) -> dict:
    """Update opt-in consent and preferences. Enforces mandatory platform defaults."""
    if data.get("telemetry_consent") is False or data.get("presence_consent") is False:
        raise ValueError("Presence and anonymous statistics are mandatory platform features and cannot be disabled.")
    telemetry = 1
    presence = 1
    playground = 1 if data.get("playground_effects_enabled", True) else 0
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """INSERT INTO user_preferences (user_id, telemetry_consent, presence_consent, playground_effects_enabled, updated_at)
                   VALUES (%s, %s, %s, %s, NOW())
                   ON CONFLICT (user_id) DO UPDATE
                   SET telemetry_consent = EXCLUDED.telemetry_consent,
                       presence_consent = EXCLUDED.presence_consent,
                       playground_effects_enabled = EXCLUDED.playground_effects_enabled,
                       updated_at = NOW()""",
                (user_id, bool(telemetry), bool(presence), bool(playground))
            )
        else:
            cur.execute(
                """INSERT INTO user_preferences (user_id, telemetry_consent, presence_consent, playground_effects_enabled, updated_at)
                   VALUES (?, ?, ?, ?, ?)
                   ON CONFLICT (user_id) DO UPDATE
                   SET telemetry_consent = excluded.telemetry_consent,
                       presence_consent = excluded.presence_consent,
                       playground_effects_enabled = excluded.playground_effects_enabled,
                       updated_at = excluded.updated_at""",
                (user_id, telemetry, presence, playground, now_iso)
            )
        if not presence:
            if _is_pg():
                cur.execute("UPDATE app_sessions SET ended_at = NOW(), current_status = 'offline' WHERE user_id = %s AND ended_at IS NULL", (user_id,))
            else:
                cur.execute("UPDATE app_sessions SET ended_at = ?, current_status = 'offline' WHERE user_id = ? AND ended_at IS NULL", (now_iso, user_id))
        conn.commit()

    return {"status": "ok"}


# --- Admin Endpoints ---

def admin_login(data: dict) -> dict:
    """Authenticate administrator and return bearer token."""
    username_or_email = data.get("username") or data.get("email") or ""
    password = data.get("password", "")
    token_input = data.get("token", "")

    # Operator token login
    if ADMIN_TOKEN and token_input and hmac.compare_digest(token_input, ADMIN_TOKEN):
        user_info = {"id": None, "email": "", "display_name": "Operator", "role": "admin"}
        auth_token = secrets.token_hex(24)
        ACTIVE_TOKENS[auth_token] = {
            "user": user_info,
            "expires_at": datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=7)
        }
        audit_log(None, "ADMIN_LOGIN_TOKEN", "operator", "master-token")
        return {"token": auth_token, "user": user_info}

    # Username / Password login
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                "SELECT id, email, display_name, password_hash, role, account_status FROM users WHERE email = %s OR display_name = %s",
                (username_or_email, username_or_email)
            )
        else:
            cur.execute(
                "SELECT id, email, display_name, password_hash, role, account_status FROM users WHERE email = ? OR display_name = ?",
                (username_or_email, username_or_email)
            )
        row = cur.fetchone()
        if not row:
            raise PermissionError("Invalid username or password")

        user = dict(row)
        if user["account_status"] != "active":
            raise PermissionError("Account is inactive")
        if user["role"] != "admin":
            raise PermissionError("Access denied: Administrator privileges required")
        if not _verify_password(password, user.get("password_hash")):
            raise PermissionError("Invalid username or password")

        # Create session token
        auth_token = secrets.token_hex(24)
        user_info = {
            "id": user["id"],
            "email": user["email"],
            "display_name": user["display_name"],
            "role": user["role"]
        }
        ACTIVE_TOKENS[auth_token] = {
            "user": user_info,
            "expires_at": datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=7)
        }
        audit_log(user["id"], "ADMIN_LOGIN_PASSWORD", "user", user["id"])
        return {"token": auth_token, "user": user_info}


def get_admin_overview() -> dict:
    """Return aggregated metrics, version distribution, and live player activity."""
    with get_db() as conn:
        cur = conn.cursor()
        
        # 1. Online users count (heartbeat within 120s, current_status != 'offline', ended_at IS NULL)
        if _is_pg():
            cur.execute(
                """SELECT COUNT(*) as count FROM app_sessions
                   WHERE ended_at IS NULL
                     AND current_status != 'offline'
                     AND last_seen_at >= NOW() - INTERVAL '120 seconds'"""
            )
            online_now = cur.fetchone()["count"]

            cur.execute("SELECT COUNT(*) as count FROM users")
            total_accounts = cur.fetchone()["count"]

            cur.execute("SELECT COUNT(*) as count FROM app_sessions WHERE ended_at IS NULL AND current_status != 'offline' AND last_seen_at >= NOW() - INTERVAL '120 seconds'")
            active_sessions = cur.fetchone()["count"]

            # App version distribution
            cur.execute(
                """SELECT app_version, COUNT(*) as count
                   FROM app_sessions
                   WHERE ended_at IS NULL AND last_seen_at >= NOW() - INTERVAL '120 seconds'
                   GROUP BY app_version ORDER BY count DESC"""
            )
            version_rows = cur.fetchall()

            # Recent events from audit log
            cur.execute(
                """SELECT l.id, l.action, l.target_type, l.target_id, l.created_at, l.metadata,
                          u.display_name as admin_name
                   FROM admin_audit_logs l
                   LEFT JOIN users u ON l.admin_user_id = u.id
                   ORDER BY l.created_at DESC LIMIT 10"""
            )
            event_rows = cur.fetchall()

            # Active connected players list
            cur.execute(
                """SELECT s.id, s.session_identifier, s.app_version, s.current_status, s.last_seen_at,
                          COALESCE(u.display_name, 'Guest-' || SUBSTRING(s.session_identifier FROM 1 FOR 6)) as display_name
                   FROM app_sessions s
                   LEFT JOIN users u ON s.user_id = u.id
                   WHERE s.ended_at IS NULL
                     AND s.current_status != 'offline'
                     AND s.last_seen_at >= NOW() - INTERVAL '120 seconds'
                   ORDER BY s.last_seen_at DESC LIMIT 15"""
            )
            player_rows = cur.fetchall()

        else:
            # SQLite query logic
            cutoff = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(seconds=120)).isoformat()
            
            cur.execute(
                "SELECT COUNT(*) as count FROM app_sessions WHERE ended_at IS NULL AND current_status != 'offline' AND last_seen_at >= ?",
                (cutoff,)
            )
            online_now = cur.fetchone()["count"]

            cur.execute("SELECT COUNT(*) as count FROM users")
            total_accounts = cur.fetchone()["count"]

            cur.execute("SELECT COUNT(*) as count FROM app_sessions WHERE ended_at IS NULL AND current_status != 'offline' AND last_seen_at >= ?", (cutoff,))
            active_sessions = cur.fetchone()["count"]

            cur.execute(
                "SELECT app_version, COUNT(*) as count FROM app_sessions WHERE ended_at IS NULL AND last_seen_at >= ? GROUP BY app_version ORDER BY count DESC",
                (cutoff,)
            )
            version_rows = cur.fetchall()

            cur.execute(
                """SELECT l.id, l.action, l.target_type, l.target_id, l.created_at, l.metadata,
                          u.display_name as admin_name
                   FROM admin_audit_logs l
                   LEFT JOIN users u ON l.admin_user_id = u.id
                   ORDER BY l.created_at DESC LIMIT 10"""
            )
            event_rows = cur.fetchall()

            cur.execute(
                """SELECT s.id, s.session_identifier, s.app_version, s.current_status, s.last_seen_at,
                          COALESCE(u.display_name, 'Guest-' || SUBSTR(s.session_identifier, 1, 6)) as display_name
                   FROM app_sessions s
                   LEFT JOIN users u ON s.user_id = u.id
                   WHERE s.ended_at IS NULL
                     AND s.current_status != 'offline'
                     AND s.last_seen_at >= ?
                   ORDER BY s.last_seen_at DESC LIMIT 15""",
                (cutoff,)
            )
            player_rows = cur.fetchall()

    # Calculate version stats
    total_versions = sum(r["count"] for r in version_rows) if version_rows else 1
    dominant_version = version_rows[0]["app_version"] if version_rows else None
    dominant_share = f"{(version_rows[0]['count'] / total_versions * 100):.1f}% of active sessions" if version_rows else None
    donut_pct = round(version_rows[0]["count"] / total_versions * 100) if version_rows else None

    if not dominant_version:
        # Fallback to latest published release from app_releases to ensure consistency with Releases view
        try:
            with get_db() as conn:
                cur = conn.cursor()
                if _is_pg():
                    cur.execute("SELECT tag_name, version FROM app_releases WHERE is_current_latest = TRUE LIMIT 1")
                else:
                    cur.execute("SELECT tag_name, version FROM app_releases WHERE is_current_latest = 1 LIMIT 1")
                row = cur.fetchone()
                if row:
                    row_dict = dict(row)
                    dominant_version = row_dict.get("tag_name") or row_dict.get("version")
        except Exception:
            pass
        if not dominant_version:
            dominant_version = "v1.1.0"
        dominant_share = "100.0% (Production baseline, no active sessions)"
        donut_pct = 100

    # Calculate real 24-hour hourly activity curve from database
    activity_curve = []
    now_utc = datetime.datetime.now(datetime.timezone.utc)
    with get_db() as conn:
        c_curve = conn.cursor()
        for h in range(23, -1, -1):
            w_start = now_utc - datetime.timedelta(hours=h+1)
            w_end = now_utc - datetime.timedelta(hours=h)
            if _is_pg():
                c_curve.execute(
                    """SELECT COUNT(DISTINCT session_identifier) as count
                       FROM app_sessions
                       WHERE started_at < %s AND last_seen_at >= %s""",
                    (w_end, w_start)
                )
            else:
                c_curve.execute(
                    "SELECT COUNT(DISTINCT session_identifier) as count FROM app_sessions WHERE started_at < ? AND last_seen_at >= ?",
                    (w_end.isoformat(), w_start.isoformat())
                )
            row_cnt = c_curve.fetchone()
            activity_curve.append(int(row_cnt["count"]) if row_cnt else 0)

    recent_events = []
    for r in event_rows:
        row_dict = dict(r)
        meta = json.loads(row_dict.get("metadata") or "{}") if isinstance(row_dict.get("metadata"), str) else row_dict.get("metadata", {})
        recent_events.append({
            "id": row_dict["id"],
            "action": row_dict["action"],
            "target": f"{row_dict.get('target_type') or ''}: {row_dict.get('target_id') or ''}".strip(": "),
            "created_at": str(row_dict["created_at"]),
            "admin": row_dict.get("admin_name") or "System",
            "metadata": meta,
        })

    active_players = []
    for p in player_rows:
        p_dict = dict(p)
        active_players.append({
            "id": p_dict["id"],
            "session_identifier": p_dict["session_identifier"],
            "display_name": p_dict["display_name"],
            "app_version": p_dict["app_version"],
            "status": p_dict["current_status"],
            "last_seen_at": str(p_dict["last_seen_at"]),
        })

    return {
        "metrics": {
            "onlineNow": online_now,
            "onlineDelta": None,
            "totalAccounts": total_accounts,
            "accountsDelta": None,
            "activeSessions": active_sessions,
            "sessionsDelta": f"{active_sessions} total registered sessions",
            "appVersion": dominant_version,
            "versionShare": dominant_share,
            "versionDonutPercentage": donut_pct,
        },
        "live_activity_24h": activity_curve,
        "recent_events": recent_events,
        "active_players": active_players,
    }


def get_admin_analytics(range_str: str = "7d") -> dict:
    """Compute real historical analytics, daily active users, and version distribution."""
    num_days = 30 if range_str == "30d" else (90 if range_str == "90d" else 7)
    now = datetime.datetime.now(datetime.timezone.utc)
    one_day_ago = now - datetime.timedelta(hours=24)
    two_days_ago = now - datetime.timedelta(hours=48)
    seven_days_ago = now - datetime.timedelta(days=7)
    fourteen_days_ago = now - datetime.timedelta(days=14)

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            # DAU (past 24h)
            cur.execute(
                """SELECT COUNT(DISTINCT COALESCE(user_id::text, session_identifier)) as count
                   FROM app_sessions
                   WHERE last_seen_at >= NOW() - INTERVAL '24 hours'"""
            )
            dau = cur.fetchone()["count"]

            # Prior DAU (24h to 48h ago)
            cur.execute(
                """SELECT COUNT(DISTINCT COALESCE(user_id::text, session_identifier)) as count
                   FROM app_sessions
                   WHERE last_seen_at >= NOW() - INTERVAL '48 hours'
                     AND last_seen_at < NOW() - INTERVAL '24 hours'"""
            )
            prior_dau = cur.fetchone()["count"]

            # WAU (past 7d)
            cur.execute(
                """SELECT COUNT(DISTINCT COALESCE(user_id::text, session_identifier)) as count
                   FROM app_sessions
                   WHERE last_seen_at >= NOW() - INTERVAL '7 days'"""
            )
            wau = cur.fetchone()["count"]

            # Prior WAU (7d to 14d ago)
            cur.execute(
                """SELECT COUNT(DISTINCT COALESCE(user_id::text, session_identifier)) as count
                   FROM app_sessions
                   WHERE last_seen_at >= NOW() - INTERVAL '14 days'
                     AND last_seen_at < NOW() - INTERVAL '7 days'"""
            )
            prior_wau = cur.fetchone()["count"]

            # Average session duration (minutes)
            cur.execute(
                """SELECT COALESCE(AVG(EXTRACT(EPOCH FROM (COALESCE(ended_at, last_seen_at) - started_at)) / 60), 0) as avg_mins
                   FROM app_sessions
                   WHERE started_at >= NOW() - INTERVAL '30 days'"""
            )
            avg_session_mins = round(float(cur.fetchone()["avg_mins"] or 0), 1)

            # Daily users trend
            daily_users = []
            for i in range(num_days - 1, -1, -1):
                day_start = (now - datetime.timedelta(days=i)).replace(hour=0, minute=0, second=0, microsecond=0)
                day_end = day_start + datetime.timedelta(days=1)
                cur.execute(
                    """SELECT COUNT(DISTINCT COALESCE(user_id::text, session_identifier)) as count
                       FROM app_sessions
                       WHERE (started_at < %s AND last_seen_at >= %s)""",
                    (day_end, day_start)
                )
                cnt = cur.fetchone()["count"]
                daily_users.append({
                    "label": day_start.strftime("%b %d"),
                    "count": cnt
                })

            # Version distribution
            cur.execute(
                """SELECT app_version, COUNT(*) as count
                   FROM app_sessions
                   GROUP BY app_version ORDER BY count DESC"""
            )
            ver_rows = cur.fetchall()

        else:
            # SQLite query logic
            c24 = one_day_ago.isoformat()
            c48 = two_days_ago.isoformat()
            c7d = seven_days_ago.isoformat()
            c14d = fourteen_days_ago.isoformat()

            cur.execute(
                "SELECT COUNT(DISTINCT COALESCE(user_id, session_identifier)) as count FROM app_sessions WHERE last_seen_at >= ?",
                (c24,)
            )
            dau = cur.fetchone()["count"]

            cur.execute(
                "SELECT COUNT(DISTINCT COALESCE(user_id, session_identifier)) as count FROM app_sessions WHERE last_seen_at >= ? AND last_seen_at < ?",
                (c48, c24)
            )
            prior_dau = cur.fetchone()["count"]

            cur.execute(
                "SELECT COUNT(DISTINCT COALESCE(user_id, session_identifier)) as count FROM app_sessions WHERE last_seen_at >= ?",
                (c7d,)
            )
            wau = cur.fetchone()["count"]

            cur.execute(
                "SELECT COUNT(DISTINCT COALESCE(user_id, session_identifier)) as count FROM app_sessions WHERE last_seen_at >= ? AND last_seen_at < ?",
                (c14d, c7d)
            )
            prior_wau = cur.fetchone()["count"]

            # Average session duration
            cur.execute(
                "SELECT AVG((julianday(COALESCE(ended_at, last_seen_at)) - julianday(started_at)) * 1440) as avg_mins FROM app_sessions"
            )
            row = cur.fetchone()
            avg_session_mins = round(float(row["avg_mins"] or 0), 1) if row and row["avg_mins"] else 0

            daily_users = []
            for i in range(num_days - 1, -1, -1):
                day_start = (now - datetime.timedelta(days=i)).replace(hour=0, minute=0, second=0, microsecond=0)
                day_end = day_start + datetime.timedelta(days=1)
                cur.execute(
                    "SELECT COUNT(DISTINCT COALESCE(user_id, session_identifier)) as count FROM app_sessions WHERE started_at < ? AND last_seen_at >= ?",
                    (day_end.isoformat(), day_start.isoformat())
                )
                cnt = cur.fetchone()["count"]
                daily_users.append({
                    "label": day_start.strftime("%b %d"),
                    "count": cnt
                })

            cur.execute("SELECT app_version, COUNT(*) as count FROM app_sessions GROUP BY app_version ORDER BY count DESC")
            ver_rows = cur.fetchall()

    # Delta formatting
    if prior_dau == 0:
        dau_delta = f"+{dau} today" if dau > 0 else "0% vs prior day"
    else:
        pct = round(((dau - prior_dau) / prior_dau) * 100)
        dau_delta = f"{'+' if pct >= 0 else ''}{pct}% vs prior day"

    if prior_wau == 0:
        wau_delta = f"+{wau} this week" if wau > 0 else "0% vs prior week"
    else:
        pct = round(((wau - prior_wau) / prior_wau) * 100)
        wau_delta = f"{'+' if pct >= 0 else ''}{pct}% vs prior week"

    total_ver = sum(r["count"] for r in ver_rows) if ver_rows else 0
    version_dist = []
    if ver_rows and total_ver > 0:
        for r in ver_rows:
            pct = round((r["count"] / total_ver) * 100)
            version_dist.append({"label": r["app_version"] or "Unknown", "count": r["count"], "percentage": pct})
    else:
        version_dist = [{"label": "v1.1.0", "count": 0, "percentage": 100}]

    return {
        "dau": dau,
        "wau": wau,
        "avgSession": f"{avg_session_mins:.0f}m" if avg_session_mins > 0 else "0m",
        "dauDelta": dau_delta,
        "wauDelta": wau_delta,
        "avgSessionDelta": "Real-time average",
        "dailyUsers": daily_users,
        "versionDistribution": version_dist,
        "period": range_str,
    }


def get_admin_users(search: str = "", status: str = "", role: str = "", limit: int = 50, offset: int = 0) -> dict:
    """Retrieve users list with filtering and search."""
    limit = min(max(1, limit), 100)
    offset = max(0, offset)

    conditions = []
    params = []

    if search:
        s_pat = f"%{search}%"
        conditions.append("(u.display_name LIKE ? OR u.email LIKE ?)")
        params.extend([s_pat, s_pat])
    if status:
        conditions.append("u.account_status = ?")
        params.append(status)
    if role:
        conditions.append("u.role = ?")
        params.append(role)

    where_clause = ("WHERE " + " AND ".join(conditions)) if conditions else ""

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            pg_where = where_clause.replace("?", "%s")
            cur.execute(f"SELECT COUNT(*) as count FROM users u {pg_where}", tuple(params))
            total = cur.fetchone()["count"]

            cur.execute(
                f"""SELECT u.id, u.email, u.display_name, u.role, u.account_status, u.created_at, u.last_login_at,
                           p.telemetry_consent, p.presence_consent
                    FROM users u
                    LEFT JOIN user_preferences p ON u.id = p.user_id
                    {pg_where}
                    ORDER BY u.created_at DESC
                    LIMIT %s OFFSET %s""",
                tuple(params + [limit, offset])
            )
            users = [dict(r) for r in cur.fetchall()]
        else:
            cur.execute(f"SELECT COUNT(*) as count FROM users u {where_clause}", tuple(params))
            total = cur.fetchone()["count"]

            cur.execute(
                f"""SELECT u.id, u.email, u.display_name, u.role, u.account_status, u.created_at, u.last_login_at,
                           p.telemetry_consent, p.presence_consent
                    FROM users u
                    LEFT JOIN user_preferences p ON u.id = p.user_id
                    {where_clause}
                    ORDER BY u.created_at DESC
                    LIMIT ? OFFSET ?""",
                tuple(params + [limit, offset])
            )
            users = [dict(r) for r in cur.fetchall()]

    return {"total": total, "users": users, "limit": limit, "offset": offset}


def create_admin_user(data: dict, admin_id: str) -> dict:
    """Create a new user account with role and preferences."""
    email = data.get("email", "").strip().lower()
    display_name = data.get("display_name", "").strip()
    role = data.get("role", "user")
    password = data.get("password", "")

    if not email or "@" not in email:
        raise ValueError("Valid email is required")
    if not display_name:
        raise ValueError("Display name is required")
    if role not in ("user", "admin"):
        role = "user"

    pw_hash = _hash_password(password) if password else None
    user_id = str(uuid.uuid4())
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """INSERT INTO users (id, email, display_name, password_hash, role, account_status, email_verified, created_at)
                   VALUES (%s, %s, %s, %s, %s, 'active', true, NOW())""",
                (user_id, email, display_name, pw_hash, role)
            )
            cur.execute(
                """INSERT INTO user_preferences (user_id, telemetry_consent, presence_consent, playground_effects_enabled)
                   VALUES (%s, true, true, false)""",
                (user_id,)
            )
        else:
            cur.execute(
                """INSERT INTO users (id, email, display_name, password_hash, role, account_status, email_verified, created_at)
                   VALUES (?, ?, ?, ?, ?, 'active', 1, ?)""",
                (user_id, email, display_name, pw_hash, role, now_iso)
            )
            cur.execute(
                """INSERT INTO user_preferences (user_id, telemetry_consent, presence_consent, playground_effects_enabled)
                   VALUES (?, 1, 1, 0)""",
                (user_id,)
            )
        conn.commit()

    audit_log(admin_id, "USER_CREATED", "user", user_id, {"email": email, "role": role})
    return {"id": user_id, "email": email, "display_name": display_name, "role": role}


def update_admin_user(user_id: str, data: dict, admin_id: str) -> dict:
    """Update user role, status, or display name."""
    fields = []
    params = []
    
    if "display_name" in data:
        fields.append("display_name = ?")
        params.append(str(data["display_name"]).strip())
    if "role" in data and data["role"] in ("user", "admin"):
        fields.append("role = ?")
        params.append(data["role"])
    if "account_status" in data and data["account_status"] in ("active", "suspended", "flagged"):
        fields.append("account_status = ?")
        params.append(data["account_status"])

    if not fields:
        return {"status": "no_change"}

    set_clause = ", ".join(fields)
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            pg_set = set_clause.replace("?", "%s")
            cur.execute(f"UPDATE users SET {pg_set} WHERE id = %s", tuple(params + [user_id]))
        else:
            cur.execute(f"UPDATE users SET {set_clause} WHERE id = ?", tuple(params + [user_id]))
        conn.commit()

    audit_log(admin_id, "USER_UPDATED", "user", user_id, data)
    return {"status": "ok", "user_id": user_id}


def get_admin_sessions(status_filter: str = "all", limit: int = 50, offset: int = 0) -> dict:
    """List sessions with computed online state."""
    limit = min(max(1, limit), 100)
    offset = max(0, offset)

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """SELECT s.id, s.session_identifier, s.app_version, s.started_at, s.last_seen_at,
                          s.ended_at, s.current_status,
                          COALESCE(u.display_name, 'Guest-' || SUBSTRING(s.session_identifier FROM 1 FOR 6)) as display_name,
                          u.email,
                          (s.ended_at IS NULL AND s.last_seen_at >= NOW() - INTERVAL '120 seconds') as is_online
                   FROM app_sessions s
                   LEFT JOIN users u ON s.user_id = u.id
                   ORDER BY s.last_seen_at DESC
                   LIMIT %s OFFSET %s""",
                (limit, offset)
            )
            sessions = [dict(r) for r in cur.fetchall()]
            cur.execute("SELECT COUNT(*) as count FROM app_sessions")
            total = cur.fetchone()["count"]
        else:
            cutoff = (datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(seconds=120)).isoformat()
            cur.execute(
                """SELECT s.id, s.session_identifier, s.app_version, s.started_at, s.last_seen_at,
                          s.ended_at, s.current_status,
                          COALESCE(u.display_name, 'Guest-' || SUBSTR(s.session_identifier, 1, 6)) as display_name,
                          u.email,
                          CASE WHEN s.ended_at IS NULL AND s.last_seen_at >= ? THEN 1 ELSE 0 END as is_online
                   FROM app_sessions s
                   LEFT JOIN users u ON s.user_id = u.id
                   ORDER BY s.last_seen_at DESC
                   LIMIT ? OFFSET ?""",
                (cutoff, limit, offset)
            )
            sessions = [dict(r) for r in cur.fetchall()]
            cur.execute("SELECT COUNT(*) as count FROM app_sessions")
            total = cur.fetchone()["count"]

    return {"total": total, "sessions": sessions, "limit": limit, "offset": offset}


def terminate_session(session_id: str, admin_id: str) -> dict:
    """Terminate an active session."""
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                "UPDATE app_sessions SET ended_at = NOW(), current_status = 'terminated' WHERE id = %s OR session_identifier = %s",
                (session_id, session_id)
            )
        else:
            cur.execute(
                "UPDATE app_sessions SET ended_at = ?, current_status = 'terminated' WHERE id = ? OR session_identifier = ?",
                (now_iso, session_id, session_id)
            )
        conn.commit()

    audit_log(admin_id, "SESSION_TERMINATED", "session", session_id)
    return {"status": "ok", "terminated_session": session_id}


def get_admin_announcements() -> list[dict]:
    """Retrieve all announcements including drafts and scheduled."""
    with get_db() as conn:
        cur = conn.cursor()
        cur.execute(
            """SELECT a.id, a.title, a.message, a.audience, a.status, a.published_at, a.expires_at, a.created_at,
                      u.display_name as author
               FROM announcements a
               LEFT JOIN users u ON a.created_by = u.id
               ORDER BY a.created_at DESC"""
        )
        return [dict(r) for r in cur.fetchall()]


def create_announcement(data: dict, admin_id: str) -> dict:
    """Create a new broadcast announcement."""
    title = data.get("title", "").strip()
    message = data.get("message", "").strip()
    audience = data.get("audience", "all")
    status = data.get("status", "published")
    expires_at = data.get("expires_at")

    if not title or not message:
        raise ValueError("Title and message are required")

    ann_id = str(uuid.uuid4())
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    published_at = now_iso if status == "published" else None

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """INSERT INTO announcements (id, title, message, audience, status, published_at, expires_at, created_by, created_at)
                   VALUES (%s, %s, %s, %s, %s, %s, %s, %s, NOW())""",
                (ann_id, title, message, audience, status, published_at, expires_at, admin_id)
            )
        else:
            cur.execute(
                """INSERT INTO announcements (id, title, message, audience, status, published_at, expires_at, created_by, created_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (ann_id, title, message, audience, status, published_at, expires_at, admin_id, now_iso)
            )
        conn.commit()

    audit_log(admin_id, "ANNOUNCEMENT_CREATED", "announcement", ann_id, {"title": title, "status": status})
    return {"id": ann_id, "title": title, "status": status}


def update_announcement(ann_id: str, data: dict, admin_id: str) -> dict:
    """Update, publish, or retract an announcement."""
    fields = []
    params = []

    if "title" in data:
        fields.append("title = ?")
        params.append(data["title"])
    if "message" in data:
        fields.append("message = ?")
        params.append(data["message"])
    if "status" in data:
        fields.append("status = ?")
        params.append(data["status"])
        if data["status"] == "published":
            now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
            fields.append("published_at = ?")
            params.append(now_iso)

    if not fields:
        return {"status": "no_change"}

    set_clause = ", ".join(fields)
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            pg_set = set_clause.replace("?", "%s")
            cur.execute(f"UPDATE announcements SET {pg_set} WHERE id = %s", tuple(params + [ann_id]))
        else:
            cur.execute(f"UPDATE announcements SET {set_clause} WHERE id = ?", tuple(params + [ann_id]))
        conn.commit()

    audit_log(admin_id, "ANNOUNCEMENT_UPDATED", "announcement", ann_id, data)
    return {"status": "ok", "announcement_id": ann_id}


def delete_announcement(ann_id: str, admin_id: str) -> dict:
    """Delete an announcement."""
    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("DELETE FROM announcements WHERE id = %s", (ann_id,))
        else:
            cur.execute("DELETE FROM announcements WHERE id = ?", (ann_id,))
        conn.commit()

    audit_log(admin_id, "ANNOUNCEMENT_DELETED", "announcement", ann_id)
    return {"status": "ok", "deleted_id": ann_id}


def _check_github_status() -> tuple[bool, str]:
    """Non-blocking probe of external GitHub Releases API.
    Explicitly handles 403, 404, 429, 500, network errors, and timeouts without throwing."""
    url = "https://api.github.com/repos/ojeshfrrr/larpiano/releases"
    req = urllib.request.Request(
        url,
        headers={"User-Agent": "Larpiano-Admin-Server/1.0", "Accept": "application/vnd.github.v3+json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=2.0) as resp:
            if resp.status == 200:
                return True, "available"
            return False, f"HTTP {resp.status}"
    except urllib.error.HTTPError as exc:
        if exc.code == 403:
            return False, "unavailable (HTTP 403 Forbidden - account suspended)"
        elif exc.code == 404:
            return False, "unavailable (HTTP 404 Not Found)"
        elif exc.code == 429:
            return False, "unavailable (HTTP 429 Rate Limit Exceeded)"
        elif exc.code >= 500:
            return False, f"unavailable (HTTP {exc.code} Server Error)"
        return False, f"unavailable (HTTP {exc.code})"
    except urllib.error.URLError as exc:
        return False, f"unavailable (Network unreachable: {exc.reason})"
    except TimeoutError:
        return False, "unavailable (Connection timed out after 2s)"
    except Exception as exc:
        return False, f"unavailable ({str(exc)})"


def _get_static_release_definitions(github_available: bool, github_status: str) -> list[dict]:
    """Authoritative release records fallback if database read fails."""
    return [
        {
            "id": "rel-v1.1.0",
            "tag_name": "v1.1.0",
            "version": "v1.1.0",
            "name": "Larpiano v1.1.0 — Official Production Release",
            "channel": "Stable",
            "platform": "Windows x64 Portable",
            "date": "2026-11-09",
            "published_at": "2026-11-09T10:14:45Z",
            "created_at": "2026-11-09T10:14:45Z",
            "notes": "# Larpiano v1.1.0 — Official Production Release\n\n- Permanent Privacy & Telemetry platform defaults with non-interactive UI status rows\n- Shared Cloud MIDI Library with 218 curated arrangements and one-click play\n- Authoritative Portways deployment and Fleet Management integration",
            "body": "# Larpiano v1.1.0 — Official Production Release\n\n- Permanent Privacy & Telemetry platform defaults with non-interactive UI status rows\n- Shared Cloud MIDI Library with 218 curated arrangements and one-click play\n- Authoritative Portways deployment and Fleet Management integration",
            "prerelease": False,
            "githubUrl": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0",
            "html_url": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0",
            "download_url": "/api/releases/latest/download",
            "storage_key": "releases/v1.1.0/larpiano-windows-x64.zip",
            "file_name": "larpiano-windows-x64.zip",
            "file_size": 60602878,
            "sha256": "98c66d95af60e3b0a93ea82248bd82236232641ce7d404bb1c0767167a5f8ecd",
            "is_current_latest": True,
            "release_metadata_available": True,
            "external_download_host_available": github_available,
            "external_download_host_status": github_status,
            "local_storage_available": True,
            "assets": [
                {
                    "name": "larpiano-windows-x64.zip",
                    "size": 60602878,
                    "sha256": "98c66d95af60e3b0a93ea82248bd82236232641ce7d404bb1c0767167a5f8ecd",
                    "download_url": "/api/releases/latest/download",
                    "storage_available": True,
                    "external_download_url": "https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0/larpiano-windows-x64.zip" if github_available else None,
                    "external_host_available": github_available,
                    "external_host_status": github_status,
                }
            ],
        },
        {
            "id": "rel-v1.1.0-beta.2",
            "tag_name": "v1.1.0-beta.2",
            "version": "v1.1.0-beta.2",
            "name": "Larpiano v1.1.0-beta.2",
            "channel": "Beta",
            "platform": "Windows x64 Portable",
            "date": "2026-11-09",
            "published_at": "2026-11-09T04:36:00Z",
            "created_at": "2026-11-09T04:36:00Z",
            "notes": "Beta candidate with initial cloud catalog integration and fleet telemetry tests.",
            "body": "Beta candidate with initial cloud catalog integration and fleet telemetry tests.",
            "prerelease": True,
            "githubUrl": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.2",
            "html_url": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.2",
            "download_url": None,
            "storage_key": None,
            "file_name": "Larpiano-v1.1.0-beta.2-win-x64.zip",
            "file_size": 60600564,
            "sha256": "d94aa6be130a0bf05b6348ef533c39d73caeb31498ec5b8c9c615a6b0c268700",
            "is_current_latest": False,
            "release_metadata_available": True,
            "external_download_host_available": github_available,
            "external_download_host_status": github_status,
            "local_storage_available": False,
            "assets": [
                {
                    "name": "Larpiano-v1.1.0-beta.2-win-x64.zip",
                    "size": 60600564,
                    "sha256": "d94aa6be130a0bf05b6348ef533c39d73caeb31498ec5b8c9c615a6b0c268700",
                    "download_url": None,
                    "storage_available": False,
                    "external_download_url": "https://github.com/ojeshfrrr/larpiano/releases/download/v1.1.0-beta.2/Larpiano-v1.1.0-beta.2-win-x64.zip" if github_available else None,
                    "external_host_available": github_available,
                    "external_host_status": github_status,
                }
            ],
        },
        {
            "id": "rel-v1.1.0-beta.1",
            "tag_name": "v1.1.0-beta.1",
            "version": "v1.1.0-beta.1",
            "name": "Larpiano v1.1.0-beta.1",
            "channel": "Beta",
            "platform": "Windows x64 Portable",
            "date": "2026-11-07",
            "published_at": "2026-11-07T18:31:00Z",
            "created_at": "2026-11-07T18:31:00Z",
            "notes": "Initial v1.1.0 beta testing opt-in telemetry and presence architecture.",
            "body": "Initial v1.1.0 beta testing opt-in telemetry and presence architecture.",
            "prerelease": True,
            "githubUrl": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.1",
            "html_url": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.1.0-beta.1",
            "download_url": None,
            "storage_key": None,
            "file_name": "larpiano-v1.1.0-beta.1-windows-x64.zip",
            "file_size": None,
            "sha256": None,
            "is_current_latest": False,
            "release_metadata_available": True,
            "external_download_host_available": github_available,
            "external_download_host_status": github_status,
            "local_storage_available": False,
            "assets": [],
        },
        {
            "id": "rel-v1.0.35",
            "tag_name": "v1.0.35",
            "version": "v1.0.35",
            "name": "Larpiano v1.0.35",
            "channel": "Stable",
            "platform": "Windows x64 Portable",
            "date": "2026-11-08",
            "published_at": "2026-11-08T12:00:00Z",
            "created_at": "2026-11-08T12:00:00Z",
            "notes": "Visual reconstruction of the desktop application and floating overlay menu polish.",
            "body": "Visual reconstruction of the desktop application and floating overlay menu polish.",
            "prerelease": False,
            "githubUrl": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.35",
            "html_url": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.35",
            "download_url": None,
            "storage_key": None,
            "file_name": "larpiano-v1.0.35-windows-x64.zip",
            "file_size": None,
            "sha256": None,
            "is_current_latest": False,
            "release_metadata_available": True,
            "external_download_host_available": github_available,
            "external_download_host_status": github_status,
            "local_storage_available": False,
            "assets": [],
        },
        {
            "id": "rel-v1.0.0",
            "tag_name": "v1.0.0",
            "version": "v1.0.0",
            "name": "Larpiano v1.0.0 — Initial Release",
            "channel": "Stable",
            "platform": "Windows x64 Portable",
            "date": "2026-11-03",
            "published_at": "2026-11-03T14:00:00Z",
            "created_at": "2026-11-03T14:00:00Z",
            "notes": "Initial stable public release of Larpiano MIDI playback engine for Roblox.",
            "body": "Initial stable public release of Larpiano MIDI playback engine for Roblox.",
            "prerelease": False,
            "githubUrl": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.0",
            "html_url": "https://github.com/ojeshfrrr/larpiano/releases/tag/v1.0.0",
            "download_url": None,
            "storage_key": None,
            "file_name": "larpiano-v1.0.0-windows-x64.zip",
            "file_size": None,
            "sha256": None,
            "is_current_latest": False,
            "release_metadata_available": True,
            "external_download_host_available": github_available,
            "external_download_host_status": github_status,
            "local_storage_available": False,
            "assets": [],
        },
    ]


def get_releases_cached() -> list[dict]:
    """Retrieve authoritative releases decoupled from external GitHub availability.

    PostgreSQL / SQLite app_releases table serves as the primary source of truth.
    Upstream GitHub connectivity is probed with a non-blocking timeout and never prevents
    the Admin Releases page from loading.
    """
    now = datetime.datetime.now(datetime.timezone.utc).timestamp()
    if _RELEASES_CACHE["data"] and (now - _RELEASES_CACHE["timestamp"]) < RELEASES_TTL_SECONDS:
        return _RELEASES_CACHE["data"]

    github_available, github_status = _check_github_status()

    releases = []
    try:
        with get_db() as conn:
            cur = conn.cursor()
            cur.execute("""
                SELECT id, tag_name, version, channel, title, release_notes, prerelease,
                       published_at, created_at, storage_key, file_name, file_size, sha256,
                       download_url, github_url, github_download_url, is_current_latest
                FROM app_releases
                ORDER BY published_at DESC
            """)
            rows = cur.fetchall()
            for r in rows:
                row = dict(r)
                is_prerelease = bool(row.get("prerelease"))
                storage_key = row.get("storage_key")
                has_storage = bool(storage_key)

                # Verified download URL:
                # If local / Portways storage holds the release asset, return verified download route
                # If only external GitHub download URL exists, do not report it as available if GitHub is inaccessible
                if has_storage:
                    verified_dl_url = row.get("download_url") or f"/api/releases/{row['tag_name']}/{row['file_name']}"
                else:
                    verified_dl_url = row.get("github_download_url") if github_available else None

                assets = []
                if row.get("file_name"):
                    assets.append({
                        "name": row["file_name"],
                        "size": row.get("file_size"),
                        "sha256": row.get("sha256"),
                        "download_url": verified_dl_url,
                        "storage_available": has_storage,
                        "external_download_url": row.get("github_download_url") if github_available else None,
                        "external_host_available": github_available,
                        "external_host_status": github_status,
                    })

                pub_date = str(row.get("published_at") or "")[:10]

                releases.append({
                    "id": str(row["id"]),
                    "tag_name": row["tag_name"],
                    "version": row["tag_name"],
                    "name": row["title"],
                    "channel": row.get("channel") or ("Beta" if is_prerelease else "Stable"),
                    "platform": "Windows x64 Portable",
                    "date": pub_date,
                    "published_at": str(row.get("published_at")),
                    "created_at": str(row.get("created_at")),
                    "notes": row.get("release_notes") or "",
                    "body": row.get("release_notes") or "",
                    "prerelease": is_prerelease,
                    "githubUrl": row.get("github_url") or f"https://github.com/ojeshfrrr/larpiano/releases/tag/{row['tag_name']}",
                    "html_url": row.get("github_url") or f"https://github.com/ojeshfrrr/larpiano/releases/tag/{row['tag_name']}",
                    "download_url": verified_dl_url,
                    "storage_key": storage_key,
                    "file_name": row.get("file_name"),
                    "file_size": row.get("file_size"),
                    "sha256": row.get("sha256"),
                    "is_current_latest": bool(row.get("is_current_latest")),
                    "release_metadata_available": True,
                    "external_download_host_available": github_available,
                    "external_download_host_status": github_status,
                    "local_storage_available": has_storage,
                    "assets": assets,
                })
    except Exception as exc:
        log.warning("Could not read app_releases from database (%s); using verified fallback records", exc)
        releases = _get_static_release_definitions(github_available, github_status)

    if not releases:
        releases = _get_static_release_definitions(github_available, github_status)

    _RELEASES_CACHE["data"] = releases
    _RELEASES_CACHE["timestamp"] = now
    return releases


def trigger_playground_effect(data: dict, admin_id: str) -> dict:
    """Trigger a cosmetic playground effect on connected client(s)."""
    effect_type = data.get("effect_type", "lavender_takeover")
    target_user_id = data.get("target_user_id")
    duration_sec = int(data.get("duration_seconds", 30))

    event_id = str(uuid.uuid4())
    now = datetime.datetime.now(datetime.timezone.utc)
    expires = (now + datetime.timedelta(seconds=duration_sec)).isoformat()
    now_iso = now.isoformat()

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute(
                """INSERT INTO playground_events (id, target_user_id, effect_type, created_by, created_at, expires_at)
                   VALUES (%s, %s, %s, %s, NOW(), %s)""",
                (event_id, target_user_id, effect_type, admin_id, expires)
            )
        else:
            cur.execute(
                """INSERT INTO playground_events (id, target_user_id, effect_type, created_by, created_at, expires_at)
                   VALUES (?, ?, ?, ?, ?, ?)""",
                (event_id, target_user_id, effect_type, admin_id, now_iso, expires)
            )
        conn.commit()

    audit_log(admin_id, "PLAYGROUND_TRIGGERED", "effect", effect_type, {"target": target_user_id, "duration": duration_sec})
    return {"id": event_id, "effect_type": effect_type, "expires_at": expires}


def get_audit_logs(limit: int = 50, offset: int = 0) -> dict:
    """Retrieve paginated administrative audit logs."""
    limit = min(max(1, limit), 100)
    offset = max(0, offset)

    with get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("SELECT COUNT(*) as count FROM admin_audit_logs")
            total = cur.fetchone()["count"]

            cur.execute(
                """SELECT l.id, l.action, l.target_type, l.target_id, l.created_at, l.metadata,
                          u.display_name as admin_name, u.email as admin_email
                   FROM admin_audit_logs l
                   LEFT JOIN users u ON l.admin_user_id = u.id
                   ORDER BY l.created_at DESC
                   LIMIT %s OFFSET %s""",
                (limit, offset)
            )
            logs = [dict(r) for r in cur.fetchall()]
        else:
            cur.execute("SELECT COUNT(*) as count FROM admin_audit_logs")
            total = cur.fetchone()["count"]

            cur.execute(
                """SELECT l.id, l.action, l.target_type, l.target_id, l.created_at, l.metadata,
                          u.display_name as admin_name, u.email as admin_email
                   FROM admin_audit_logs l
                   LEFT JOIN users u ON l.admin_user_id = u.id
                   ORDER BY l.created_at DESC
                   LIMIT ? OFFSET ?""",
                (limit, offset)
            )
            logs = [dict(r) for r in cur.fetchall()]

    for item in logs:
        if isinstance(item.get("metadata"), str):
            try:
                item["metadata"] = json.loads(item["metadata"])
            except Exception:
                pass

    return {"total": total, "logs": logs, "limit": limit, "offset": offset}


def export_admin_data(export_type: str, admin_id: str) -> dict:
    """Export database table as CSV into S3/MinIO bucket under admin/exports/."""
    buf = io.StringIO()
    writer = csv.writer(buf)

    if export_type == "users":
        writer.writerow(["id", "email", "display_name", "role", "account_status", "created_at", "last_login_at"])
        with get_db() as conn:
            cur = conn.cursor()
            cur.execute("SELECT id, email, display_name, role, account_status, created_at, last_login_at FROM users")
            for row in cur.fetchall():
                writer.writerow(list(row))
    elif export_type == "sessions":
        writer.writerow(["id", "user_id", "session_identifier", "app_version", "started_at", "last_seen_at", "ended_at", "current_status"])
        with get_db() as conn:
            cur = conn.cursor()
            cur.execute("SELECT id, user_id, session_identifier, app_version, started_at, last_seen_at, ended_at, current_status FROM app_sessions")
            for row in cur.fetchall():
                writer.writerow(list(row))
    elif export_type == "audit":
        writer.writerow(["id", "admin_user_id", "action", "target_type", "target_id", "created_at", "metadata"])
        with get_db() as conn:
            cur = conn.cursor()
            cur.execute("SELECT id, admin_user_id, action, target_type, target_id, created_at, metadata FROM admin_audit_logs")
            for row in cur.fetchall():
                writer.writerow(list(row))
    else:
        raise ValueError("Invalid export_type. Allowed: users, sessions, audit")

    timestamp = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%d_%H%M%S")
    res = storage.export_csv(f"{export_type}_{timestamp}", buf.getvalue())
    audit_log(admin_id, "EXPORT_GENERATED", "export", res["key"], {"sha256": res["sha256"], "size": res["size"]})
    return res
