# Admin schema review — local inspection

A read-only information_schema query on the live Portways database found three application tables: private_catalog_songs, private_catalog_staging, and remapped_catalog_songs. No admin, account, or schema_migrations tables were present. No live database changes were made.

`001_admin_and_presence_schema.up.sql` proposes users, app sessions, preferences, announcements, daily analytics, audit logs, playground events, and migration tracking. It uses `CREATE TABLE IF NOT EXISTS`; that does not reconcile incompatible existing tables. It does not name the private MIDI catalogue tables. `002_auth_schema.up.sql` adds a users timestamp and proposes authentication sessions, verification codes, password reset tokens, and authentication audit logs. Both define foreign keys and indexes in their SQL, but existing PostgreSQL constraints and index coverage have not been verified.

Important blockers:

- `001...down.sql` drops tables with `CASCADE`; it could destroy pre-existing tables or dependent objects. It must not be run against production.
- `migrate.py` calls `CREATE TABLE IF NOT EXISTS schema_migrations` even for status and dry-run operations, so those modes are not read-only against an unprepared database.
- Migration 001 does not define an announcements `updated_at` column. The local write path no longer requires it; isolated PostgreSQL verification is still required.
- The 120-second session window is queried but stale rows are not actively cleaned up. A retention policy has not been implemented.
- `daily_analytics` has no verified aggregation job; real historical metrics are unavailable.
- Actual schema overlaps, UUID compatibility, and query plans require read-only PostgreSQL inspection. Isolated migration and rollback tests have not been run; no PostgreSQL server was available in this environment.

No SQL migration changes are proposed in this pass. Preserve the MIDI catalogue and verify a restorable backup before planning any future schema changes.
