"""Secure administrator account provisioning tool for Larpiano.

Allows an operator to safely create or promote an administrator account
using Argon2id password hashing, without hardcoding passwords in code or logs.
Supports both PostgreSQL (production) and SQLite (local dev).
"""

from __future__ import annotations

import argparse
import getpass
import json
import logging
import os
import re
import sys
import uuid
from pathlib import Path

# Ensure remapped_catalog is on path
ROOT = Path(__file__).resolve().parent
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

import admin_api
from argon2 import PasswordHasher

logging.basicConfig(level=logging.INFO, format="%(levelname)s: %(message)s")
log = logging.getLogger("provision_admin")

EMAIL_REGEX = re.compile(r"^[a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+$")


def validate_admin_password(password: str) -> tuple[bool, str]:
    """Validate that admin password meets high-entropy enterprise requirements."""
    if not password or len(password) < 16:
        return False, "Administrator password must be at least 16 characters long."
    if len(password) > 128:
        return False, "Password cannot exceed 128 characters."
    if not any(c.isupper() for c in password):
        return False, "Password must include at least one uppercase letter."
    if not any(c.islower() for c in password):
        return False, "Password must include at least one lowercase letter."
    if not any(c.isdigit() for c in password):
        return False, "Password must include at least one digit."
    if not any(c in "!@#$%^&*()-_=+[]{}|;:,.<>?/~`" for c in password):
        return False, "Password must include at least one special symbol."
    return True, ""


def provision_admin(email: str, password: str, display_name: str = "Administrator") -> bool:
    clean_email = email.strip().lower()
    if not EMAIL_REGEX.match(clean_email):
        print(f"[ERROR] Invalid email address format: {email}", file=sys.stderr)
        return False

    valid, err_msg = validate_admin_password(password)
    if not valid:
        print(f"[ERROR] Password does not meet security requirements: {err_msg}", file=sys.stderr)
        return False

    # Initialize tables if necessary (e.g. SQLite dev)
    admin_api.init_db_if_needed()

    pwd_hash = PasswordHasher().hash(password)
    clean_name = display_name.strip() or "Administrator"

    with admin_api.get_db() as conn:
        cur = conn.cursor()
        marker = "%s" if admin_api._is_pg() else "?"

        # Check if user already exists
        cur.execute(f"SELECT id, role, display_name FROM users WHERE email = {marker}", (clean_email,))
        existing = cur.fetchone()

        if existing:
            user_id = str(existing["id"])
            cur.execute(
                f"""
                UPDATE users
                SET role = 'admin',
                    password_hash = {marker},
                    email_verified = {1 if not admin_api._is_pg() else 'TRUE'},
                    account_status = 'active',
                    display_name = {marker}
                WHERE id = {marker}
                """,
                (pwd_hash, clean_name, user_id),
            )
            admin_api.audit_log(user_id, "ADMIN_PROVISIONED_PROMOTED", "user", user_id, {"email": clean_email})
            conn.commit()
            print(f"[SUCCESS] Existing user {clean_email} (ID: {user_id}) promoted to Administrator.")
            return True
        else:
            new_id = str(uuid.uuid4())
            cur.execute(
                f"""
                INSERT INTO users (id, email, display_name, password_hash, email_verified, role, account_status)
                VALUES ({marker}, {marker}, {marker}, {marker}, {1 if not admin_api._is_pg() else 'TRUE'}, 'admin', 'active')
                """,
                (new_id, clean_email, clean_name, pwd_hash),
            )
            admin_api.audit_log(new_id, "ADMIN_PROVISIONED_CREATED", "user", new_id, {"email": clean_email})
            conn.commit()
            print(f"[SUCCESS] New Administrator created: {clean_email} (ID: {new_id}).")
            return True


def main():
    parser = argparse.ArgumentParser(description="Larpiano Administrator Account Provisioning")
    parser.add_argument("--email", required=True, help="Administrator email address")
    parser.add_argument("--name", default="Administrator", help="Display name for administrator")
    parser.add_argument("--password-env", help="Environment variable containing administrator password (to avoid prompt)")

    args = parser.parse_args()

    password = ""
    if args.password_env:
        password = os.environ.get(args.password_env, "")
        if not password:
            print(f"[ERROR] Environment variable '{args.password_env}' is unset or empty.", file=sys.stderr)
            sys.exit(1)
    else:
        password = getpass.getpass("Enter Administrator password (min 16 chars): ")
        confirm = getpass.getpass("Confirm Administrator password: ")
        if password != confirm:
            print("[ERROR] Passwords do not match.", file=sys.stderr)
            sys.exit(1)

    success = provision_admin(email=args.email, password=password, display_name=args.name)
    sys.exit(0 if success else 1)


if __name__ == "__main__":
    main()
