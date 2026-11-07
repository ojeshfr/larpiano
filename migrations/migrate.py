"""Safe, versioned database migration runner for Larpiano Control Center."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import sys

MIGRATIONS_DIR = Path(__file__).resolve().parent

def get_connection():
    db_url = os.environ.get("DATABASE_URL")
    if not db_url:
        raise RuntimeError("DATABASE_URL is not set.")
    import psycopg
    return psycopg.connect(db_url, connect_timeout=10)

def init_migration_table(conn):
    with conn.cursor() as cur:
        cur.execute("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version VARCHAR(50) PRIMARY KEY,
                applied_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL
            );
        """)
    conn.commit()

def get_applied_migrations(conn) -> set[str]:
    init_migration_table(conn)
    with conn.cursor() as cur:
        cur.execute("SELECT version FROM schema_migrations ORDER BY applied_at")
        return {row[0] for row in cur.fetchall()}

def run_up(conn, dry_run: bool = False):
    applied = get_applied_migrations(conn)
    up_files = sorted(MIGRATIONS_DIR.glob("*.up.sql"))

    if not up_files:
        print("No migration files found.")
        return

    for sql_file in up_files:
        version = sql_file.name.replace(".up.sql", "")
        if version in applied:
            print(f"[SKIP] {version} already applied.")
            continue

        print(f"[APPLYING] {version} from {sql_file.name}...")
        sql = sql_file.read_text(encoding="utf-8")
        if dry_run:
            print(f"  [DRY RUN] Would execute {len(sql)} characters of SQL.")
            continue

        with conn.cursor() as cur:
            cur.execute(sql)
            cur.execute("INSERT INTO schema_migrations (version) VALUES (%s) ON CONFLICT (version) DO NOTHING", (version,))
        conn.commit()
        print(f"[SUCCESS] {version} applied successfully.")

def run_down(conn, target_version: str | None = None, dry_run: bool = False):
    applied = get_applied_migrations(conn)
    down_files = sorted(MIGRATIONS_DIR.glob("*.down.sql"), reverse=True)

    for sql_file in down_files:
        version = sql_file.name.replace(".down.sql", "")
        if version not in applied:
            continue
        if target_version and version != target_version:
            continue

        print(f"[ROLLBACK] Reverting {version} using {sql_file.name}...")
        sql = sql_file.read_text(encoding="utf-8")
        if dry_run:
            print(f"  [DRY RUN] Would execute {len(sql)} characters of rollback SQL.")
            return

        with conn.cursor() as cur:
            cur.execute(sql)
            cur.execute("DELETE FROM schema_migrations WHERE version = %s", (version,))
        conn.commit()
        print(f"[SUCCESS] {version} rolled back.")
        return  # Roll back one step by default

def show_status(conn):
    applied = get_applied_migrations(conn)
    up_files = sorted(MIGRATIONS_DIR.glob("*.up.sql"))
    print("\n--- MIGRATION STATUS ---")
    for f in up_files:
        version = f.name.replace(".up.sql", "")
        status = "APPLIED" if version in applied else "PENDING"
        print(f"  [{status}] {version}")
    print("------------------------\n")

def main():
    parser = argparse.ArgumentParser(description="Larpiano Database Migration Tool")
    parser.add_argument("action", choices=["status", "up", "down"], help="Migration action")
    parser.add_argument("--dry-run", action="store_true", help="Print plan without modifying DB")
    parser.add_argument("--version", help="Specific migration version for rollback")
    args = parser.parse_args()

    try:
        with get_connection() as conn:
            if args.action == "status":
                show_status(conn)
            elif args.action == "up":
                run_up(conn, dry_run=args.dry_run)
            elif args.action == "down":
                run_down(conn, target_version=args.version, dry_run=args.dry_run)
    except Exception as exc:
        print(f"[ERROR] Migration failed: {exc}", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
