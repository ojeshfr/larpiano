"""Secure Authentication and Account Service for Larpiano.

Implements Argon2id password hashing, rotating refresh-token sessions with replay
detection, 6-digit email verification with attempt limits and resend cooldowns,
password recovery, and secure audit logging.
"""

from __future__ import annotations

import contextlib
import datetime
import hashlib
import hmac
import json
import logging
import os
import re
import secrets
import sqlite3
import threading
import time
from typing import Any, Optional, Tuple

import argon2
from argon2 import PasswordHasher
from argon2.exceptions import VerifyMismatchError
import jwt

import admin_api
from email_service import get_email_service

log = logging.getLogger("larpiano_auth")

# JWT Configuration
JWT_SECRET = os.environ.get("JWT_SECRET", "")
if not JWT_SECRET:
    if os.environ.get("DATABASE_URL", "").startswith(("postgres://", "postgresql://")):
        raise RuntimeError("JWT_SECRET must be configured before starting the production service.")
    JWT_SECRET = secrets.token_urlsafe(48)
elif len(JWT_SECRET.encode("utf-8")) < 32:
    raise RuntimeError("JWT_SECRET must contain at least 32 bytes.")
JWT_ALGORITHM = "HS256"
ACCESS_TOKEN_LIFETIME_SECONDS = 900  # 15 minutes
REFRESH_TOKEN_LIFETIME_SECONDS = 30 * 86400  # 30 days
VERIFICATION_CODE_LIFETIME_SECONDS = 600  # 10 minutes
PASSWORD_RESET_LIFETIME_SECONDS = 900  # 15 minutes
RESEND_COOLDOWN_SECONDS = 60  # 60s cooldown
MAX_VERIFICATION_ATTEMPTS = 5

# Argon2id password hasher
_ph = PasswordHasher(
    time_cost=2,
    memory_cost=65536,
    parallelism=1,
    hash_len=32,
    type=argon2.Type.ID,
)

# Email validation regex
EMAIL_REGEX = re.compile(r"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$")


class RateLimiter:
    """Thread-safe sliding-window rate limiter for sensitive endpoints."""

    def __init__(self):
        self._lock = threading.Lock()
        self._records: dict[str, list[float]] = {}

    def is_allowed(self, key: str, max_requests: int, window_seconds: float) -> bool:
        now = time.time()
        with self._lock:
            history = self._records.get(key, [])
            cutoff = now - window_seconds
            history = [t for t in history if t > cutoff]
            if len(history) >= max_requests:
                self._records[key] = history
                return False
            history.append(now)
            self._records[key] = history
            return True

    def reset(self, key: str):
        with self._lock:
            self._records.pop(key, None)


rate_limiter = RateLimiter()


def _ensure_auth_schema_sqlite(conn: sqlite3.Connection):
    """Ensure SQLite has auth schema in dev mode."""
    cur = conn.cursor()
    cur.executescript("""
        CREATE TABLE IF NOT EXISTS user_sessions (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            refresh_token_hash TEXT NOT NULL UNIQUE,
            access_token_jti TEXT,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            last_used_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at TEXT NOT NULL,
            revoked_at TEXT,
            user_agent TEXT,
            ip_address TEXT
        );

        CREATE TABLE IF NOT EXISTS email_verification_codes (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            code_hash TEXT NOT NULL,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at TEXT NOT NULL,
            used_at TEXT,
            attempts INTEGER NOT NULL DEFAULT 0,
            last_resent_at TEXT
        );

        CREATE TABLE IF NOT EXISTS password_reset_tokens (
            id TEXT PRIMARY KEY,
            user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            token_hash TEXT NOT NULL UNIQUE,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at TEXT NOT NULL,
            used_at TEXT
        );

        CREATE TABLE IF NOT EXISTS auth_audit_logs (
            id TEXT PRIMARY KEY,
            user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
            email TEXT,
            action TEXT NOT NULL,
            ip_address TEXT,
            user_agent TEXT,
            created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
            metadata TEXT NOT NULL DEFAULT '{}'
        );
    """)
    conn.commit()


def init_auth_db_if_needed():
    """Ensure tables exist in SQLite dev environment."""
    if not admin_api._is_pg():
        with admin_api.get_db() as conn:
            admin_api._ensure_sqlite_schema(conn)
            _ensure_auth_schema_sqlite(conn)


def hash_password(password: str) -> str:
    """Hash password using Argon2id."""
    return _ph.hash(password)


def verify_and_maybe_rehash(hash_str: str, password: str) -> Tuple[bool, Optional[str]]:
    """Verify password against Argon2id (or legacy salt:sha256).

    Returns (is_valid, new_hash_if_rehash_needed).
    """
    if not hash_str or not password:
        return False, None

    # Check for Argon2 hash
    if hash_str.startswith("$argon2"):
        try:
            _ph.verify(hash_str, password)
            if _ph.check_needs_rehash(hash_str):
                return True, _ph.hash(password)
            return True, None
        except VerifyMismatchError:
            return False, None
        except Exception:
            return False, None

    # Legacy salt:sha256 compatibility
    if ":" in hash_str:
        salt, expected_hash = hash_str.split(":", 1)
        actual_hash = hashlib.sha256((salt + password).encode("utf-8")).hexdigest()
        if hmac.compare_digest(actual_hash, expected_hash):
            # Upgraded to modern Argon2id
            return True, hash_password(password)

    return False, None


def validate_password_strength(password: str) -> Tuple[bool, str]:
    """Ensure password meets production strength requirements."""
    if not password or len(password) < 8:
        return False, "Password must be at least 8 characters long."
    if len(password) > 128:
        return False, "Password cannot exceed 128 characters."
    if not any(c.isdigit() for c in password):
        return False, "Password must include at least one digit."
    if not any(c.isalpha() for c in password):
        return False, "Password must include at least one letter."
    return True, ""


def validate_email_format(email: str) -> bool:
    if not email or len(email) > 255:
        return False
    return bool(EMAIL_REGEX.match(email.strip()))


def _hash_code(code: str) -> str:
    return hashlib.sha256(code.strip().encode("utf-8")).hexdigest()


def log_auth_audit(
    action: str,
    user_id: Optional[str] = None,
    email: Optional[str] = None,
    ip_address: Optional[str] = None,
    user_agent: Optional[str] = None,
    metadata: Optional[dict] = None,
):
    """Record an immutable entry in auth_audit_logs."""
    meta_json = json.dumps(metadata or {})
    audit_id = str(secrets.token_hex(16))
    now = datetime.datetime.now(datetime.timezone.utc)

    try:
        with admin_api.get_db() as conn:
            cur = conn.cursor()
            if admin_api._is_pg():
                cur.execute(
                    """
                    INSERT INTO auth_audit_logs (id, user_id, email, action, ip_address, user_agent, created_at, metadata)
                    VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
                    """,
                    (audit_id, user_id, email, action, ip_address, user_agent, now, meta_json),
                )
            else:
                cur.execute(
                    """
                    INSERT INTO auth_audit_logs (id, user_id, email, action, ip_address, user_agent, created_at, metadata)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (audit_id, user_id, email, action, ip_address, user_agent, now.isoformat(), meta_json),
                )
            conn.commit()
    except Exception as exc:
        log.error("Failed to write auth audit log: %s", exc)


def create_access_token(user: dict, session_id: str) -> str:
    """Create short-lived JWT access token."""
    now = int(time.time())
    payload = {
        "sub": str(user["id"]),
        "email": user["email"],
        "display_name": user["display_name"],
        "role": user["role"],
        "email_verified": bool(user["email_verified"]),
        "jti": secrets.token_hex(16),
        "sid": session_id,
        "iat": now,
        "exp": now + ACCESS_TOKEN_LIFETIME_SECONDS,
        "type": "access",
    }
    return jwt.encode(payload, JWT_SECRET, algorithm=JWT_ALGORITHM)


def decode_access_token(token: str) -> Optional[dict]:
    """Decode and validate access token signature and expiry."""
    try:
        payload = jwt.decode(token, JWT_SECRET, algorithms=[JWT_ALGORITHM])
        if payload.get("type") != "access":
            return None
        session_id = payload.get("sid")
        if not session_id or not payload.get("sub"):
            return None
        with admin_api.get_db() as conn:
            cur = conn.cursor()
            marker = "%s" if admin_api._is_pg() else "?"
            cur.execute(f"SELECT s.revoked_at, s.expires_at, u.account_status FROM user_sessions s JOIN users u ON u.id = s.user_id WHERE s.id = {marker} AND s.user_id = {marker}", (session_id, payload["sub"]))
            row = cur.fetchone()
            if not row or row["revoked_at"] is not None or row["account_status"] != "active":
                return None
            expires = row["expires_at"]
            if isinstance(expires, str):
                expires = datetime.datetime.fromisoformat(expires.replace("Z", "+00:00"))
            if expires.tzinfo is None:
                expires = expires.replace(tzinfo=datetime.timezone.utc)
            if expires <= datetime.datetime.now(datetime.timezone.utc):
                return None
        return payload
    except Exception:
        return None


def register_user(
    email: str,
    password: str,
    display_name: str = "",
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str, Optional[dict]]:
    """Register a new user account.

    Always sets role='user' and email_verified=False.
    Sends 6-digit verification email.
    """
    init_auth_db_if_needed()

    clean_email = email.strip().lower()
    if not validate_email_format(clean_email):
        return False, "Invalid email address format.", None

    valid_pw, pw_err = validate_password_strength(password)
    if not valid_pw:
        return False, pw_err, None

    clean_name = display_name.strip()
    if not clean_name:
        clean_name = clean_email.split("@")[0][:30]
    elif len(clean_name) > 60:
        clean_name = clean_name[:60]

    rate_key = f"reg:{ip_address}"
    if not rate_limiter.is_allowed(rate_key, max_requests=10, window_seconds=3600):
        return False, "Too many registration attempts. Please try again later.", None

    pwd_hash = hash_password(password)
    user_id = str(secrets.token_hex(16))
    now = datetime.datetime.now(datetime.timezone.utc)

    # 6-digit cryptographically secure verification code
    raw_code = f"{secrets.randbelow(900000) + 100000:06d}"
    code_hash = _hash_code(raw_code)
    expires_at = now + datetime.timedelta(seconds=VERIFICATION_CODE_LIFETIME_SECONDS)

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        # Check existing user
        if admin_api._is_pg():
            cur.execute("SELECT id, email_verified FROM users WHERE email = %s", (clean_email,))
        else:
            cur.execute("SELECT id, email_verified FROM users WHERE email = ?", (clean_email,))
        existing = cur.fetchone()

        if existing:
            # If already verified, do not reveal user presence
            # Return generic success to avoid enumeration
            log_auth_audit("REGISTER_DUPLICATE_ATTEMPT", email=clean_email, ip_address=ip_address, user_agent=user_agent)
            return True, "If this email is not already verified, a verification code has been dispatched.", None

        # Insert user
        if admin_api._is_pg():
            cur.execute(
                """
                INSERT INTO users (id, email, display_name, password_hash, email_verified, role, account_status, created_at)
                VALUES (%s, %s, %s, %s, FALSE, 'user', 'active', %s)
                """,
                (user_id, clean_email, clean_name, pwd_hash, now),
            )
            # Create default preferences - strictly opt-in: default to FALSE for new accounts
            cur.execute(
                """
                INSERT INTO user_preferences (user_id, presence_consent, telemetry_consent, updated_at)
                VALUES (%s, FALSE, FALSE, %s)
                ON CONFLICT (user_id) DO NOTHING
                """,
                (user_id, now),
            )
            # Create verification code entry
            cur.execute(
                """
                INSERT INTO email_verification_codes (id, user_id, code_hash, created_at, expires_at, attempts, last_resent_at)
                VALUES (%s, %s, %s, %s, %s, 0, %s)
                """,
                (str(secrets.token_hex(16)), user_id, code_hash, now, expires_at, now),
            )
        else:
            cur.execute(
                """
                INSERT INTO users (id, email, display_name, password_hash, email_verified, role, account_status, created_at)
                VALUES (?, ?, ?, ?, 0, 'user', 'active', ?)
                """,
                (user_id, clean_email, clean_name, pwd_hash, now.isoformat()),
            )
            cur.execute(
                """
                INSERT INTO user_preferences (user_id, presence_consent, telemetry_consent, updated_at)
                VALUES (?, 0, 0, ?)
                ON CONFLICT (user_id) DO NOTHING
                """,
                (user_id, now.isoformat()),
            )
            cur.execute(
                """
                INSERT INTO email_verification_codes (id, user_id, code_hash, created_at, expires_at, attempts, last_resent_at)
                VALUES (?, ?, ?, ?, ?, 0, ?)
                """,
                (str(secrets.token_hex(16)), user_id, code_hash, now.isoformat(), expires_at.isoformat(), now.isoformat()),
            )
        conn.commit()

    # Send verification email via provider
    email_service = get_email_service()
    if not email_service.send_verification_email(clean_email, clean_name, raw_code):
        return False, "Could not send the verification email. Please request another code later.", None

    log_auth_audit("USER_REGISTERED", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    return True, "Registration successful. Please enter the 6-digit verification code sent to your email.", {
        "user_id": user_id,
        "email": clean_email,
        "display_name": clean_name,
    }


def verify_email_code(
    email: str,
    code: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str, Optional[dict]]:
    """Validate 6-digit verification code and mark account verified."""
    init_auth_db_if_needed()

    clean_email = email.strip().lower()
    clean_code = code.strip()

    if not clean_code or len(clean_code) != 6 or not clean_code.isdigit():
        return False, "Verification code must be 6 digits.", None

    rate_key = f"verify:{clean_email}"
    if not rate_limiter.is_allowed(rate_key, max_requests=10, window_seconds=600):
        return False, "Too many verification attempts. Please wait.", None

    now = datetime.datetime.now(datetime.timezone.utc)
    target_hash = _hash_code(clean_code)

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        if admin_api._is_pg():
            cur.execute("SELECT id, email, display_name, role, email_verified, account_status FROM users WHERE email = %s", (clean_email,))
        else:
            cur.execute("SELECT id, email, display_name, role, email_verified, account_status FROM users WHERE email = ?", (clean_email,))
        user = cur.fetchone()

        if not user:
            return False, "Invalid or expired verification code.", None

        user_id = str(user["id"])

        # Fetch latest active code
        if admin_api._is_pg():
            cur.execute(
                """
                SELECT id, code_hash, expires_at, attempts
                FROM email_verification_codes
                WHERE user_id = %s AND used_at IS NULL
                ORDER BY created_at DESC LIMIT 1
                """,
                (user_id,),
            )
        else:
            cur.execute(
                """
                SELECT id, code_hash, expires_at, attempts
                FROM email_verification_codes
                WHERE user_id = ? AND used_at IS NULL
                ORDER BY created_at DESC LIMIT 1
                """,
                (user_id,),
            )
        code_record = cur.fetchone()

        if not code_record:
            return False, "No active verification code found. Please request a new one.", None

        # Check attempt limits
        attempts = int(code_record["attempts"])
        if attempts >= MAX_VERIFICATION_ATTEMPTS:
            return False, "Too many failed attempts. This code is invalidated. Please request a new code.", None

        # Check expiration
        expires_at = code_record["expires_at"]
        if isinstance(expires_at, str):
            try:
                expires_at = datetime.datetime.fromisoformat(expires_at.replace("Z", "+00:00"))
            except Exception:
                pass
        if isinstance(expires_at, datetime.datetime):
            if expires_at.tzinfo is None:
                expires_at = expires_at.replace(tzinfo=datetime.timezone.utc)
            if now > expires_at:
                return False, "Verification code has expired. Please request a new one.", None

        # Check code hash
        expected_hash = code_record["code_hash"]
        if not hmac.compare_digest(expected_hash, target_hash):
            # Increment attempts
            new_attempts = attempts + 1
            if admin_api._is_pg():
                cur.execute("UPDATE email_verification_codes SET attempts = %s WHERE id = %s", (new_attempts, code_record["id"]))
            else:
                cur.execute("UPDATE email_verification_codes SET attempts = ? WHERE id = ?", (new_attempts, code_record["id"]))
            conn.commit()
            remaining = MAX_VERIFICATION_ATTEMPTS - new_attempts
            return False, f"Incorrect verification code. {remaining} attempt(s) remaining.", None

        # Mark code used and user verified
        if admin_api._is_pg():
            cur.execute("UPDATE email_verification_codes SET used_at = %s WHERE id = %s", (now, code_record["id"]))
            cur.execute("UPDATE users SET email_verified = TRUE WHERE id = %s", (user_id,))
        else:
            cur.execute("UPDATE email_verification_codes SET used_at = ? WHERE id = ?", (now.isoformat(), code_record["id"]))
            cur.execute("UPDATE users SET email_verified = 1 WHERE id = ?", (user_id,))
        conn.commit()

    log_auth_audit("EMAIL_VERIFIED", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    # Issue initial session
    tokens = create_user_session(user_id=user_id, ip_address=ip_address, user_agent=user_agent)

    pref_presence = False
    pref_telemetry = False
    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute("SELECT presence_consent, telemetry_consent FROM user_preferences WHERE user_id = %s", (user_id,))
        else:
            cur.execute("SELECT presence_consent, telemetry_consent FROM user_preferences WHERE user_id = ?", (user_id,))
        pref_row = cur.fetchone()
        if pref_row:
            if pref_row["presence_consent"] is not None:
                pref_presence = bool(pref_row["presence_consent"])
            if pref_row["telemetry_consent"] is not None:
                pref_telemetry = bool(pref_row["telemetry_consent"])

    return True, "Email verified successfully.", {
        "user": {
            "id": user_id,
            "email": user["email"],
            "display_name": user["display_name"],
            "role": user["role"],
            "email_verified": True,
            "presence_consent": pref_presence,
            "telemetry_consent": pref_telemetry,
        },
        "tokens": tokens,
    }


def resend_verification_code(
    email: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str, Optional[int]]:
    """Resend 6-digit verification code with a 60-second cooldown."""
    init_auth_db_if_needed()

    clean_email = email.strip().lower()
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        if admin_api._is_pg():
            cur.execute("SELECT id, display_name, email_verified FROM users WHERE email = %s", (clean_email,))
        else:
            cur.execute("SELECT id, display_name, email_verified FROM users WHERE email = ?", (clean_email,))
        user = cur.fetchone()

        if not user:
            # Generic response to prevent enumeration
            return True, "If your email is registered and unverified, a new code was sent.", 60

        if user["email_verified"]:
            return False, "This account is already verified.", 0

        user_id = str(user["id"])

        # Check last_resent_at on latest code
        if admin_api._is_pg():
            cur.execute(
                """
                SELECT last_resent_at, created_at FROM email_verification_codes
                WHERE user_id = %s ORDER BY created_at DESC LIMIT 1
                """,
                (user_id,),
            )
        else:
            cur.execute(
                """
                SELECT last_resent_at, created_at FROM email_verification_codes
                WHERE user_id = ? ORDER BY created_at DESC LIMIT 1
                """,
                (user_id,),
            )
        latest = cur.fetchone()

        if latest:
            last_sent = latest["last_resent_at"] or latest["created_at"]
            if isinstance(last_sent, str):
                try:
                    last_sent = datetime.datetime.fromisoformat(last_sent.replace("Z", "+00:00"))
                except Exception:
                    pass
            if isinstance(last_sent, datetime.datetime):
                if last_sent.tzinfo is None:
                    last_sent = last_sent.replace(tzinfo=datetime.timezone.utc)
                elapsed = (now - last_sent).total_seconds()
                if elapsed < RESEND_COOLDOWN_SECONDS:
                    wait_time = int(RESEND_COOLDOWN_SECONDS - elapsed)
                    return False, f"Please wait {wait_time}s before requesting another code.", wait_time

        # Invalidate old unused codes
        if admin_api._is_pg():
            cur.execute("UPDATE email_verification_codes SET used_at = %s WHERE user_id = %s AND used_at IS NULL", (now, user_id))
        else:
            cur.execute("UPDATE email_verification_codes SET used_at = ? WHERE user_id = ? AND used_at IS NULL", (now.isoformat(), user_id))

        # Generate new 6-digit code
        raw_code = f"{secrets.randbelow(900000) + 100000:06d}"
        code_hash = _hash_code(raw_code)
        expires_at = now + datetime.timedelta(seconds=VERIFICATION_CODE_LIFETIME_SECONDS)

        if admin_api._is_pg():
            cur.execute(
                """
                INSERT INTO email_verification_codes (id, user_id, code_hash, created_at, expires_at, attempts, last_resent_at)
                VALUES (%s, %s, %s, %s, %s, 0, %s)
                """,
                (str(secrets.token_hex(16)), user_id, code_hash, now, expires_at, now),
            )
        else:
            cur.execute(
                """
                INSERT INTO email_verification_codes (id, user_id, code_hash, created_at, expires_at, attempts, last_resent_at)
                VALUES (?, ?, ?, ?, ?, 0, ?)
                """,
                (str(secrets.token_hex(16)), user_id, code_hash, now.isoformat(), expires_at.isoformat(), now.isoformat()),
            )
        conn.commit()

    email_service = get_email_service()
    if not email_service.send_verification_email(clean_email, user["display_name"], raw_code):
        return False, "Could not send the verification email. Please try again later.", 60

    log_auth_audit("VERIFICATION_CODE_RESENT", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    return True, "A new 6-digit verification code has been dispatched to your email.", 60


def login_user(
    email: str,
    password: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str, Optional[dict]]:
    """Authenticate user with email and password.

    Issues short-lived JWT access token and rotating refresh token session.
    """
    init_auth_db_if_needed()

    clean_email = email.strip().lower()

    # Rate limiting on login attempts
    rate_key = f"login:{clean_email}:{ip_address}"
    if not rate_limiter.is_allowed(rate_key, max_requests=10, window_seconds=300):
        return False, "Too many failed login attempts. Please wait 5 minutes.", None

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        if admin_api._is_pg():
            cur.execute(
                """
                SELECT id, email, display_name, password_hash, email_verified, role, account_status
                FROM users WHERE email = %s
                """,
                (clean_email,),
            )
        else:
            cur.execute(
                """
                SELECT id, email, display_name, password_hash, email_verified, role, account_status
                FROM users WHERE email = ?
                """,
                (clean_email,),
            )
        user = cur.fetchone()

        if not user or not user["password_hash"]:
            # Timing attack mitigation: do a dummy hash verification
            try:
                _ph.verify("$argon2id$v=19$m=65536,t=2,p=1$c29tZXNhbHQ$P2eE232pA9Fw32qjD6m8eQ", "dummy-timing-password")
            except Exception:
                pass
            return False, "Invalid email or password.", None

        if user["account_status"] != "active":
            return False, "This account is currently inactive or suspended.", None

        is_valid, new_hash = verify_and_maybe_rehash(user["password_hash"], password)
        if not is_valid:
            log_auth_audit("LOGIN_FAILED", email=clean_email, ip_address=ip_address, user_agent=user_agent)
            return False, "Invalid email or password.", None
        if not user["email_verified"]:
            return False, "Verify your email before signing in.", None

        user_id = str(user["id"])
        now = datetime.datetime.now(datetime.timezone.utc)

        # Update last_login_at and rehash if upgraded from legacy
        if new_hash:
            if admin_api._is_pg():
                cur.execute("UPDATE users SET last_login_at = %s, password_hash = %s WHERE id = %s", (now, new_hash, user_id))
            else:
                cur.execute("UPDATE users SET last_login_at = ?, password_hash = ? WHERE id = ?", (now.isoformat(), new_hash, user_id))
        else:
            if admin_api._is_pg():
                cur.execute("UPDATE users SET last_login_at = %s WHERE id = %s", (now, user_id))
            else:
                cur.execute("UPDATE users SET last_login_at = ? WHERE id = ?", (now.isoformat(), user_id))
        conn.commit()

    # Reset rate limit on success
    rate_limiter.reset(rate_key)

    log_auth_audit("LOGIN_SUCCESS", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    tokens = create_user_session(user_id=user_id, ip_address=ip_address, user_agent=user_agent)

    pref_presence = False
    pref_telemetry = False
    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute("SELECT presence_consent, telemetry_consent FROM user_preferences WHERE user_id = %s", (user_id,))
        else:
            cur.execute("SELECT presence_consent, telemetry_consent FROM user_preferences WHERE user_id = ?", (user_id,))
        pref_row = cur.fetchone()
        if pref_row:
            if pref_row["presence_consent"] is not None:
                pref_presence = bool(pref_row["presence_consent"])
            if pref_row["telemetry_consent"] is not None:
                pref_telemetry = bool(pref_row["telemetry_consent"])

    user_data = {
        "id": user_id,
        "email": user["email"],
        "display_name": user["display_name"],
        "role": user["role"],
        "email_verified": bool(user["email_verified"]),
        "presence_consent": pref_presence,
        "telemetry_consent": pref_telemetry,
    }

    return True, "Login successful.", {
        "user": user_data,
        "tokens": tokens,
    }


def create_user_session(
    user_id: str,
    ip_address: str = "",
    user_agent: str = "",
) -> dict[str, Any]:
    """Generates an access token and an opaque rotating refresh token session."""
    init_auth_db_if_needed()

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute("SELECT id, email, display_name, role, email_verified FROM users WHERE id = %s", (user_id,))
        else:
            cur.execute("SELECT id, email, display_name, role, email_verified FROM users WHERE id = ?", (user_id,))
        user = cur.fetchone()

    if not user:
        raise ValueError("User not found.")

    # 48-byte URL-safe refresh token
    raw_refresh_token = secrets.token_urlsafe(48)
    refresh_hash = hashlib.sha256(raw_refresh_token.encode("utf-8")).hexdigest()

    session_id = str(secrets.token_hex(16))
    access_token = create_access_token(user, session_id)
    now = datetime.datetime.now(datetime.timezone.utc)
    expires_at = now + datetime.timedelta(seconds=REFRESH_TOKEN_LIFETIME_SECONDS)

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute(
                """
                INSERT INTO user_sessions (id, user_id, refresh_token_hash, created_at, last_used_at, expires_at, user_agent, ip_address)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
                """,
                (session_id, user_id, refresh_hash, now, now, expires_at, user_agent, ip_address),
            )
        else:
            cur.execute(
                """
                INSERT INTO user_sessions (id, user_id, refresh_token_hash, created_at, last_used_at, expires_at, user_agent, ip_address)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (session_id, user_id, refresh_hash, now.isoformat(), now.isoformat(), expires_at.isoformat(), user_agent, ip_address),
            )
        conn.commit()

    return {
        "access_token": access_token,
        "refresh_token": raw_refresh_token,
        "token_type": "Bearer",
        "expires_in": ACCESS_TOKEN_LIFETIME_SECONDS,
    }


def rotate_refresh_token(
    raw_refresh_token: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str, Optional[dict]]:
    """Rotates refresh token and performs token replay detection."""
    init_auth_db_if_needed()

    if not raw_refresh_token:
        return False, "Missing refresh token.", None

    token_hash = hashlib.sha256(raw_refresh_token.strip().encode("utf-8")).hexdigest()
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        if admin_api._is_pg():
            cur.execute(
                """
                SELECT s.id, s.user_id, s.expires_at, s.revoked_at, u.account_status, u.role, u.email, u.display_name, u.email_verified
                FROM user_sessions s
                JOIN users u ON s.user_id = u.id
                WHERE s.refresh_token_hash = %s FOR UPDATE OF s
                """,
                (token_hash,),
            )
        else:
            cur.execute(
                """
                SELECT s.id, s.user_id, s.expires_at, s.revoked_at, u.account_status, u.role, u.email, u.display_name, u.email_verified
                FROM user_sessions s
                JOIN users u ON s.user_id = u.id
                WHERE s.refresh_token_hash = ?
                """,
                (token_hash,),
            )
        session = cur.fetchone()

        if not session:
            return False, "Invalid refresh session.", None

        user_id = str(session["user_id"])

        # Replay Attack Detection!
        if session["revoked_at"] is not None:
            # Token was already used or revoked! Potential session hijacking!
            # Revoke ALL active sessions for this user immediately
            if admin_api._is_pg():
                cur.execute("UPDATE user_sessions SET revoked_at = %s WHERE user_id = %s", (now, user_id))
            else:
                cur.execute("UPDATE user_sessions SET revoked_at = ? WHERE user_id = ?", (now.isoformat(), user_id))
            conn.commit()

            log_auth_audit(
                "REPLAY_ATTACK_DETECTED",
                user_id=user_id,
                email=session["email"],
                ip_address=ip_address,
                user_agent=user_agent,
                metadata={"revoked_session_id": str(session["id"])},
            )
            return False, "Security alert: Compromised session detected. All sessions have been revoked.", None

        # Check expiration
        expires_at = session["expires_at"]
        if isinstance(expires_at, str):
            try:
                expires_at = datetime.datetime.fromisoformat(expires_at.replace("Z", "+00:00"))
            except Exception:
                pass
        if isinstance(expires_at, datetime.datetime):
            if expires_at.tzinfo is None:
                expires_at = expires_at.replace(tzinfo=datetime.timezone.utc)
            if now > expires_at:
                return False, "Refresh session expired. Please sign in again.", None

        if session["account_status"] != "active":
            return False, "Account is inactive.", None

        # Revoke the used session
        session_id = str(session["id"])
        if admin_api._is_pg():
            cur.execute("UPDATE user_sessions SET revoked_at = %s WHERE id = %s", (now, session_id))
        else:
            cur.execute("UPDATE user_sessions SET revoked_at = ? WHERE id = ?", (now.isoformat(), session_id))

        # Generate new rotating pair
        new_refresh_token = secrets.token_urlsafe(48)
        new_refresh_hash = hashlib.sha256(new_refresh_token.encode("utf-8")).hexdigest()
        new_session_id = str(secrets.token_hex(16))
        new_expires_at = now + datetime.timedelta(seconds=REFRESH_TOKEN_LIFETIME_SECONDS)

        if admin_api._is_pg():
            cur.execute(
                """
                INSERT INTO user_sessions (id, user_id, refresh_token_hash, created_at, last_used_at, expires_at, user_agent, ip_address)
                VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
                """,
                (new_session_id, user_id, new_refresh_hash, now, now, new_expires_at, user_agent, ip_address),
            )
        else:
            cur.execute(
                """
                INSERT INTO user_sessions (id, user_id, refresh_token_hash, created_at, last_used_at, expires_at, user_agent, ip_address)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (new_session_id, user_id, new_refresh_hash, now.isoformat(), now.isoformat(), new_expires_at.isoformat(), user_agent, ip_address),
            )
        conn.commit()

    user_dict = {
        "id": user_id,
        "email": session["email"],
        "display_name": session["display_name"],
        "role": session["role"],
        "email_verified": bool(session["email_verified"]),
    }
    new_access_token = create_access_token(user_dict, new_session_id)

    log_auth_audit("TOKEN_ROTATED", user_id=user_id, email=session["email"], ip_address=ip_address, user_agent=user_agent)

    return True, "Token rotated successfully.", {
        "access_token": new_access_token,
        "refresh_token": new_refresh_token,
        "token_type": "Bearer",
        "expires_in": ACCESS_TOKEN_LIFETIME_SECONDS,
        "user": user_dict,
    }


def logout_session(
    raw_refresh_token: str = "",
    access_token: str = "",
    ip_address: str = "",
    user_agent: str = "",
) -> bool:
    """Revoke a refresh token and/or access token session."""
    now = datetime.datetime.now(datetime.timezone.utc)
    revoked_user_id = None

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        marker = "%s" if admin_api._is_pg() else "?"
        now_val = now if admin_api._is_pg() else now.isoformat()

        # 1. Revoke by refresh token hash if provided
        if raw_refresh_token and raw_refresh_token.strip():
            token_hash = hashlib.sha256(raw_refresh_token.strip().encode("utf-8")).hexdigest()
            cur.execute(f"SELECT user_id FROM user_sessions WHERE refresh_token_hash = {marker}", (token_hash,))
            row = cur.fetchone()
            if row:
                revoked_user_id = row["user_id"] if isinstance(row, dict) else row[0]
            cur.execute(f"UPDATE user_sessions SET revoked_at = {marker} WHERE refresh_token_hash = {marker}", (now_val, token_hash))

        # 2. Revoke by access token session_id if provided
        if access_token and access_token.strip():
            try:
                payload = jwt.decode(access_token.strip(), JWT_SECRET, algorithms=[JWT_ALGORITHM], options={"verify_exp": False})
                sid = payload.get("sid")
                sub = payload.get("sub")
                if sid:
                    cur.execute(f"UPDATE user_sessions SET revoked_at = {marker} WHERE id = {marker}", (now_val, sid))
                    if not revoked_user_id and sub:
                        revoked_user_id = sub
            except Exception:
                pass

        conn.commit()

    if revoked_user_id:
        log_auth_audit("LOGOUT_SESSION", user_id=str(revoked_user_id), ip_address=ip_address, user_agent=user_agent)

    return True


def logout_all_sessions(
    user_id: str,
    ip_address: str = "",
    user_agent: str = "",
) -> bool:
    """Revoke all sessions for a user."""
    now = datetime.datetime.now(datetime.timezone.utc)
    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute("UPDATE user_sessions SET revoked_at = %s WHERE user_id = %s", (now, user_id))
        else:
            cur.execute("UPDATE user_sessions SET revoked_at = ? WHERE user_id = ?", (now.isoformat(), user_id))
        conn.commit()

    log_auth_audit("LOGOUT_ALL_SESSIONS", user_id=user_id, ip_address=ip_address, user_agent=user_agent)
    return True


def request_password_reset(
    email: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str]:
    """Request password reset code. Generic response to prevent enumeration."""
    init_auth_db_if_needed()

    clean_email = email.strip().lower()

    rate_key = f"pwd_req:{clean_email}"
    if not rate_limiter.is_allowed(rate_key, max_requests=5, window_seconds=900):
        return False, "Too many password reset requests. Please try again later."

    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute("SELECT id, display_name FROM users WHERE email = %s", (clean_email,))
        else:
            cur.execute("SELECT id, display_name FROM users WHERE email = ?", (clean_email,))
        user = cur.fetchone()

        if user:
            user_id = str(user["id"])
            # Generate 6-digit recovery code
            raw_code = f"{secrets.randbelow(900000) + 100000:06d}"
            code_hash = _hash_code(raw_code)
            expires_at = now + datetime.timedelta(seconds=PASSWORD_RESET_LIFETIME_SECONDS)

            # Invalidate prior unused tokens
            if admin_api._is_pg():
                cur.execute("UPDATE password_reset_tokens SET used_at = %s WHERE user_id = %s AND used_at IS NULL", (now, user_id))
                cur.execute(
                    """
                    INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at)
                    VALUES (%s, %s, %s, %s, %s)
                    """,
                    (str(secrets.token_hex(16)), user_id, code_hash, now, expires_at),
                )
            else:
                cur.execute("UPDATE password_reset_tokens SET used_at = ? WHERE user_id = ? AND used_at IS NULL", (now.isoformat(), user_id))
                cur.execute(
                    """
                    INSERT INTO password_reset_tokens (id, user_id, token_hash, created_at, expires_at)
                    VALUES (?, ?, ?, ?, ?)
                    """,
                    (str(secrets.token_hex(16)), user_id, code_hash, now.isoformat(), expires_at.isoformat()),
                )
            conn.commit()

            email_service = get_email_service()
            email_service.send_password_reset_email(clean_email, user["display_name"], raw_code)

            log_auth_audit("PASSWORD_RESET_REQUESTED", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    return True, "If this email is associated with an account, a password reset code has been sent."


def reset_password_with_code(
    email: str,
    code: str,
    new_password: str,
    ip_address: str = "",
    user_agent: str = "",
) -> Tuple[bool, str]:
    """Verify recovery code and reset password. Revokes all active sessions."""
    init_auth_db_if_needed()

    clean_email = email.strip().lower()
    clean_code = code.strip()

    if not rate_limiter.is_allowed(f"reset:{clean_email}", max_requests=8, window_seconds=900):
        return False, "Too many recovery attempts. Please try again later."

    valid_pw, pw_err = validate_password_strength(new_password)
    if not valid_pw:
        return False, pw_err

    target_hash = _hash_code(clean_code)
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()

        if admin_api._is_pg():
            cur.execute("SELECT id FROM users WHERE email = %s", (clean_email,))
        else:
            cur.execute("SELECT id FROM users WHERE email = ?", (clean_email,))
        user = cur.fetchone()

        if not user:
            return False, "Invalid or expired recovery code."

        user_id = str(user["id"])

        if admin_api._is_pg():
            cur.execute(
                """
                SELECT id, expires_at, used_at FROM password_reset_tokens
                WHERE user_id = %s AND token_hash = %s
                ORDER BY created_at DESC LIMIT 1
                """,
                (user_id, target_hash),
            )
        else:
            cur.execute(
                """
                SELECT id, expires_at, used_at FROM password_reset_tokens
                WHERE user_id = ? AND token_hash = ?
                ORDER BY created_at DESC LIMIT 1
                """,
                (user_id, target_hash),
            )
        token_record = cur.fetchone()

        if not token_record or token_record["used_at"] is not None:
            return False, "Invalid or expired recovery code."

        expires_at = token_record["expires_at"]
        if isinstance(expires_at, str):
            try:
                expires_at = datetime.datetime.fromisoformat(expires_at.replace("Z", "+00:00"))
            except Exception:
                pass
        if isinstance(expires_at, datetime.datetime):
            if expires_at.tzinfo is None:
                expires_at = expires_at.replace(tzinfo=datetime.timezone.utc)
            if now > expires_at:
                return False, "Recovery code has expired. Please request a new one."

        # Mark token used
        new_pwd_hash = hash_password(new_password)
        if admin_api._is_pg():
            cur.execute("UPDATE password_reset_tokens SET used_at = %s WHERE id = %s", (now, token_record["id"]))
            cur.execute("UPDATE users SET password_hash = %s WHERE id = %s", (new_pwd_hash, user_id))
            # Invalidate all user sessions
            cur.execute("UPDATE user_sessions SET revoked_at = %s WHERE user_id = %s", (now, user_id))
        else:
            cur.execute("UPDATE password_reset_tokens SET used_at = ? WHERE id = ?", (now.isoformat(), token_record["id"]))
            cur.execute("UPDATE users SET password_hash = ? WHERE id = ?", (new_pwd_hash, user_id))
            cur.execute("UPDATE user_sessions SET revoked_at = ? WHERE user_id = ?", (now.isoformat(), user_id))
        conn.commit()

    log_auth_audit("PASSWORD_RESET_SUCCESS", user_id=user_id, email=clean_email, ip_address=ip_address, user_agent=user_agent)

    return True, "Password reset successfully. You may now sign in with your new password."


def get_current_user_profile(user_id: str) -> Optional[dict]:
    """Retrieve authenticated user's profile and consent preferences."""
    init_auth_db_if_needed()

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute(
                """
                SELECT u.id, u.email, u.display_name, u.role, u.account_status, u.email_verified, u.created_at, u.last_login_at,
                       p.telemetry_consent, p.presence_consent
                FROM users u
                LEFT JOIN user_preferences p ON u.id = p.user_id
                WHERE u.id = %s
                """,
                (user_id,),
            )
        else:
            cur.execute(
                """
                SELECT u.id, u.email, u.display_name, u.role, u.account_status, u.email_verified, u.created_at, u.last_login_at,
                       p.telemetry_consent, p.presence_consent
                FROM users u
                LEFT JOIN user_preferences p ON u.id = p.user_id
                WHERE u.id = ?
                """,
                (user_id,),
            )
        row = cur.fetchone()
        if not row:
            return None

        return {
            "id": str(row["id"]),
            "email": row["email"],
            "display_name": row["display_name"],
            "role": row["role"],
            "account_status": row["account_status"],
            "email_verified": bool(row["email_verified"]),
            "created_at": str(row["created_at"]),
            "last_login_at": str(row["last_login_at"]) if row["last_login_at"] else None,
            "telemetry_consent": bool(row["telemetry_consent"]) if row["telemetry_consent"] is not None else False,
            "presence_consent": bool(row["presence_consent"]) if row["presence_consent"] is not None else False,
        }


def update_user_preferences(
    user_id: str,
    presence_consent: bool,
    telemetry_consent: bool = False,
) -> bool:
    """Updates presence and telemetry consent preferences for user."""
    init_auth_db_if_needed()
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute(
                """
                INSERT INTO user_preferences (user_id, presence_consent, telemetry_consent, updated_at)
                VALUES (%s, %s, %s, %s)
                ON CONFLICT (user_id) DO UPDATE SET
                    presence_consent = EXCLUDED.presence_consent,
                    telemetry_consent = EXCLUDED.telemetry_consent,
                    updated_at = EXCLUDED.updated_at
                """,
                (user_id, presence_consent, telemetry_consent, now),
            )
        else:
            cur.execute(
                """
                INSERT INTO user_preferences (user_id, presence_consent, telemetry_consent, updated_at)
                VALUES (?, ?, ?, ?)
                ON CONFLICT (user_id) DO UPDATE SET
                    presence_consent = excluded.presence_consent,
                    telemetry_consent = excluded.telemetry_consent,
                    updated_at = excluded.updated_at
                """,
                (user_id, 1 if presence_consent else 0, 1 if telemetry_consent else 0, now.isoformat()),
            )
        conn.commit()

    log_auth_audit(
        "PREFERENCES_UPDATED",
        user_id=user_id,
        metadata={"presence_consent": presence_consent, "telemetry_consent": telemetry_consent},
    )
    return True


def list_user_sessions(user_id: str) -> list[dict]:
    """Lists non-revoked active sessions for user."""
    init_auth_db_if_needed()
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute(
                """
                SELECT id, created_at, last_used_at, expires_at, user_agent, ip_address
                FROM user_sessions
                WHERE user_id = %s AND revoked_at IS NULL AND expires_at > %s
                ORDER BY last_used_at DESC
                """,
                (user_id, now),
            )
        else:
            cur.execute(
                """
                SELECT id, created_at, last_used_at, expires_at, user_agent, ip_address
                FROM user_sessions
                WHERE user_id = ? AND revoked_at IS NULL AND expires_at > ?
                ORDER BY last_used_at DESC
                """,
                (user_id, now.isoformat()),
            )
        rows = cur.fetchall()

    return [
        {
            "id": str(r["id"]),
            "created_at": str(r["created_at"]),
            "last_used_at": str(r["last_used_at"]),
            "expires_at": str(r["expires_at"]),
            "user_agent": r["user_agent"] or "Desktop Client",
            "ip_address": r["ip_address"] or "Unknown",
        }
        for r in rows
    ]


def revoke_user_session_by_id(user_id: str, session_id: str) -> bool:
    """Revoke a specific session owned by user."""
    init_auth_db_if_needed()
    now = datetime.datetime.now(datetime.timezone.utc)

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        if admin_api._is_pg():
            cur.execute(
                "UPDATE user_sessions SET revoked_at = %s WHERE id = %s AND user_id = %s",
                (now, session_id, user_id),
            )
        else:
            cur.execute(
                "UPDATE user_sessions SET revoked_at = ? WHERE id = ? AND user_id = ?",
                (now.isoformat(), session_id, user_id),
            )
        conn.commit()
    return True
