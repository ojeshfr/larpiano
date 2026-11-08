"""Integration tests for Larpiano Authentication and Account HTTP Endpoints."""

import io
import json
import os
import tempfile
import unittest
from pathlib import Path

import sys
CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
if str(CATALOG_DIR) not in sys.path:
    sys.path.insert(0, str(CATALOG_DIR))

import admin_api
import auth_service
from email_service import MockEmailDeliveryService, set_email_service_for_testing
import server


class AuthApiIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        admin_api.DEV_DB_PATH = Path(self.temp_dir.name) / "test_auth_api.db"
        self.mock_email = MockEmailDeliveryService()
        set_email_service_for_testing(self.mock_email)
        auth_service.init_auth_db_if_needed()
        auth_service.rate_limiter._records.clear()

    def tearDown(self):
        self.temp_dir.cleanup()

    def make_handler(self, method, path, headers=None, body=None):
        h = server.Handler.__new__(server.Handler)
        h.path = path
        h.command = method
        h.headers = headers or {}
        h.responses = []
        h.wfile = io.BytesIO()
        h.client_address = ("127.0.0.1", 54321)

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

    def get_response_json(self, handler):
        return json.loads(handler.wfile.getvalue().decode("utf-8"))

    def get_status(self, handler):
        for item in handler.responses:
            if item[0] == "status":
                return item[1]
        return None

    def test_full_auth_api_lifecycle(self):
        """Test Register -> Verify -> Login -> Refresh -> /me -> Logout via API handler."""
        email = "pianist@example.com"
        pwd = "Virtuoso2026!#"

        # 1. Register
        h_reg = self.make_handler("POST", "/api/auth/register", body={
            "email": email,
            "password": pwd,
            "display_name": "Frédéric",
        })
        served = h_reg.serve_api(method="POST")
        self.assertTrue(served)
        self.assertEqual(self.get_status(h_reg), 200)
        reg_res = self.get_response_json(h_reg)
        self.assertTrue(reg_res["success"])

        # Check mock email sent
        self.assertEqual(len(self.mock_email.sent_emails), 1)
        code = self.mock_email.sent_emails[0]["code"]

        # 2. Verify Email
        h_ver = self.make_handler("POST", "/api/auth/verify-email", body={
            "email": email,
            "code": code,
        })
        self.assertTrue(h_ver.serve_api(method="POST"))
        self.assertEqual(self.get_status(h_ver), 200)
        ver_res = self.get_response_json(h_ver)
        self.assertTrue(ver_res["success"])
        tokens = ver_res["data"]["tokens"]
        access_token = tokens["access_token"]
        refresh_token = tokens["refresh_token"]

        # 3. GET /api/auth/me with Bearer token
        h_me = self.make_handler("GET", "/api/auth/me", headers={
            "Authorization": f"Bearer {access_token}"
        })
        self.assertTrue(h_me.serve_api(method="GET"))
        self.assertEqual(self.get_status(h_me), 200)
        me_res = self.get_response_json(h_me)
        self.assertEqual(me_res["user"]["email"], email)
        self.assertEqual(me_res["user"]["display_name"], "Frédéric")
        self.assertTrue(me_res["user"]["email_verified"])

        # 4. POST /api/auth/refresh
        h_ref = self.make_handler("POST", "/api/auth/refresh", body={
            "refresh_token": refresh_token,
        })
        self.assertTrue(h_ref.serve_api(method="POST"))
        self.assertEqual(self.get_status(h_ref), 200)
        ref_res = self.get_response_json(h_ref)
        new_access = ref_res["data"]["access_token"]
        new_refresh = ref_res["data"]["refresh_token"]
        self.assertNotEqual(refresh_token, new_refresh)

        # 5. POST /api/auth/preferences
        h_pref = self.make_handler("POST", "/api/auth/preferences", headers={
            "Authorization": f"Bearer {new_access}"
        }, body={
            "presence_consent": True,
            "telemetry_consent": True,
        })
        self.assertTrue(h_pref.serve_api(method="POST"))
        self.assertEqual(self.get_status(h_pref), 200)

        # Check /api/auth/me reflects consent
        h_me2 = self.make_handler("GET", "/api/auth/me", headers={
            "Authorization": f"Bearer {new_access}"
        })
        h_me2.serve_api(method="GET")
        me2_res = self.get_response_json(h_me2)
        self.assertTrue(me2_res["user"]["presence_consent"])
        self.assertTrue(me2_res["user"]["telemetry_consent"])

        # 6. POST /api/auth/logout
        h_out = self.make_handler("POST", "/api/auth/logout", body={
            "refresh_token": new_refresh,
        })
        self.assertTrue(h_out.serve_api(method="POST"))
        self.assertEqual(self.get_status(h_out), 200)

        # Refresh with logged-out token should fail
        h_ref_fail = self.make_handler("POST", "/api/auth/refresh", body={
            "refresh_token": new_refresh,
        })
        self.assertTrue(h_ref_fail.serve_api(method="POST"))
        self.assertEqual(self.get_status(h_ref_fail), 401)


if __name__ == "__main__":
    unittest.main()
