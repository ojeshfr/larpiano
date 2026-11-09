"""Read-only public catalogue, admin control center API, and static website for Portways."""

from __future__ import annotations

import base64
import datetime
import decimal
import uuid
import hashlib
import hmac
import json
import logging
import math
import mimetypes
import os
from pathlib import Path
import re
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, unquote, urlsplit

import admin_api
import auth_service
import storage

ROOT = Path(__file__).resolve().parent
WEB_ROOT = Path(os.environ.get("WEB_ROOT", ROOT.parent / "website")).resolve()
ADMIN_ROOT = Path(os.environ.get("ADMIN_ROOT", ROOT.parent / "admin-preview")).resolve()
SEED_ROOT = ROOT / "seed"
MAX_MIDI_BYTES = 10 * 1024 * 1024
MAX_MANIFEST_BYTES = 1024 * 1024
MAX_JSON_BYTES = 1024 * 1024
MAX_ENTRIES = 1000
DIGEST = re.compile(r"^[0-9a-f]{64}$")
FIELDS = ("sha256", "title", "artist", "source", "license", "profile", "octave",
          "keptNotes", "excludedNotes", "notesAdjusted", "attribution",
          "durationSeconds", "modification")
REQUIRED = set(FIELDS[:9])
DB_LOCK = threading.Lock()
DB_SLOTS = threading.BoundedSemaphore(6)
initialized = False
seed_cache = None
log = logging.getLogger("remapped_catalog")


def load_seeds(seed_root: Path = SEED_ROOT) -> list[tuple[str, dict, bytes]]:
    manifest = seed_root / "manifest.json"
    if not manifest.is_file():
        raise ValueError("Seed manifest is missing")
    if manifest.stat().st_size > MAX_MANIFEST_BYTES:
        raise ValueError("Seed manifest exceeds the size limit")
    entries = json.loads(manifest.read_text(encoding="utf-8"))
    if not isinstance(entries, list) or len(entries) > MAX_ENTRIES:
        raise ValueError("Seed manifest must be a bounded array")
    result = []
    seen = set()
    for entry in entries:
        if not isinstance(entry, dict) or not REQUIRED.issubset(entry):
            raise ValueError("Invalid seed metadata")
        digest = entry["sha256"]
        if not isinstance(digest, str) or not DIGEST.fullmatch(digest) or digest in seen:
            raise ValueError("Invalid or duplicate seed checksum")
        seen.add(digest)
        for field in ("title", "artist", "source", "license", "profile"):
            if not isinstance(entry[field], str) or not 0 < len(entry[field]) <= 2048:
                raise ValueError("Invalid seed text field: " + field)
        source = urlsplit(entry["source"])
        if source.scheme != "https" or not source.hostname or source.username or source.password:
            raise ValueError("Seed source must be an HTTPS URL")
        for field in ("octave", "keptNotes", "excludedNotes"):
            if type(entry[field]) is not int or not (-2 <= entry[field] <= 2 if field == "octave" else 0 <= entry[field] <= 2_000_000):
                raise ValueError("Invalid seed number: " + field)
        if "notesAdjusted" in entry and (type(entry["notesAdjusted"]) is not int or not 0 <= entry["notesAdjusted"] <= 2_000_000):
            raise ValueError("Invalid seed note adjustment count")
        if "durationSeconds" in entry and (type(entry["durationSeconds"]) not in (int, float) or not math.isfinite(entry["durationSeconds"]) or not 0 <= entry["durationSeconds"] <= 86400):
            raise ValueError("Invalid seed duration")
        for field in ("attribution", "modification"):
            if field in entry and (not isinstance(entry[field], str) or len(entry[field]) > 4096):
                raise ValueError("Invalid seed text field: " + field)
        path = seed_root / (digest + ".mid")
        if not path.is_file() or path.stat().st_size > MAX_MIDI_BYTES:
            raise ValueError("Seed MIDI missing or exceeds size limit")
        midi = path.read_bytes()
        if len(midi) < 14 or midi[:4] != b"MThd" or midi[4:8] != b"\x00\x00\x00\x06":
            raise ValueError("Seed is not a standard MIDI file")
        if hashlib.sha256(midi).hexdigest() != digest:
            raise ValueError("Seed checksum mismatch")
        metadata = {field: entry[field] for field in FIELDS if field in entry}
        result.append((digest, metadata, midi))
    return result


def connect():
    import psycopg
    url = os.environ.get("DATABASE_URL")
    if not url:
        raise RuntimeError("Catalogue database is not configured")
    return psycopg.connect(url, connect_timeout=5)


def initialize():
    global initialized
    if initialized:
        return
    with DB_LOCK:
        if initialized:
            return
        from psycopg.types.json import Jsonb
        seeds = load_seeds()
        with DB_SLOTS, connect() as conn:
            with conn.cursor() as cur:
                cur.execute("""CREATE TABLE IF NOT EXISTS remapped_catalog_songs (
                    sha256 char(64) PRIMARY KEY,
                    metadata jsonb NOT NULL,
                    midi bytea NOT NULL CHECK (octet_length(midi) BETWEEN 14 AND 10485760)
                )""")
                for digest, metadata, midi in seeds:
                    cur.execute("""INSERT INTO remapped_catalog_songs (sha256,metadata,midi)
                        VALUES (%s,%s,%s) ON CONFLICT (sha256) DO UPDATE
                        SET metadata=EXCLUDED.metadata,midi=EXCLUDED.midi""",
                        (digest, Jsonb(metadata), midi))
        initialized = True


def static_catalog():
    global seed_cache
    if seed_cache is None:
        with DB_LOCK:
            if seed_cache is None:
                seed_cache = load_seeds()
    return seed_cache


def list_songs():
    if not os.environ.get("DATABASE_URL"):
        return [metadata for _, metadata, _ in static_catalog()]
    initialize()
    with DB_SLOTS, connect() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT metadata FROM remapped_catalog_songs ORDER BY metadata->>'title', sha256 LIMIT %s", (MAX_ENTRIES,))
            return [row[0] for row in cur.fetchall()]


def get_song(digest: str):
    if not os.environ.get("DATABASE_URL"):
        return next((midi for key, _, midi in static_catalog() if key == digest), None)
    initialize()
    with DB_SLOTS, connect() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT midi FROM remapped_catalog_songs WHERE sha256=%s", (digest,))
            row = cur.fetchone()
    if row is None:
        return None
    data = bytes(row[0])
    if len(data) > MAX_MIDI_BYTES or hashlib.sha256(data).hexdigest() != digest or data[:4] != b"MThd":
        raise ValueError("Stored MIDI failed integrity verification")
    return data


def private_authorized(header: str) -> bool:
    token = os.environ.get("PRIVATE_CATALOG_TOKEN", "")
    if len(token) < 24 or len(token) > 256 or not token.isascii():
        return False
    supplied = header.removeprefix("Bearer ") if header.startswith("Bearer ") else ""
    return len(supplied) <= 256 and supplied.isascii() and hmac.compare_digest(supplied, token)


def private_songs():
    with DB_SLOTS, connect() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT metadata FROM private_catalog_songs ORDER BY metadata->>'title', sha256 LIMIT %s", (MAX_ENTRIES,))
            return [row[0] for row in cur.fetchall()]


def private_song(digest: str):
    with DB_SLOTS, connect() as conn:
        with conn.cursor() as cur:
            cur.execute("SELECT midi FROM private_catalog_songs WHERE sha256=%s", (digest,))
            row = cur.fetchone()
    if row is None:
        return None
    data = bytes(row[0])
    if len(data) < 14 or len(data) > MAX_MIDI_BYTES or hashlib.sha256(data).hexdigest() != digest or data[:4] != b"MThd":
        raise ValueError("Stored MIDI failed integrity verification")
    return data


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        args[0].settimeout(15)
        super().__init__(*args, directory=str(WEB_ROOT), **kwargs)

    def log_message(self, fmt, *args):
        log.info("%s %s", self.address_string(), fmt % args)

    def read_json_body(self) -> dict:
        content_length = int(self.headers.get("Content-Length", 0))
        if content_length <= 0:
            return {}
        if content_length > MAX_JSON_BYTES:
            raise ValueError("Payload exceeds limit")
        raw = self.rfile.read(content_length)
        if not raw:
            return {}
        return json.loads(raw.decode("utf-8"))

    def send_json(self, status: int, payload, head: bool = False):
        def encode_database_value(value):
            if isinstance(value, (uuid.UUID, datetime.date, datetime.datetime)):
                return str(value) if isinstance(value, uuid.UUID) else value.isoformat()
            if isinstance(value, decimal.Decimal):
                return str(value)
            raise TypeError(f"Unsupported JSON value: {type(value).__name__}")

        content = json.dumps(payload, ensure_ascii=False, separators=(",", ":"), default=encode_database_value).encode("utf-8")
        if len(content) > 4 * 1024 * 1024:
            status, content = 503, b'{"error":"Catalogue temporarily unavailable"}'
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(content)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PATCH, DELETE, OPTIONS, HEAD")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With")
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.end_headers()
        if not head:
            self.wfile.write(content)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, PATCH, DELETE, OPTIONS, HEAD")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization, X-Requested-With")
        self.send_header("Access-Control-Max-Age", "86400")
        self.end_headers()

    def get_auth_user(self) -> Optional[dict]:
        auth = self.headers.get("Authorization", "")
        if auth.startswith("Bearer "):
            token = auth[7:].strip()
            return auth_service.decode_access_token(token)
        return None

    def get_client_ip(self) -> str:
        if self.client_address:
            return str(self.client_address[0])
        return "127.0.0.1"

    def serve_api(self, method="GET", head=False):
        parsed = urlsplit(self.path)
        path = parsed.path
        qs = parse_qs(parsed.query)

        # 0. Service Health Check
        if path == "/api/health" and method == "GET":
            self.send_json(200, {
                "status": "ok",
                "service": "larpiano-backend",
                "timestamp": datetime.datetime.now(datetime.timezone.utc).isoformat()
            }, head)
            return True

        # 1. Existing Private Catalogue
        if path == "/api/private/songs" or path.startswith("/api/private/songs/"):
            if method != "GET":
                self.send_json(405, {"error": "Method not allowed"}, head)
                return True
            if not private_authorized(self.headers.get("Authorization", "")):
                self.send_json(401, {"error": "Invalid access code"}, head)
                return True
            try:
                if path == "/api/private/songs":
                    self.send_json(200, private_songs(), head)
                    return True
                match = re.fullmatch(r"/api/private/songs/([0-9a-f]{64})/midi", path)
                if match is None:
                    self.send_json(404, {"error": "Not found"}, head)
                    return True
                data = private_song(match.group(1))
                if data is None:
                    self.send_json(404, {"error": "Not found"}, head)
                    return True
                self.send_response(200)
                self.send_header("Content-Type", "audio/midi")
                self.send_header("Content-Length", str(len(data)))
                self.send_header("Cache-Control", "private, no-store")
                self.send_header("X-Content-Type-Options", "nosniff")
                self.end_headers()
                if not head:
                    self.wfile.write(data)
            except Exception as exc:
                log.error("Private catalogue unavailable (%s)", type(exc).__name__)
                self.send_json(503, {"error": "Private catalogue unavailable"}, head)
            return True

        # 2. Existing Remapped Catalogue List
        if path == "/api/remapped/songs" and method == "GET":
            try:
                self.send_json(200, list_songs(), head)
            except Exception as exc:
                log.error("Catalogue unavailable (%s)", type(exc).__name__)
                self.send_json(503, {"error": "Catalogue temporarily unavailable"}, head)
            return True

        # 3. Existing Remapped Song Download
        prefix = "/api/remapped/songs/"
        if path.startswith(prefix) and method == "GET":
            match = re.fullmatch(r"([0-9a-f]{64})/midi", path[len(prefix):])
            if not match:
                self.send_json(404, {"error": "Not found"}, head)
                return True
            try:
                data = get_song(match.group(1))
                if data is None:
                    self.send_json(404, {"error": "Not found"}, head)
                    return True
                self.send_response(200)
                self.send_header("Content-Type", "audio/midi")
                self.send_header("Content-Length", str(len(data)))
                self.send_header("X-Content-Type-Options", "nosniff")
                self.send_header("Content-Disposition", 'attachment; filename="' + match.group(1) + '.mid"')
                self.send_header("Cache-Control", "public, max-age=86400, immutable")
                self.end_headers()
                if not head:
                    self.wfile.write(data)
            except Exception as exc:
                log.error("MIDI unavailable (%s)", type(exc).__name__)
                self.send_json(503, {"error": "Song temporarily unavailable"}, head)
            return True

        # --- App Client Presence & Telemetry Endpoints ---
        if path.startswith("/api/auth/"):
            ip = self.get_client_ip()
            ua = self.headers.get("User-Agent", "")

            if path == "/api/auth/register" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg, data = auth_service.register_user(
                        email=body.get("email", ""),
                        password=body.get("password", ""),
                        display_name=body.get("display_name", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    if ok:
                        self.send_json(200, {"success": True, "message": msg, "data": data}, head)
                    else:
                        self.send_json(400, {"success": False, "error": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/verify-email" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg, data = auth_service.verify_email_code(
                        email=body.get("email", ""),
                        code=body.get("code", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    if ok:
                        self.send_json(200, {"success": True, "message": msg, "data": data}, head)
                    else:
                        self.send_json(400, {"success": False, "error": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/resend-code" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg, wait = auth_service.resend_verification_code(
                        email=body.get("email", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    status_code = 200 if ok else (429 if (wait or 0) > 0 else 400)
                    self.send_json(status_code, {"success": ok, "message": msg, "cooldown": wait}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/login" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg, data = auth_service.login_user(
                        email=body.get("email", ""),
                        password=body.get("password", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    if ok:
                        self.send_json(200, {"success": True, "message": msg, "data": data}, head)
                    else:
                        self.send_json(401, {"success": False, "error": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/refresh" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg, data = auth_service.rotate_refresh_token(
                        raw_refresh_token=body.get("refresh_token", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    if ok:
                        self.send_json(200, {"success": True, "message": msg, "data": data}, head)
                    else:
                        self.send_json(401, {"success": False, "error": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/logout" and method == "POST":
                try:
                    body = self.read_json_body()
                    refresh_token = body.get("refresh_token", "") if isinstance(body, dict) else ""
                    auth_header = self.headers.get("Authorization", "")
                    access_token = auth_header.removeprefix("Bearer ").strip() if auth_header.startswith("Bearer ") else ""

                    auth_service.logout_session(
                        raw_refresh_token=refresh_token,
                        access_token=access_token,
                        ip_address=ip,
                        user_agent=ua
                    )
                    self.send_json(200, {"success": True, "message": "Signed out successfully."}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/logout-all" and method == "POST":
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                auth_service.logout_all_sessions(user["sub"], ip_address=ip, user_agent=ua)
                self.send_json(200, {"success": True, "message": "All sessions revoked."}, head)
                return True

            if path == "/api/auth/forgot-password" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg = auth_service.request_password_reset(body.get("email", ""), ip_address=ip, user_agent=ua)
                    self.send_json(200, {"success": True, "message": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/reset-password" and method == "POST":
                try:
                    body = self.read_json_body()
                    ok, msg = auth_service.reset_password_with_code(
                        email=body.get("email", ""),
                        code=body.get("code", ""),
                        new_password=body.get("new_password", ""),
                        ip_address=ip,
                        user_agent=ua,
                    )
                    if ok:
                        self.send_json(200, {"success": True, "message": msg}, head)
                    else:
                        self.send_json(400, {"success": False, "error": msg}, head)
                except Exception as exc:
                    self.send_json(400, {"success": False, "error": str(exc)}, head)
                return True

            if path == "/api/auth/me" and method == "GET":
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                profile = auth_service.get_current_user_profile(user["sub"])
                if not profile:
                    self.send_json(404, {"error": "User profile not found"}, head)
                    return True
                self.send_json(200, {"success": True, "user": profile}, head)
                return True

            if path == "/api/auth/preferences" and method == "POST":
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                body = self.read_json_body()
                auth_service.update_user_preferences(
                    user_id=user["sub"],
                    presence_consent=bool(body.get("presence_consent", False)),
                    telemetry_consent=bool(body.get("telemetry_consent", False)),
                )
                self.send_json(200, {"success": True}, head)
                return True

            if path == "/api/auth/sessions" and method == "GET":
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                sessions = auth_service.list_user_sessions(user["sub"])
                self.send_json(200, {"success": True, "sessions": sessions}, head)
                return True

            match_sess = re.fullmatch(r"/api/auth/sessions/([^/]+)", path)
            if match_sess and method == "DELETE":
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                auth_service.revoke_user_session_by_id(user["sub"], match_sess.group(1))
                self.send_json(200, {"success": True}, head)
                return True

            self.send_json(404, {"error": "Auth endpoint not found"}, head)
            return True
        if path in ("/api/app/sessions/heartbeat", "/api/app/sessions/end") and method == "POST":
            user = self.get_auth_user()
            if not user:
                self.send_json(401, {"error": "Unauthorized"}, head)
                return True
            if not auth_service.rate_limiter.is_allowed(f"presence:{user['sub']}", 30, 60):
                self.send_json(429, {"error": "Too many presence requests"}, head)
                return True
            try:
                profile = auth_service.get_current_user_profile(user["sub"])
                if path.endswith("heartbeat") and (not profile or not profile.get("presence_consent")):
                    self.send_json(403, {"error": "Presence consent required"}, head)
                    return True
                body = self.read_json_body()
                body["user_id"] = user["sub"]
                result = (admin_api.handle_heartbeat(body) if path.endswith("heartbeat")
                          else admin_api.handle_session_end(body))
                self.send_json(200, result, head)
            except PermissionError:
                self.send_json(403, {"error": "Session belongs to another user"}, head)
            except (TypeError, ValueError):
                self.send_json(400, {"error": "Invalid presence request"}, head)
            return True

        if path == "/api/app/announcements" and method == "GET":
            self.send_json(200, admin_api.get_app_announcements(), head)
            return True

        if path == "/api/app/playground/events" and method == "GET":
            user_id = qs.get("user_id", [None])[0]
            self.send_json(200, admin_api.get_app_playground_events(user_id), head)
            return True

        match_dismiss = re.fullmatch(r"/api/app/playground/events/([^/]+)/dismiss", path)
        if match_dismiss and method == "POST":
            self.send_json(200, admin_api.dismiss_playground_event(match_dismiss.group(1)), head)
            return True

        if path == "/api/app/preferences" and method == "POST":
            try:
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                body = self.read_json_body()
                user_id = user["sub"]
                self.send_json(200, admin_api.update_user_preferences(user_id, body), head)
            except Exception as e:
                self.send_json(400, {"error": str(e)}, head)
            return True

        if path == "/api/app/telemetry" and method == "POST":
            try:
                user = self.get_auth_user()
                if not user:
                    self.send_json(401, {"error": "Unauthorized"}, head)
                    return True
                profile = auth_service.get_current_user_profile(user["sub"])
                if not profile or not profile.get("telemetry_consent"):
                    self.send_json(403, {"error": "Telemetry consent required"}, head)
                    return True
                body = self.read_json_body()
                result = admin_api.record_telemetry_event(body, user["sub"])
                self.send_json(200, result, head)
            except ValueError as e:
                self.send_json(400, {"error": str(e)}, head)
            except Exception as e:
                self.send_json(500, {"error": "Failed to record telemetry"}, head)
            return True

        # --- Admin Endpoints ---
        if path == "/api/admin/auth/login" and method == "POST":
            try:
                if not auth_service.rate_limiter.is_allowed(f"admin-login:{self.get_client_ip()}", 10, 300):
                    self.send_json(429, {"error": "Too many login attempts"}, head)
                    return True
                body = self.read_json_body()
                self.send_json(200, admin_api.admin_login(body), head)
            except PermissionError as e:
                self.send_json(401, {"error": str(e)}, head)
            except Exception as e:
                self.send_json(400, {"error": str(e)}, head)
            return True

        # Object names are not credentials; downloads require administrator access.
        if path == "/api/admin/storage/download" and method == "GET":
            if not admin_api.verify_admin_token(self.headers.get("Authorization")):
                self.send_json(401, {"error": "Unauthorized"}, head)
                return True
            key = qs.get("key", [""])[0]
            if not key:
                self.send_json(400, {"error": "Missing key"}, head)
                return True
            try:
                data, content_type, digest = storage.get_object(key)
                self.send_response(200)
                self.send_header("Content-Type", content_type)
                self.send_header("Content-Length", str(len(data)))
                self.send_header("ETag", f'"{digest}"')
                self.send_header("Cache-Control", "private, max-age=3600")
                self.end_headers()
                if not head:
                    self.wfile.write(data)
            except FileNotFoundError:
                self.send_json(404, {"error": "Object not found"}, head)
            except PermissionError:
                self.send_json(403, {"error": "Access denied"}, head)
            except Exception as e:
                self.send_json(500, {"error": str(e)}, head)
            return True

        # Admin logout is idempotent and handles its own session cleanup
        if path == "/api/admin/auth/logout" and method == "POST":
            supplied = self.headers.get("Authorization", "").removeprefix("Bearer ").strip()
            if supplied:
                session_data = admin_api.ACTIVE_TOKENS.pop(supplied, None)
                if session_data and session_data.get("user"):
                    uid = session_data["user"].get("id")
                    admin_api.audit_log(uid, "ADMIN_LOGOUT", "user", uid or "operator")
            self.send_json(200, {"status": "ok", "success": True, "message": "Signed out successfully."}, head)
            return True

        # All other /api/admin/* endpoints require administrator authentication
        if path.startswith("/api/admin/"):
            admin_user = admin_api.verify_admin_token(self.headers.get("Authorization"))
            if not admin_user:
                self.send_json(401, {"error": "Unauthorized"}, head)
                return True

            if path == "/api/admin/auth/me" and method == "GET":
                self.send_json(200, {"user": admin_user}, head)
                return True

            if path == "/api/admin/overview" and method == "GET":
                self.send_json(200, admin_api.get_admin_overview(), head)
                return True

            if path == "/api/admin/users" and method == "GET":
                search = qs.get("search", [""])[0]
                status = qs.get("status", [""])[0]
                role = qs.get("role", [""])[0]
                limit = int(qs.get("limit", [50])[0])
                offset = int(qs.get("offset", [0])[0])
                self.send_json(200, admin_api.get_admin_users(search, status, role, limit, offset), head)
                return True

            if path == "/api/admin/users" and method == "POST":
                try:
                    body = self.read_json_body()
                    self.send_json(201, admin_api.create_admin_user(body, admin_user["id"]), head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            match_user = re.fullmatch(r"/api/admin/users/([^/]+)", path)
            if match_user and method == "PATCH":
                try:
                    body = self.read_json_body()
                    self.send_json(200, admin_api.update_admin_user(match_user.group(1), body, admin_user["id"]), head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            if path == "/api/admin/sessions" and method == "GET":
                status = qs.get("status", ["all"])[0]
                limit = int(qs.get("limit", [50])[0])
                offset = int(qs.get("offset", [0])[0])
                self.send_json(200, admin_api.get_admin_sessions(status, limit, offset), head)
                return True

            match_sess_term = re.fullmatch(r"/api/admin/sessions/([^/]+)/terminate", path)
            if match_sess_term and method == "POST":
                self.send_json(200, admin_api.terminate_session(match_sess_term.group(1), admin_user["id"]), head)
                return True

            if path == "/api/admin/analytics" and method == "GET":
                self.send_json(200, admin_api.get_admin_overview()["metrics"], head)
                return True

            if path == "/api/admin/announcements" and method == "GET":
                self.send_json(200, admin_api.get_admin_announcements(), head)
                return True

            if path == "/api/admin/announcements" and method == "POST":
                try:
                    body = self.read_json_body()
                    self.send_json(201, admin_api.create_announcement(body, admin_user["id"]), head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            match_ann = re.fullmatch(r"/api/admin/announcements/([^/]+)", path)
            if match_ann:
                if method == "PATCH":
                    try:
                        body = self.read_json_body()
                        self.send_json(200, admin_api.update_announcement(match_ann.group(1), body, admin_user["id"]), head)
                    except Exception as e:
                        self.send_json(400, {"error": str(e)}, head)
                    return True
                elif method == "DELETE":
                    self.send_json(200, admin_api.delete_announcement(match_ann.group(1), admin_user["id"]), head)
                    return True

            if path == "/api/admin/releases" and method == "GET":
                try:
                    self.send_json(200, admin_api.get_releases_cached(), head)
                except RuntimeError:
                    self.send_json(503, {"error": "Release metadata is unavailable"}, head)
                return True

            if path == "/api/admin/playground/trigger" and method == "POST":
                try:
                    body = self.read_json_body()
                    self.send_json(200, admin_api.trigger_playground_effect(body, admin_user["id"]), head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            if path == "/api/admin/audit" and method == "GET":
                limit = int(qs.get("limit", [50])[0])
                offset = int(qs.get("offset", [0])[0])
                self.send_json(200, admin_api.get_audit_logs(limit, offset), head)
                return True

            if path == "/api/admin/storage/upload" and method == "POST":
                try:
                    body = self.read_json_body()
                    prefix = body.get("prefix", "admin/announcements/")
                    filename = body.get("filename", "upload.png")
                    content_type = body.get("content_type", "image/png")
                    data_b64 = body.get("data_base64", "")
                    raw_data = base64.b64decode(data_b64)
                    res = storage.put_object(prefix, filename, raw_data, content_type)
                    admin_api.audit_log(admin_user["id"], "STORAGE_UPLOAD", "storage", res["key"], {"size": res["size"], "sha256": res["sha256"]})
                    self.send_json(201, res, head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            if path == "/api/admin/storage/export" and method == "POST":
                try:
                    body = self.read_json_body()
                    export_type = body.get("type", "users")
                    res = admin_api.export_admin_data(export_type, admin_user["id"])
                    self.send_json(200, res, head)
                except Exception as e:
                    self.send_json(400, {"error": str(e)}, head)
                return True

            self.send_json(404, {"error": "Admin endpoint not found"}, head)
            return True

        if path.startswith("/api/"):
            self.send_json(404, {"error": "Not found"}, head)
            return True

        return False

    def safe_static(self):
        path = unquote(urlsplit(self.path).path)
        if "\x00" in path or "\\" in path:
            return False
        parts = path.split("/")
        if any(part in ("..", ".") or part.startswith(".") for part in parts if part):
            return False
        if path == "/admin" or path.startswith("/admin/"):
            sub = path.removeprefix("/admin").lstrip("/")
            target = (ADMIN_ROOT / sub).resolve() if sub else (ADMIN_ROOT / "index.html")
            return target.is_relative_to(ADMIN_ROOT)
        return (WEB_ROOT / path.lstrip("/")).resolve().is_relative_to(WEB_ROOT)

    def serve_admin_static(self, head: bool = False):
        path = unquote(urlsplit(self.path).path)
        sub = path.removeprefix("/admin").lstrip("/")
        target = (ADMIN_ROOT / sub).resolve() if sub else (ADMIN_ROOT / "index.html")
        if not target.is_file():
            target = ADMIN_ROOT / "index.html"
        if not target.is_file():
            self.send_error(404)
            return

        content_type, _ = mimetypes.guess_type(str(target))
        if not content_type:
            content_type = "text/html" if target.suffix == ".html" else "application/octet-stream"
        if target.suffix == ".js":
            content_type = "application/javascript"
        elif target.suffix == ".css":
            content_type = "text/css"

        data = target.read_bytes()
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; font-src 'self' https://fonts.gstatic.com; img-src 'self' data:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'")
        self.send_header("Cache-Control", "no-cache" if target.suffix in (".html", ".js", ".css") else "max-age=86400")
        self.end_headers()
        if not head:
            self.wfile.write(data)

    def do_GET(self):
        if self.serve_api(method="GET"):
            return
        if not self.safe_static():
            self.send_error(404)
            return
        path = unquote(urlsplit(self.path).path)
        if path == "/admin" or path.startswith("/admin/"):
            self.serve_admin_static(head=False)
            return
        super().do_GET()

    def do_HEAD(self):
        if self.serve_api(method="GET", head=True):
            return
        if not self.safe_static():
            self.send_error(404)
            return
        path = unquote(urlsplit(self.path).path)
        if path == "/admin" or path.startswith("/admin/"):
            self.serve_admin_static(head=True)
            return
        super().do_HEAD()

    def do_POST(self):
        if self.serve_api(method="POST"):
            return
        self.send_json(404, {"error": "Not found"})

    def do_PATCH(self):
        if self.serve_api(method="PATCH"):
            return
        self.send_json(404, {"error": "Not found"})

    def do_DELETE(self):
        if self.serve_api(method="DELETE"):
            return
        self.send_json(404, {"error": "Not found"})

    def list_directory(self, path):
        self.send_error(404)
        return None


class BoundedServer(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.request_slots = threading.BoundedSemaphore(32)

    def process_request(self, request, client_address):
        if not self.request_slots.acquire(blocking=False):
            try:
                request.sendall(b"HTTP/1.0 503 Service Unavailable\r\nContent-Length: 0\r\n\r\n")
            except OSError:
                pass
            self.shutdown_request(request)
            return
        try:
            super().process_request(request, client_address)
        except BaseException:
            self.request_slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self.request_slots.release()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    port = int(os.environ.get("PORT", "80"))
    BoundedServer(("0.0.0.0", port), Handler).serve_forever()
