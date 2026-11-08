"""Comprehensive automated unit tests for Larpiano authentication service."""

import os
import sys
import unittest
from pathlib import Path

# Add remapped_catalog to sys.path
CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
sys.path.insert(0, str(CATALOG_DIR))

import admin_api
import auth_service
from email_service import MockEmailDeliveryService, set_email_service_for_testing


class AuthServiceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Force SQLite for test isolation
        if "DATABASE_URL" in os.environ:
            del os.environ["DATABASE_URL"]
        admin_api.DEV_DB_PATH = Path(__file__).resolve().parent / "test_auth_dev.db"
        if admin_api.DEV_DB_PATH.exists():
            admin_api.DEV_DB_PATH.unlink()

    def setUp(self):
        self.mock_email = MockEmailDeliveryService()
        set_email_service_for_testing(self.mock_email)
        auth_service.init_auth_db_if_needed()

    def tearDown(self):
        auth_service.rate_limiter._records.clear()

    @classmethod
    def tearDownClass(cls):
        if admin_api.DEV_DB_PATH.exists():
            try:
                admin_api.DEV_DB_PATH.unlink()
            except Exception:
                pass

    def test_password_hashing_and_strength(self):
        """Verify Argon2id hashing and password validation rules."""
        valid, msg = auth_service.validate_password_strength("short")
        self.assertFalse(valid)

        valid, msg = auth_service.validate_password_strength("allletterswithoutdigit")
        self.assertFalse(valid)

        valid, msg = auth_service.validate_password_strength("1234567890")
        self.assertFalse(valid)

        valid, msg = auth_service.validate_password_strength("SecurePass123!")
        self.assertTrue(valid)

        # Hash and verify
        hashed = auth_service.hash_password("SecurePass123!")
        self.assertTrue(hashed.startswith("$argon2id$"))

        is_valid, _ = auth_service.verify_and_maybe_rehash(hashed, "SecurePass123!")
        self.assertTrue(is_valid)

        is_valid, _ = auth_service.verify_and_maybe_rehash(hashed, "WrongPass123!")
        self.assertFalse(is_valid)

    def test_legacy_hash_upgrade(self):
        """Ensure legacy salt:sha256 hashes are verified and upgraded to Argon2id."""
        import hashlib
        salt = "testsalt"
        pwd = "LegacyPassword99"
        legacy_hash = f"{salt}:{hashlib.sha256((salt + pwd).encode()).hexdigest()}"

        is_valid, new_hash = auth_service.verify_and_maybe_rehash(legacy_hash, pwd)
        self.assertTrue(is_valid)
        self.assertIsNotNone(new_hash)
        self.assertTrue(new_hash.startswith("$argon2id$"))

    def test_registration_and_email_verification_flow(self):
        """Test complete registration, 6-digit code delivery, and account verification."""
        email = "testplayer@example.com"
        success, msg, data = auth_service.register_user(email, "Secret987!", "TestPianist")
        self.assertTrue(success, msg)
        self.assertIsNotNone(data)

        # Check mock email was sent
        sent = self.mock_email.sent_emails
        self.assertEqual(len(sent), 1)
        self.assertEqual(sent[0]["to"], email)
        code = sent[0]["code"]
        self.assertEqual(len(code), 6)
        self.assertTrue(code.isdigit())

        # Verify with incorrect code first
        v_ok, v_err, _ = auth_service.verify_email_code(email, "000000")
        self.assertFalse(v_ok)
        self.assertIn("Incorrect verification code", v_err)

        # Verify with correct code
        v_ok, v_msg, v_data = auth_service.verify_email_code(email, code)
        self.assertTrue(v_ok, v_msg)
        self.assertTrue(v_data["user"]["email_verified"])
        self.assertIn("tokens", v_data)
        self.assertIn("access_token", v_data["tokens"])
        self.assertIn("refresh_token", v_data["tokens"])

        # Check token is valid JWT
        payload = auth_service.decode_access_token(v_data["tokens"]["access_token"])
        self.assertIsNotNone(payload)
        self.assertEqual(payload["email"], email)
        self.assertTrue(payload["email_verified"])

    def test_resend_verification_cooldown(self):
        """Verify 60-second cooldown is enforced on code resend."""
        email = "cooldown@example.com"
        auth_service.register_user(email, "Secret987!", "CooldownUser")
        self.assertEqual(len(self.mock_email.sent_emails), 1)

        # Immediate resend should trigger cooldown
        ok, msg, wait = auth_service.resend_verification_code(email)
        self.assertFalse(ok)
        self.assertIn("Please wait", msg)
        self.assertGreater(wait, 0)

    def test_login_and_token_rotation(self):
        """Test sign-in and subsequent refresh token rotation."""
        email = "loginuser@example.com"
        pwd = "ComplexPassw0rd!"
        auth_service.register_user(email, pwd, "LoginUser")
        self.assertFalse(auth_service.login_user(email, pwd)[0])
        verified, message, _ = auth_service.verify_email_code(email, self.mock_email.sent_emails[-1]["code"])
        self.assertTrue(verified, message)

        # Login
        ok, msg, data = auth_service.login_user(email, pwd)
        self.assertTrue(ok, msg)
        refresh_token = data["tokens"]["refresh_token"]

        # Rotate token
        rot_ok, rot_msg, rot_data = auth_service.rotate_refresh_token(refresh_token)
        self.assertTrue(rot_ok, rot_msg)
        new_refresh = rot_data["refresh_token"]
        self.assertNotEqual(refresh_token, new_refresh)

        # Replay Attack Detection: Attempting to use the old refresh token should trigger replay defense!
        replay_ok, replay_msg, _ = auth_service.rotate_refresh_token(refresh_token)
        self.assertFalse(replay_ok)
        self.assertIn("Compromised session detected", replay_msg)

        # All sessions for the user should now be invalidated
        self.assertIsNone(auth_service.decode_access_token(data["tokens"]["access_token"]))
        old_replay_ok, _, _ = auth_service.rotate_refresh_token(new_refresh)
        self.assertFalse(old_replay_ok)

    def test_password_recovery_flow(self):
        """Test forgot password request, code verification, password update, and session invalidation."""
        email = "recoverme@example.com"
        old_pwd = "OldPassword123!"
        new_pwd = "NewPassword456!"
        auth_service.register_user(email, old_pwd, "RecoveryUser")
        verified, message, _ = auth_service.verify_email_code(email, self.mock_email.sent_emails[-1]["code"])
        self.assertTrue(verified, message)

        # Login to create an active session
        l_ok, _, l_data = auth_service.login_user(email, old_pwd)
        self.assertTrue(l_ok)
        session_token = l_data["tokens"]["refresh_token"]

        # Request reset
        r_ok, r_msg = auth_service.request_password_reset(email)
        self.assertTrue(r_ok)

        # Retrieve code from mock email
        reset_emails = [e for e in self.mock_email.sent_emails if e["type"] == "password_reset"]
        self.assertEqual(len(reset_emails), 1)
        reset_code = reset_emails[0]["code"]

        # Reset password
        reset_ok, reset_msg = auth_service.reset_password_with_code(email, reset_code, new_pwd)
        self.assertTrue(reset_ok, reset_msg)

        # Verify old session was invalidated
        rot_ok, _, _ = auth_service.rotate_refresh_token(session_token)
        self.assertFalse(rot_ok)

        # Verify login with new password works
        new_l_ok, _, _ = auth_service.login_user(email, new_pwd)
        self.assertTrue(new_l_ok)

        # Old password fails
        old_l_ok, _, _ = auth_service.login_user(email, old_pwd)
        self.assertFalse(old_l_ok)


if __name__ == "__main__":
    unittest.main()
