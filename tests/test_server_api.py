"""Integration tests for remapped_catalog HTTP Server with Admin & App Presence APIs."""

import io
import datetime
import uuid
import email
import re
import json
import os
import shutil
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
from urllib.parse import urlsplit

import sys
CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
if str(CATALOG_DIR) not in sys.path:
    sys.path.insert(0, str(CATALOG_DIR))

import admin_api
import server
import storage


class ServerApiIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.old_admin_token = admin_api.ADMIN_TOKEN
        admin_api.ADMIN_TOKEN = "test-only-operator-token"
        self.temp_dir = tempfile.TemporaryDirectory()
        admin_api.DEV_DB_PATH = Path(self.temp_dir.name) / "test_server.db"
        storage.LOCAL_ROOT = Path(self.temp_dir.name) / "storage"
        admin_api.init_db_if_needed()

    def tearDown(self):
        self.temp_dir.cleanup()
        admin_api.ADMIN_TOKEN = self.old_admin_token

    def make_handler(self, method, path, headers=None, body=None):
        h = server.Handler.__new__(server.Handler)
        h.client_address = ("127.0.0.1", 12345)
        h.path = path
        h.command = method
        h.headers = headers or {}
        h.responses = []
        h.wfile = io.BytesIO()
        
        body_bytes = b""
        if body is not None:
            if isinstance(body, dict):
                body_bytes = json.dumps(body).encode("utf-8")
                h.headers["Content-Type"] = "application/json"
            elif isinstance(body, bytes):
                body_bytes = body
            h.headers["Content-Length"] = str(len(body_bytes))
        
        h.rfile = io.BytesIO(body_bytes)
        h.send_response = lambda code: h.responses.append(("status", code))
        h.send_header = lambda k, v: h.responses.append((k, v))
        h.end_headers = lambda: None
        return h

    def test_postgres_values_are_json_serializable(self):
        handler = self.make_handler("GET", "/api/admin/users")
        identifier = uuid.uuid4()
        now = datetime.datetime(2026, 1, 2, tzinfo=datetime.timezone.utc)
        handler.send_json(200, {"id": identifier, "created_at": now})
        self.assertEqual(json.loads(handler.wfile.getvalue()), {"id": str(identifier), "created_at": now.isoformat()})

    def test_app_heartbeat_api(self):
        h = self.make_handler("POST", "/api/app/sessions/heartbeat", body={
            "session_identifier": "desktop-client-integration-1",
            "app_version": "v1.1.0-beta.1",
            "status": "playing"
        })
        self.assertTrue(h.serve_api(method="POST"))
        self.assertIn(("status", 401), h.responses)

    def test_presence_requires_consent_and_ignores_claimed_identity(self):
        with patch.object(server.Handler, "get_auth_user", return_value={"sub": "verified-user"}), \
             patch.object(server.auth_service, "get_current_user_profile", return_value={"presence_consent": False}):
            h = self.make_handler("POST", "/api/app/sessions/heartbeat", body={"session_identifier": "one", "user_id": "someone-else"})
            h.serve_api(method="POST")
            self.assertIn(("status", 403), h.responses)
        with patch.object(server.Handler, "get_auth_user", return_value={"sub": "verified-user"}), \
             patch.object(server.auth_service, "get_current_user_profile", return_value={"presence_consent": True}), \
             patch.object(admin_api, "handle_heartbeat", return_value={"status": "ok"}) as heartbeat:
            h = self.make_handler("POST", "/api/app/sessions/heartbeat", body={"session_identifier": "one", "user_id": "someone-else"})
            h.serve_api(method="POST")
            self.assertIn(("status", 200), h.responses)
            self.assertEqual(heartbeat.call_args.args[0]["user_id"], "verified-user")

    def test_registration_verification_through_mocked_smtp(self):
        import email_service
        sender = email_service.SmtpEmailDeliveryService(
            "smtp-relay.brevo.com", port=587, username="test-user", password="test-secret",
            from_email="sender@example.test", use_tls=True)
        with patch.object(server.auth_service, "get_email_service", return_value=sender), \
             patch.object(email_service.smtplib, "SMTP") as smtp:
            register = self.make_handler("POST", "/api/auth/register", body={
                "email": "new@example.test", "password": "StrongPass123!", "display_name": "Pianist"})
            register.serve_api(method="POST")
            self.assertIn(("status", 200), register.responses)
            session = smtp.return_value.__enter__.return_value
            session.starttls.assert_called_once()
            message = email.message_from_string(session.sendmail.call_args.args[2])
            code_match = re.search(r"\b[0-9]{6}\b", str(message["Subject"]))
            self.assertIsNotNone(code_match)
            verify = self.make_handler("POST", "/api/auth/verify-email", body={
                "email": "new@example.test", "code": code_match.group()})
            verify.serve_api(method="POST")
            self.assertIn(("status", 200), verify.responses)
            self.assertTrue(json.loads(verify.wfile.getvalue())["data"]["user"]["email_verified"])

    def test_admin_auth_and_overview(self):
        # 1. Without auth -> 401
        h_unauth = self.make_handler("GET", "/api/admin/overview")
        self.assertTrue(h_unauth.serve_api(method="GET"))
        self.assertIn(("status", 401), h_unauth.responses)

        # 2. Login with master admin token
        h_login = self.make_handler("POST", "/api/admin/auth/login", body={
            "token": "test-only-operator-token"
        })
        self.assertTrue(h_login.serve_api(method="POST"))
        self.assertIn(("status", 200), h_login.responses)
        login_res = json.loads(h_login.wfile.getvalue())
        self.assertIn("token", login_res)
        token = login_res["token"]

        # 3. Overview with Bearer token -> 200
        h_overview = self.make_handler("GET", "/api/admin/overview", headers={
            "Authorization": f"Bearer {token}"
        })
        self.assertTrue(h_overview.serve_api(method="GET"))
        self.assertIn(("status", 200), h_overview.responses)
        overview_res = json.loads(h_overview.wfile.getvalue())
        self.assertIn("metrics", overview_res)
        self.assertIn("live_activity_24h", overview_res)

    def test_admin_storage_download_requires_authorization(self):
        handler = self.make_handler("GET", "/api/admin/storage/download?key=exports/test.csv")
        self.assertTrue(handler.serve_api(method="GET"))
        self.assertIn(("status", 401), handler.responses)

    def test_preferences_require_account_authentication(self):
        handler = self.make_handler("POST", "/api/app/preferences", body={"user_id": "other-user", "presence_consent": True})
        self.assertTrue(handler.serve_api(method="POST"))
        self.assertIn(("status", 401), handler.responses)

    def test_unconfigured_operator_token_does_not_authenticate(self):
        admin_api.ADMIN_TOKEN = ""
        self.assertIsNone(admin_api.verify_admin_token("Bearer test-only-operator-token"))
        with self.assertRaises(PermissionError):
            admin_api.admin_login({"token": "test-only-operator-token"})

    def test_admin_announcements_crud(self):
        token = "test-only-operator-token"
        # Create
        h_create = self.make_handler("POST", "/api/admin/announcements", headers={"Authorization": f"Bearer {token}"}, body={
            "title": "Fleet Update v1.1",
            "message": "Testing announcement delivery",
            "status": "published"
        })
        self.assertTrue(h_create.serve_api(method="POST"))
        self.assertIn(("status", 201), h_create.responses)
        ann = json.loads(h_create.wfile.getvalue())

        # List via app API (public to client)
        h_app = self.make_handler("GET", "/api/app/announcements")
        self.assertTrue(h_app.serve_api(method="GET"))
        self.assertIn(("status", 200), h_app.responses)
        app_list = json.loads(h_app.wfile.getvalue())
        self.assertTrue(any(a["id"] == ann["id"] for a in app_list))


if __name__ == "__main__":
    unittest.main()
