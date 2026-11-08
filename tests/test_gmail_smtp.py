"""SMTP configuration and delivery tests; no network or real credentials."""
import os
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
sys.path.insert(0, str(CATALOG_DIR))
import email_service


class GmailSmtpTests(unittest.TestCase):
    def test_verification_email_uses_gmail_starttls(self):
        sender = email_service.SmtpEmailDeliveryService(
            host="smtp.gmail.com", port=587, username="sender@example.test",
            password="test-app-password", from_email="sender@example.test", use_tls=True)
        with patch.object(email_service.smtplib, "SMTP") as smtp:
            connection = smtp.return_value.__enter__.return_value
            self.assertTrue(sender.send_verification_email("recipient@example.test", "Pianist", "123456"))
            smtp.assert_called_once_with("smtp.gmail.com", 587, timeout=15)
            connection.starttls.assert_called_once_with()
            connection.login.assert_called_once_with("sender@example.test", "test-app-password")
            self.assertEqual(connection.sendmail.call_args.args[:2], ("sender@example.test", ["recipient@example.test"]))

    def test_gmail_smtp_environment_configuration(self):
        env = {"EMAIL_PROVIDER": "smtp", "EMAIL_HOST": "smtp.gmail.com", "EMAIL_PORT": "587",
               "EMAIL_USER": "sender@example.test", "EMAIL_PASS": "test-app-password",
               "EMAIL_FROM": "sender@example.test", "EMAIL_USE_TLS": "true"}
        with patch.dict(os.environ, env, clear=True), patch.object(email_service, "_DEFAULT_SERVICE", None):
            service = email_service.get_email_service()
            self.assertEqual((service.host, service.port, service.username, service.from_email),
                             ("smtp.gmail.com", 587, "sender@example.test", "sender@example.test"))
            self.assertTrue(service.use_tls)

if __name__ == "__main__":
    unittest.main()
