"""Unit tests for storage.py prefix security, MIME checks, and SHA-256 integrity."""

import os
import shutil
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

# Add remapped_catalog to sys.path
import sys
CATALOG_DIR = Path(__file__).resolve().parent.parent / "remapped_catalog"
if str(CATALOG_DIR) not in sys.path:
    sys.path.insert(0, str(CATALOG_DIR))

import storage


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        storage.LOCAL_ROOT = Path(self.temp_dir.name).resolve()

    def tearDown(self):
        self.temp_dir.cleanup()

    def test_portways_s3_environment_variables(self):
        env = {"S3_ENDPOINT": "https://storage.example.test", "S3_BUCKET": "test-bucket",
               "S3_ACCESS_KEY_ID": "test-access", "S3_SECRET_ACCESS_KEY": "test-secret", "S3_REGION": "test-region"}
        with patch.dict(os.environ, env, clear=True):
            config = storage.get_s3_config()
        self.assertEqual(config["access_key"], "test-access")
        self.assertEqual(config["secret_key"], "test-secret")
        self.assertEqual(config["bucket"], "test-bucket")
        self.assertEqual(config["region"], "test-region")

    def test_sanitize_filename(self):
        self.assertEqual(storage.sanitize_filename("banner.png"), "banner.png")
        self.assertEqual(storage.sanitize_filename("../../etc/passwd"), "passwd")
        self.assertEqual(storage.sanitize_filename("a space & file.jpg"), "a_space___file.jpg")
        self.assertEqual(storage.sanitize_filename(""), "asset.bin")

    def test_prefix_restrictions(self):
        # Disallowed prefix
        with self.assertRaises(ValueError) as ctx:
            storage.put_object("user/private/", "test.txt", b"data", "text/plain")
        self.assertIn("not allowed", str(ctx.exception))

        with self.assertRaises(ValueError) as ctx:
            storage.put_object("songs/", "song.mid", b"MThd...", "audio/midi")
        self.assertIn("not allowed", str(ctx.exception))

    def test_mime_type_restrictions(self):
        # image/png allowed for announcements
        res = storage.put_object("admin/announcements/", "banner.png", b"\x89PNG\r\n\x1a\nfake", "image/png")
        self.assertEqual(res["prefix"], "admin/announcements/")
        self.assertTrue(res["key"].startswith("admin/announcements/"))

        # text/plain NOT allowed for announcements
        with self.assertRaises(ValueError) as ctx:
            storage.put_object("admin/announcements/", "notes.txt", b"hello", "text/plain")
        self.assertIn("Content-Type", str(ctx.exception))

    def test_size_limits(self):
        oversized = b"X" * (5 * 1024 * 1024 + 1)
        with self.assertRaises(ValueError) as ctx:
            storage.put_object("admin/announcements/", "huge.png", oversized, "image/png")
        self.assertIn("exceeds maximum limit", str(ctx.exception))

    def test_put_and_get_sha256_integrity(self):
        content = b"header1,header2\nval1,val2\n"
        res = storage.export_csv("analytics_test", content.decode("utf-8"))
        
        self.assertEqual(res["prefix"], "admin/exports/")
        self.assertEqual(res["content_type"], "text/csv")
        self.assertEqual(res["size"], len(content))

        data, content_type, digest = storage.get_object(res["key"])
        self.assertEqual(data, content)
        self.assertEqual(digest, res["sha256"])
        self.assertEqual(content_type, "text/csv")

    def test_get_invalid_prefix_denied(self):
        with self.assertRaises(PermissionError):
            storage.get_object("unauthorized/path/file.txt")

    def test_delete_object(self):
        res = storage.export_csv("to_delete", "col1\nval1")
        self.assertTrue(storage.delete_object(res["key"]))
        with self.assertRaises(FileNotFoundError):
            storage.get_object(res["key"])


if __name__ == "__main__":
    unittest.main()
