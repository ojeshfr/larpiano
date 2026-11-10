import datetime
import logging
import os
import sqlite3
import uuid
from typing import Any, Optional

import admin_api

log = logging.getLogger("larpiano_playlists")


def _is_pg() -> bool:
    url = os.environ.get("DATABASE_URL", "")
    return url.startswith("postgres://") or url.startswith("postgresql://")


def _get_db():
    return admin_api.get_db()


def ensure_schema():
    """Ensure playlist tables exist in SQLite or PostgreSQL."""
    with _get_db() as conn:
        if _is_pg():
            with conn.cursor() as cur:
                cur.execute("""
                    CREATE TABLE IF NOT EXISTS user_playlists (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        user_id UUID NOT NULL,
                        name VARCHAR(200) NOT NULL,
                        is_public BOOLEAN NOT NULL DEFAULT FALSE,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                    CREATE INDEX IF NOT EXISTS idx_user_playlists_user ON user_playlists(user_id);

                    CREATE TABLE IF NOT EXISTS user_playlist_items (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        playlist_id UUID NOT NULL REFERENCES user_playlists(id) ON DELETE CASCADE,
                        song_sha256 VARCHAR(64) NOT NULL,
                        title VARCHAR(300) NOT NULL,
                        artist VARCHAR(300) NOT NULL,
                        duration_seconds DOUBLE PRECISION NOT NULL DEFAULT 0.0,
                        order_index INT NOT NULL DEFAULT 0,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
                    );
                    CREATE INDEX IF NOT EXISTS idx_user_playlist_items_playlist ON user_playlist_items(playlist_id, order_index);
                """)
                conn.commit()
        else:
            cur = conn.cursor()
            cur.executescript("""
                CREATE TABLE IF NOT EXISTS user_playlists (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    is_public INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_user_playlists_user ON user_playlists(user_id);

                CREATE TABLE IF NOT EXISTS user_playlist_items (
                    id TEXT PRIMARY KEY,
                    playlist_id TEXT NOT NULL REFERENCES user_playlists(id) ON DELETE CASCADE,
                    song_sha256 TEXT NOT NULL,
                    title TEXT NOT NULL,
                    artist TEXT NOT NULL,
                    duration_seconds REAL NOT NULL DEFAULT 0.0,
                    order_index INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                );
                CREATE INDEX IF NOT EXISTS idx_user_playlist_items_playlist ON user_playlist_items(playlist_id, order_index);
            """)
            conn.commit()


def list_playlists(user_id: str) -> list[dict[str, Any]]:
    ensure_schema()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("""
                SELECT p.id, p.user_id, p.name, p.is_public, p.created_at, p.updated_at,
                       COUNT(i.id) as item_count,
                       COALESCE(SUM(i.duration_seconds), 0) as total_duration
                FROM user_playlists p
                LEFT JOIN user_playlist_items i ON p.id = i.playlist_id
                WHERE p.user_id = %s
                GROUP BY p.id
                ORDER BY p.updated_at DESC, p.created_at DESC
            """, (user_id,))
            rows = cur.fetchall()
            return [dict(r) for r in rows]
        else:
            cur.execute("""
                SELECT p.id, p.user_id, p.name, p.is_public, p.created_at, p.updated_at,
                       COUNT(i.id) as item_count,
                       COALESCE(SUM(i.duration_seconds), 0) as total_duration
                FROM user_playlists p
                LEFT JOIN user_playlist_items i ON p.id = i.playlist_id
                WHERE p.user_id = ?
                GROUP BY p.id
                ORDER BY p.updated_at DESC, p.created_at DESC
            """, (str(user_id),))
            rows = cur.fetchall()
            return [dict(r) for r in rows]


def create_playlist(user_id: str, name: str, is_public: bool = False) -> dict[str, Any]:
    ensure_schema()
    name = (name or "").strip()
    if not name or len(name) > 200:
        raise ValueError("Playlist name must be between 1 and 200 characters.")
    p_id = str(uuid.uuid4())
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("""
                INSERT INTO user_playlists (id, user_id, name, is_public, created_at, updated_at)
                VALUES (%s, %s, %s, %s, NOW(), NOW())
                RETURNING id, user_id, name, is_public, created_at, updated_at
            """, (p_id, user_id, name, is_public))
            row = cur.fetchone()
            conn.commit()
            res = dict(row)
            res["item_count"] = 0
            res["total_duration"] = 0.0
            res["items"] = []
            return res
        else:
            cur.execute("""
                INSERT INTO user_playlists (id, user_id, name, is_public, created_at, updated_at)
                VALUES (?, ?, ?, ?, ?, ?)
            """, (p_id, str(user_id), name, 1 if is_public else 0, now_iso, now_iso))
            conn.commit()
            return {
                "id": p_id,
                "user_id": str(user_id),
                "name": name,
                "is_public": is_public,
                "created_at": now_iso,
                "updated_at": now_iso,
                "item_count": 0,
                "total_duration": 0.0,
                "items": []
            }


def get_playlist(user_id: str, playlist_id: str) -> Optional[dict[str, Any]]:
    ensure_schema()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("""
                SELECT id, user_id, name, is_public, created_at, updated_at
                FROM user_playlists
                WHERE id = %s AND (user_id = %s OR is_public = TRUE)
            """, (playlist_id, user_id))
            row = cur.fetchone()
            if not row:
                return None
            p = dict(row)
            cur.execute("""
                SELECT id, playlist_id, song_sha256, title, artist, duration_seconds, order_index, created_at
                FROM user_playlist_items
                WHERE playlist_id = %s
                ORDER BY order_index ASC, created_at ASC
            """, (playlist_id,))
            items = [dict(i) for i in cur.fetchall()]
            p["items"] = items
            p["item_count"] = len(items)
            p["total_duration"] = sum(i.get("duration_seconds", 0) for i in items)
            return p
        else:
            cur.execute("""
                SELECT id, user_id, name, is_public, created_at, updated_at
                FROM user_playlists
                WHERE id = ? AND (user_id = ? OR is_public = 1)
            """, (str(playlist_id), str(user_id)))
            row = cur.fetchone()
            if not row:
                return None
            p = dict(row)
            cur.execute("""
                SELECT id, playlist_id, song_sha256, title, artist, duration_seconds, order_index, created_at
                FROM user_playlist_items
                WHERE playlist_id = ?
                ORDER BY order_index ASC, created_at ASC
            """, (str(playlist_id),))
            items = [dict(i) for i in cur.fetchall()]
            p["items"] = items
            p["item_count"] = len(items)
            p["total_duration"] = sum(i.get("duration_seconds", 0) for i in items)
            return p


def update_playlist(user_id: str, playlist_id: str, name: str) -> Optional[dict[str, Any]]:
    ensure_schema()
    name = (name or "").strip()
    if not name or len(name) > 200:
        raise ValueError("Playlist name must be between 1 and 200 characters.")
    now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("""
                UPDATE user_playlists
                SET name = %s, updated_at = NOW()
                WHERE id = %s AND user_id = %s
                RETURNING id, user_id, name, is_public, created_at, updated_at
            """, (name, playlist_id, user_id))
            row = cur.fetchone()
            conn.commit()
            return dict(row) if row else None
        else:
            cur.execute("""
                UPDATE user_playlists
                SET name = ?, updated_at = ?
                WHERE id = ? AND user_id = ?
            """, (name, now_iso, str(playlist_id), str(user_id)))
            conn.commit()
            if cur.rowcount == 0:
                return None
            return get_playlist(user_id, playlist_id)


def delete_playlist(user_id: str, playlist_id: str) -> bool:
    ensure_schema()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("DELETE FROM user_playlists WHERE id = %s AND user_id = %s", (playlist_id, user_id))
            conn.commit()
            return cur.rowcount > 0
        else:
            cur.execute("DELETE FROM user_playlist_items WHERE playlist_id = ?", (str(playlist_id),))
            cur.execute("DELETE FROM user_playlists WHERE id = ? AND user_id = ?", (str(playlist_id), str(user_id)))
            conn.commit()
            return cur.rowcount > 0


def add_item(user_id: str, playlist_id: str, song_sha256: str, title: str, artist: str, duration_seconds: float = 0.0) -> Optional[dict[str, Any]]:
    ensure_schema()
    song_sha256 = (song_sha256 or "").strip().lower()
    title = (title or "").strip()
    artist = (artist or "").strip()
    if len(song_sha256) != 64 or not title:
        raise ValueError("Invalid song metadata for playlist entry.")

    with _get_db() as conn:
        cur = conn.cursor()
        # Verify ownership
        if _is_pg():
            cur.execute("SELECT id FROM user_playlists WHERE id = %s AND user_id = %s", (playlist_id, user_id))
            if not cur.fetchone():
                return None
            cur.execute("SELECT COALESCE(MAX(order_index), -1) + 1 FROM user_playlist_items WHERE playlist_id = %s", (playlist_id,))
            next_idx = cur.fetchone()[0]
            item_id = str(uuid.uuid4())
            cur.execute("""
                INSERT INTO user_playlist_items (id, playlist_id, song_sha256, title, artist, duration_seconds, order_index, created_at)
                VALUES (%s, %s, %s, %s, %s, %s, %s, NOW())
                RETURNING id, playlist_id, song_sha256, title, artist, duration_seconds, order_index, created_at
            """, (item_id, playlist_id, song_sha256, title, artist, float(duration_seconds), next_idx))
            cur.execute("UPDATE user_playlists SET updated_at = NOW() WHERE id = %s", (playlist_id,))
            row = cur.fetchone()
            conn.commit()
            return dict(row)
        else:
            cur.execute("SELECT id FROM user_playlists WHERE id = ? AND user_id = ?", (str(playlist_id), str(user_id)))
            if not cur.fetchone():
                return None
            cur.execute("SELECT COALESCE(MAX(order_index), -1) + 1 FROM user_playlist_items WHERE playlist_id = ?", (str(playlist_id),))
            next_idx = cur.fetchone()[0]
            item_id = str(uuid.uuid4())
            now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
            cur.execute("""
                INSERT INTO user_playlist_items (id, playlist_id, song_sha256, title, artist, duration_seconds, order_index, created_at)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """, (item_id, str(playlist_id), song_sha256, title, artist, float(duration_seconds), next_idx, now_iso))
            cur.execute("UPDATE user_playlists SET updated_at = ? WHERE id = ?", (now_iso, str(playlist_id)))
            conn.commit()
            return {
                "id": item_id,
                "playlist_id": str(playlist_id),
                "song_sha256": song_sha256,
                "title": title,
                "artist": artist,
                "duration_seconds": float(duration_seconds),
                "order_index": next_idx,
                "created_at": now_iso
            }


def remove_item(user_id: str, playlist_id: str, item_id: str) -> bool:
    ensure_schema()
    with _get_db() as conn:
        cur = conn.cursor()
        # Verify ownership
        if _is_pg():
            cur.execute("SELECT id FROM user_playlists WHERE id = %s AND user_id = %s", (playlist_id, user_id))
            if not cur.fetchone():
                return False
            cur.execute("DELETE FROM user_playlist_items WHERE id = %s AND playlist_id = %s", (item_id, playlist_id))
            deleted = cur.rowcount > 0
            if deleted:
                cur.execute("UPDATE user_playlists SET updated_at = NOW() WHERE id = %s", (playlist_id,))
            conn.commit()
            return deleted
        else:
            cur.execute("SELECT id FROM user_playlists WHERE id = ? AND user_id = ?", (str(playlist_id), str(user_id)))
            if not cur.fetchone():
                return False
            cur.execute("DELETE FROM user_playlist_items WHERE id = ? AND playlist_id = ?", (str(item_id), str(playlist_id)))
            deleted = cur.rowcount > 0
            if deleted:
                now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
                cur.execute("UPDATE user_playlists SET updated_at = ? WHERE id = ?", (now_iso, str(playlist_id)))
            conn.commit()
            return deleted


def reorder_items(user_id: str, playlist_id: str, item_ids: list[str]) -> bool:
    ensure_schema()
    with _get_db() as conn:
        cur = conn.cursor()
        if _is_pg():
            cur.execute("SELECT id FROM user_playlists WHERE id = %s AND user_id = %s", (playlist_id, user_id))
            if not cur.fetchone():
                return False
            for idx, iid in enumerate(item_ids):
                cur.execute("UPDATE user_playlist_items SET order_index = %s WHERE id = %s AND playlist_id = %s", (idx, iid, playlist_id))
            cur.execute("UPDATE user_playlists SET updated_at = NOW() WHERE id = %s", (playlist_id,))
            conn.commit()
            return True
        else:
            cur.execute("SELECT id FROM user_playlists WHERE id = ? AND user_id = ?", (str(playlist_id), str(user_id)))
            if not cur.fetchone():
                return False
            now_iso = datetime.datetime.now(datetime.timezone.utc).isoformat()
            for idx, iid in enumerate(item_ids):
                cur.execute("UPDATE user_playlist_items SET order_index = ? WHERE id = ? AND playlist_id = ?", (idx, str(iid), str(playlist_id)))
            cur.execute("UPDATE user_playlists SET updated_at = ? WHERE id = ?", (now_iso, str(playlist_id)))
            conn.commit()
            return True


def duplicate_playlist(user_id: str, playlist_id: str, new_name: Optional[str] = None) -> Optional[dict[str, Any]]:
    orig = get_playlist(user_id, playlist_id)
    if not orig:
        return None
    name = (new_name or f"{orig['name']} (Copy)").strip()
    new_p = create_playlist(user_id, name)
    for item in orig.get("items", []):
        add_item(user_id, new_p["id"], item["song_sha256"], item["title"], item["artist"], item.get("duration_seconds", 0.0))
    return get_playlist(user_id, new_p["id"])
