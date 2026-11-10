"""Unit tests for admin_api.py endpoints, presence heartbeats, and audit trails."""

import os
import sqlite3
from contextlib import contextmanager
import shutil
import tempfile
import unittest
import unittest.mock
from pathlib import Path

# Add remapped_catalog to sys.path
import sys
CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
if str(CATALOG_DIR) not in sys.path:
    sys.path.insert(0, str(CATALOG_DIR))

import admin_api
import storage


class AdminApiTests(unittest.TestCase):
    def setUp(self):
        self.env_patcher = unittest.mock.patch.dict(os.environ, {"INITIAL_ADMIN_EMAIL": "admin@example.test", "INITIAL_ADMIN_PASSWORD": "test-password-long-enough"})
        self.env_patcher.start()
        self.temp_dir = tempfile.TemporaryDirectory()
        admin_api.DEV_DB_PATH = Path(self.temp_dir.name) / "test_admin.db"
        storage.LOCAL_ROOT = Path(self.temp_dir.name) / "storage"
        admin_api.init_db_if_needed()

    def tearDown(self):
        self.temp_dir.cleanup()
        self.env_patcher.stop()

    def test_database_initialization_and_admin_seed(self):
        # Admin user should be seeded
        users = admin_api.get_admin_users()
        self.assertGreaterEqual(users["total"], 1)
        admin = next(u for u in users["users"] if u["display_name"] == "Admin")
        self.assertEqual(admin["role"], "admin")

    def test_admin_created_users_start_with_mandatory_privacy_defaults(self):
        user = admin_api.create_admin_user({"email": "new-user@example.test", "display_name": "New user"}, None)
        with admin_api.get_db() as conn:
            row = conn.execute("SELECT telemetry_consent, presence_consent, playground_effects_enabled FROM user_preferences WHERE user_id = ?", (user["id"],)).fetchone()
            self.assertEqual(tuple(row), (1, 1, 0))

    def test_heartbeat_upsert_and_no_append_spam(self):
        sess_id = "test-session-uuid-1234"
        # First heartbeat
        res1 = admin_api.handle_heartbeat({
            "session_identifier": sess_id,
            "app_version": "v1.1.0-beta.1",
            "status": "playing"
        })
        self.assertEqual(res1["status"], "ok")
        self.assertEqual(res1["heartbeat_interval_sec"], 45)

        # Check sessions count
        sessions = admin_api.get_admin_sessions()
        matching = [s for s in sessions["sessions"] if s["session_identifier"] == sess_id]
        self.assertEqual(len(matching), 1)
        self.assertEqual(matching[0]["current_status"], "playing")

        # Second heartbeat should update the same session row
        res2 = admin_api.handle_heartbeat({
            "session_identifier": sess_id,
            "app_version": "v1.1.0-beta.1",
            "status": "idle"
        })
        self.assertEqual(res2["status"], "ok")

        sessions2 = admin_api.get_admin_sessions()
        matching2 = [s for s in sessions2["sessions"] if s["session_identifier"] == sess_id]
        self.assertEqual(len(matching2), 1)
        self.assertEqual(matching2[0]["current_status"], "idle")

    def test_session_end(self):
        sess_id = "test-session-end-5678"
        admin_api.handle_heartbeat({
            "session_identifier": sess_id,
            "app_version": "v1.1.0-beta.1",
            "status": "online"
        })
        
        end_res = admin_api.handle_session_end({"session_identifier": sess_id})
        self.assertEqual(end_res["status"], "ok")

        sessions = admin_api.get_admin_sessions()
        target = next(s for s in sessions["sessions"] if s["session_identifier"] == sess_id)
        self.assertEqual(target["current_status"], "offline")
        self.assertIsNotNone(target["ended_at"])
        self.assertEqual(target["is_online"], 0)

    def test_admin_overview_metrics(self):
        sess_id = "test-active-overview"
        admin_api.handle_heartbeat({
            "session_identifier": sess_id,
            "app_version": "v1.1.0-beta.1",
            "status": "playing"
        })

        overview = admin_api.get_admin_overview()
        self.assertIn("metrics", overview)
        self.assertGreaterEqual(overview["metrics"]["onlineNow"], 1)
        self.assertEqual(len(overview["live_activity_24h"]), 24)
        self.assertGreaterEqual(sum(overview["live_activity_24h"]), 1)
        self.assertGreaterEqual(len(overview["active_players"]), 1)

        # Test real analytics endpoint
        analytics = admin_api.get_admin_analytics("7d")
        self.assertIn("dau", analytics)
        self.assertIn("wau", analytics)
        self.assertGreaterEqual(analytics["dau"], 1)
        self.assertEqual(len(analytics["dailyUsers"]), 7)
        self.assertTrue(any(v["count"] >= 1 for v in analytics["versionDistribution"]))

    def test_announcements_lifecycle(self):
        # Create
        created = admin_api.create_announcement({
            "title": "Maintenance Tonight",
            "message": "We will update game hook offsets.",
            "audience": "all",
            "status": "published"
        }, admin_id="master-admin-000")
        self.assertEqual(created["title"], "Maintenance Tonight")

        # Desktop client queries active announcements
        app_ann = admin_api.get_app_announcements()
        self.assertTrue(any(a["id"] == created["id"] for a in app_ann))

        # Update
        update_res = admin_api.update_announcement(created["id"], {"status": "retracted"}, admin_id="master-admin-000")
        self.assertEqual(update_res["status"], "ok")

        # Now app client should not receive retracted announcement
        app_ann_after = admin_api.get_app_announcements()
        self.assertFalse(any(a["id"] == created["id"] for a in app_ann_after))

    def test_announcements_work_without_updated_at_column(self):
        conn = sqlite3.connect(":memory:")
        conn.row_factory = sqlite3.Row
        conn.execute("""CREATE TABLE announcements (
            id TEXT PRIMARY KEY, title TEXT NOT NULL, message TEXT NOT NULL,
            audience TEXT, status TEXT, published_at TEXT, expires_at TEXT,
            created_by TEXT, created_at TEXT)""")

        @contextmanager
        def in_memory_db():
            yield conn

        try:
            with unittest.mock.patch.object(admin_api, "get_db", in_memory_db), \
                 unittest.mock.patch.object(admin_api, "audit_log"):
                item = admin_api.create_announcement({"title": "Hi", "message": "Hello"}, None)
                admin_api.update_announcement(item["id"], {"status": "retracted"}, None)
                self.assertEqual(conn.execute("SELECT status FROM announcements").fetchone()[0], "retracted")
        finally:
            conn.close()

    def test_playground_trigger_and_dismiss(self):
        triggered = admin_api.trigger_playground_effect({
            "effect_type": "lavender_snow",
            "duration_seconds": 60
        }, admin_id="master-admin-000")
        self.assertEqual(triggered["effect_type"], "lavender_snow")

        # Client checks events
        events = admin_api.get_app_playground_events()
        self.assertTrue(any(e["id"] == triggered["id"] for e in events))

        # Client dismisses
        dismiss_res = admin_api.dismiss_playground_event(triggered["id"])
        self.assertEqual(dismiss_res["status"], "ok")

        # Check dismissed
        events_after = admin_api.get_app_playground_events()
        self.assertFalse(any(e["id"] == triggered["id"] for e in events_after))

    def test_export_admin_data_csv(self):
        res = admin_api.export_admin_data("users", admin_id="master-admin-000")
        self.assertTrue(res["key"].startswith("admin/exports/"))
        self.assertTrue(res["key"].endswith(".csv"))
        self.assertGreater(res["size"], 0)

        # Audit log should show the export action
        audit = admin_api.get_audit_logs()
        self.assertTrue(any(l["action"] == "EXPORT_GENERATED" for l in audit["logs"]))

    def test_provision_admin_flow_and_login_validation(self):
        """Verify secure admin provisioning and authentication enforcement."""
        import provision_admin

        # Test password strength check
        valid, msg = provision_admin.validate_admin_password("short")
        self.assertFalse(valid)
        valid, msg = provision_admin.validate_admin_password("WeakPassWithoutSymbol123")
        self.assertFalse(valid)
        valid, msg = provision_admin.validate_admin_password("UltraSecureP@ssw0rd!2026")
        self.assertTrue(valid)

        # Provision new administrator
        admin_email = "superadmin@larpiano.test"
        admin_pw = "EnterpriseP@ssw0rd!2026"
        success = provision_admin.provision_admin(admin_email, admin_pw, "Super Admin")
        self.assertTrue(success)

        # Admin can successfully log in
        login_res = admin_api.admin_login({"username": admin_email, "password": admin_pw})
        self.assertIn("token", login_res)
        self.assertEqual(login_res["user"]["role"], "admin")

        # Non-admin user cannot log into admin endpoints
        user = admin_api.create_admin_user({"email": "regular@larpiano.test", "display_name": "Regular Player"}, None)
        with self.assertRaises(PermissionError):
            admin_api.admin_login({"username": "regular@larpiano.test", "password": "AnyPassword123!"})

    def test_smtp_diagnostics_safe_reporting(self):
        """Verify check_smtp_connectivity safely diagnoses without leaking credentials."""
        import email_service

        # Safe diagnostic on unconfigured environment
        diag = email_service.check_smtp_connectivity({"host": ""})
        self.assertFalse(diag["configured"])
        self.assertEqual(diag["status"], "unconfigured")

        # Safe diagnostic on mock connection failure using mocked SMTP to avoid network delays
        with unittest.mock.patch.object(email_service.smtplib, "SMTP", side_effect=ConnectionRefusedError("Simulated connection refused")):
            diag_mock = email_service.check_smtp_connectivity({
                "host": "smtp.mock.test",
                "port": 587,
                "username": "supersecretuser@example.com",
                "password": "supersecretpassword123!",
                "use_tls": True,
            })
        self.assertTrue(diag_mock["configured"])
        self.assertFalse(diag_mock["connected"])
        # Ensure password is NEVER leaked in error or result
        self.assertNotIn("supersecretpassword123!", str(diag_mock))
        self.assertEqual(diag_mock["username_hint"], "sup...com")

    def test_admin_releases_decoupled_from_github_failures(self):
        """Verify Admin Releases endpoints are completely decoupled from external GitHub downtime.
        Tests 403, 404, 429, 500, and timeout conditions."""
        import urllib.error
        import admin_api

        # 1. Test GitHub 403 Forbidden (Account Suspended)
        err_403 = urllib.error.HTTPError("https://api.github.com/...", 403, "Forbidden", {}, None)
        with unittest.mock.patch.object(admin_api.urllib.request, "urlopen", side_effect=err_403):
            admin_api._RELEASES_CACHE = {"timestamp": 0, "data": []}
            releases = admin_api.get_releases_cached()
            self.assertEqual(len(releases), 5)
            latest = releases[0]
            self.assertEqual(latest["tag_name"], "v1.1.0")
            self.assertTrue(latest["release_metadata_available"])
            self.assertFalse(latest["external_download_host_available"])
            self.assertIn("403", latest["external_download_host_status"])
            self.assertTrue(latest["local_storage_available"])
            self.assertEqual(latest["download_url"], "/api/releases/latest/download")

        # 2. Test GitHub 404 Not Found
        err_404 = urllib.error.HTTPError("https://api.github.com/...", 404, "Not Found", {}, None)
        with unittest.mock.patch.object(admin_api.urllib.request, "urlopen", side_effect=err_404):
            admin_api._RELEASES_CACHE = {"timestamp": 0, "data": []}
            releases = admin_api.get_releases_cached()
            self.assertEqual(len(releases), 5)
            self.assertFalse(releases[0]["external_download_host_available"])
            self.assertIn("404", releases[0]["external_download_host_status"])

        # 3. Test GitHub 429 Rate Limit Exceeded
        err_429 = urllib.error.HTTPError("https://api.github.com/...", 429, "Too Many Requests", {}, None)
        with unittest.mock.patch.object(admin_api.urllib.request, "urlopen", side_effect=err_429):
            admin_api._RELEASES_CACHE = {"timestamp": 0, "data": []}
            releases = admin_api.get_releases_cached()
            self.assertEqual(len(releases), 5)
            self.assertFalse(releases[0]["external_download_host_available"])
            self.assertIn("429", releases[0]["external_download_host_status"])

        # 4. Test GitHub 500 Internal Server Error
        err_500 = urllib.error.HTTPError("https://api.github.com/...", 500, "Server Error", {}, None)
        with unittest.mock.patch.object(admin_api.urllib.request, "urlopen", side_effect=err_500):
            admin_api._RELEASES_CACHE = {"timestamp": 0, "data": []}
            releases = admin_api.get_releases_cached()
            self.assertEqual(len(releases), 5)
            self.assertFalse(releases[0]["external_download_host_available"])
            self.assertIn("500", releases[0]["external_download_host_status"])

        # 5. Test Network Timeout
        with unittest.mock.patch.object(admin_api.urllib.request, "urlopen", side_effect=TimeoutError("Timed out")):
            admin_api._RELEASES_CACHE = {"timestamp": 0, "data": []}
            releases = admin_api.get_releases_cached()
            self.assertEqual(len(releases), 5)
            self.assertFalse(releases[0]["external_download_host_available"])
            self.assertIn("timed out", releases[0]["external_download_host_status"].lower())


if __name__ == "__main__":
    unittest.main()
