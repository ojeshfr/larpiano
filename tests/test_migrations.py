"""Automated verification tests for database migrations."""

import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
MIGRATIONS_DIR = ROOT / "migrations"

class MigrationTests(unittest.TestCase):
    def setUp(self):
        self.up_file = MIGRATIONS_DIR / "001_admin_and_presence_schema.up.sql"
        self.down_file = MIGRATIONS_DIR / "001_admin_and_presence_schema.down.sql"
        self.assertTrue(self.up_file.is_file(), "Migration up file missing")
        self.assertTrue(self.down_file.is_file(), "Migration down file missing")
        self.up_sql = self.up_file.read_text(encoding="utf-8")
        self.down_sql = self.down_file.read_text(encoding="utf-8")

    def test_transactional_safety(self):
        """Ensure all migrations are wrapped in explicit BEGIN and COMMIT blocks."""
        self.assertRegex(self.up_sql, r"\bBEGIN;", "Up migration must include BEGIN;")
        self.assertRegex(self.up_sql, r"\bCOMMIT;", "Up migration must include COMMIT;")
        self.assertRegex(self.down_sql, r"\bBEGIN;", "Down migration must include BEGIN;")
        self.assertRegex(self.down_sql, r"\bCOMMIT;", "Down migration must include COMMIT;")

    def test_required_tables_present(self):
        """Verify all 7 proposed entities exist with IF NOT EXISTS."""
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
            self.assertRegex(self.up_sql, pattern, f"Table {tbl} must be created with IF NOT EXISTS")

    def test_rollback_completeness(self):
        """Verify every non-metadata table created in up.sql is dropped in down.sql."""
        created_tables = re.findall(r"CREATE TABLE IF NOT EXISTS (\w+)", self.up_sql)
        for tbl in created_tables:
            if tbl == "schema_migrations":
                continue
            drop_pattern = rf"DROP TABLE IF EXISTS {tbl}\b"
            self.assertRegex(self.down_sql, drop_pattern, f"Down migration must drop {tbl}")

    def test_indexes_present(self):
        """Verify indexes on critical lookup columns."""
        indexes = [
            "idx_users_email",
            "idx_app_sessions_last_seen",
            "idx_announcements_status_dates",
            "idx_admin_audit_logs_created",
            "idx_playground_events_active",
        ]
        for idx in indexes:
            self.assertIn(idx, self.up_sql, f"Index {idx} must be defined in up.sql")

    def test_foreign_keys_and_constraints(self):
        """Verify references and data integrity constraints."""
        self.assertIn("REFERENCES users(id)", self.up_sql)
        self.assertIn("ON DELETE CASCADE", self.up_sql)
        self.assertIn("ON DELETE SET NULL", self.up_sql)

if __name__ == "__main__":
    unittest.main()
