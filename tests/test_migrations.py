"""Automated verification tests for database migrations."""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MIGRATIONS_DIR = ROOT / "migrations"

class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.up_file_001 = MIGRATIONS_DIR / "001_admin_and_presence_schema.up.sql"
        self.down_file_001 = MIGRATIONS_DIR / "001_admin_and_presence_schema.down.sql"
        self.up_file_002 = MIGRATIONS_DIR / "002_auth_schema.up.sql"
        self.down_file_002 = MIGRATIONS_DIR / "002_auth_schema.down.sql"

        for f in (self.up_file_001, self.down_file_001, self.up_file_002, self.down_file_002):
            self.assertTrue(f.is_file(), f"Migration file missing: {f.name}")

        self.up_sql_001 = self.up_file_001.read_text(encoding="utf-8")
        self.down_sql_001 = self.down_file_001.read_text(encoding="utf-8")
        self.up_sql_002 = self.up_file_002.read_text(encoding="utf-8")
        self.down_sql_002 = self.down_file_002.read_text(encoding="utf-8")

    def test_transactional_safety(self):
        """Ensure all migrations are wrapped in explicit BEGIN and COMMIT blocks."""
        for sql in (self.up_sql_001, self.down_sql_001, self.up_sql_002, self.down_sql_002):
            self.assertRegex(sql, r"\bBEGIN;", "Migration must include BEGIN;")
            self.assertRegex(sql, r"\bCOMMIT;", "Migration must include COMMIT;")

    def test_required_tables_present_001(self):
        """Verify all 7 proposed entities exist with IF NOT EXISTS in migration 001."""
        required = [
            "users",
            "app_sessions",
            "user_preferences",
            "announcements",
            "daily_analytics",
            "admin_audit_logs",
            "playground_events",
            "schema_migrations",
        ]
        for tbl in required:
            pattern = rf"CREATE TABLE IF NOT EXISTS {tbl}\b"
            self.assertRegex(self.up_sql_001, pattern, f"Table {tbl} must be created with IF NOT EXISTS")

    def test_required_tables_present_002(self):
        """Verify auth tables exist with IF NOT EXISTS in migration 002."""
        required = [
            "user_sessions",
            "email_verification_codes",
            "password_reset_tokens",
            "auth_audit_logs",
        ]
        for tbl in required:
            pattern = rf"CREATE TABLE IF NOT EXISTS {tbl}\b"
            self.assertRegex(self.up_sql_002, pattern, f"Table {tbl} must be created with IF NOT EXISTS in 002")

    def test_rollback_completeness(self):
        """Verify every non-metadata table created in up.sql is dropped in down.sql."""
        for up_sql, down_sql in ((self.up_sql_001, self.down_sql_001), (self.up_sql_002, self.down_sql_002)):
            created_tables = re.findall(r"CREATE TABLE IF NOT EXISTS (\w+)", up_sql)
            for tbl in created_tables:
                if tbl == "schema_migrations":
                    continue
                drop_pattern = rf"DROP TABLE IF EXISTS {tbl}\b"
                self.assertRegex(down_sql, drop_pattern, f"Down migration must drop {tbl}")

    def test_indexes_present_001(self):
        """Verify indexes on critical lookup columns in 001."""
        indexes = [
            "idx_users_email",
            "idx_app_sessions_last_seen",
            "idx_announcements_status_dates",
            "idx_admin_audit_logs_created",
            "idx_playground_events_active",
        ]
        for idx in indexes:
            self.assertIn(idx, self.up_sql_001, f"Index {idx} must be defined in up.sql 001")

    def test_indexes_present_002(self):
        """Verify indexes on critical lookup columns in 002."""
        indexes = [
            "idx_user_sessions_user_id",
            "idx_user_sessions_expires",
            "idx_user_sessions_hash",
            "idx_email_ver_user_id",
            "idx_pwd_reset_user_id",
            "idx_pwd_reset_hash",
            "idx_auth_audit_user_id",
            "idx_auth_audit_created",
            "idx_auth_audit_action",
        ]
        for idx in indexes:
            self.assertIn(idx, self.up_sql_002, f"Index {idx} must be defined in up.sql 002")

    def test_foreign_keys_and_constraints(self):
        """Verify references and data integrity constraints."""
        self.assertIn("REFERENCES users(id)", self.up_sql_001)
        self.assertIn("REFERENCES users(id)", self.up_sql_002)
        self.assertIn("ON DELETE CASCADE", self.up_sql_002)

if __name__ == "__main__":
    unittest.main()
