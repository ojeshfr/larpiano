import json
import os
import sys
import unittest
import uuid
from unittest.mock import patch

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "remapped_catalog")))
import auth_service
import playlist_service
import server


class TestPlaylistsAndRegionalCatalog(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        playlist_service.ensure_schema()
        cls.user1_id = str(uuid.uuid4())
        cls.user2_id = str(uuid.uuid4())
        cls.token1 = auth_service.create_access_token({"id": cls.user1_id, "email": "user1@example.com", "display_name": "User One", "role": "user", "email_verified": True}, str(uuid.uuid4()))
        cls.token2 = auth_service.create_access_token({"id": cls.user2_id, "email": "user2@example.com", "display_name": "User Two", "role": "user", "email_verified": True}, str(uuid.uuid4()))

    def test_playlist_unauthorized(self):
        # Without auth token, should return 401
        class MockHandler:
            headers = {}
            def send_json(self, status, payload, head=False):
                self.status = status
                self.payload = payload
        handler = MockHandler()
        handler.get_auth_user = lambda: server.Handler.get_auth_user(handler)
        handler.path = "/api/playlists"
        result = server.Handler.serve_api(handler, method="GET")
        self.assertTrue(result)
        self.assertEqual(handler.status, 401)

    def test_playlist_lifecycle(self):
        # 1. Create playlist
        p = playlist_service.create_playlist(self.user1_id, "Chill Classics")
        self.assertEqual(p["name"], "Chill Classics")
        p_id = p["id"]

        # 2. Add items
        item1 = playlist_service.add_item(self.user1_id, p_id, "a" * 64, "Song A", "Artist 1", 120.0)
        self.assertIsNotNone(item1)
        self.assertEqual(item1["title"], "Song A")
        self.assertEqual(item1["order_index"], 0)

        item2 = playlist_service.add_item(self.user1_id, p_id, "b" * 64, "Song B", "Artist 2", 150.0)
        self.assertIsNotNone(item2)
        self.assertEqual(item2["title"], "Song B")
        self.assertEqual(item2["order_index"], 1)

        # 3. Get playlist
        fetched = playlist_service.get_playlist(self.user1_id, p_id)
        self.assertIsNotNone(fetched)
        self.assertEqual(len(fetched["items"]), 2)
        self.assertEqual(fetched["total_duration"], 270.0)

        # 4. User 2 cannot access User 1's private playlist
        user2_access = playlist_service.get_playlist(self.user2_id, p_id)
        self.assertIsNone(user2_access)

        # 5. Reorder items
        reordered_ok = playlist_service.reorder_items(self.user1_id, p_id, [item2["id"], item1["id"]])
        self.assertTrue(reordered_ok)
        fetched_reordered = playlist_service.get_playlist(self.user1_id, p_id)
        self.assertEqual(fetched_reordered["items"][0]["title"], "Song B")
        self.assertEqual(fetched_reordered["items"][1]["title"], "Song A")

        # 6. Duplicate playlist
        dup = playlist_service.duplicate_playlist(self.user1_id, p_id, "Chill Classics Copy")
        self.assertIsNotNone(dup)
        self.assertEqual(dup["name"], "Chill Classics Copy")
        self.assertEqual(len(dup["items"]), 2)

        # 7. Remove item
        remove_ok = playlist_service.remove_item(self.user1_id, p_id, item1["id"])
        self.assertTrue(remove_ok)
        fetched_after_rm = playlist_service.get_playlist(self.user1_id, p_id)
        self.assertEqual(len(fetched_after_rm["items"]), 1)

        # 8. Delete playlist
        del_ok = playlist_service.delete_playlist(self.user1_id, p_id)
        self.assertTrue(del_ok)
        self.assertIsNone(playlist_service.get_playlist(self.user1_id, p_id))

        # Cleanup dup
        playlist_service.delete_playlist(self.user1_id, dup["id"])

    def test_regional_catalog_filtering(self):
        mock_catalog = [
            ("1" * 64, {
                "title": "Mo Li Hua (Jasmine Flower)",
                "artist": "Traditional Chinese",
                "category": "Traditional",
                "origin_country": "China",
                "origin_region": "East Asia",
                "language": "Mandarin",
                "musical_style": "Folk",
                "culture_tags": "Jasmine, Jiangnan"
            }, b"MThd" + b"\x00" * 20),
            ("2" * 64, {
                "title": "Tum Hi Ho",
                "artist": "Arijit Singh / Mithoon",
                "category": "Pop",
                "origin_country": "India",
                "origin_region": "South Asia",
                "language": "Hindi",
                "musical_style": "Bollywood Romantic",
                "culture_tags": "Aashiqui 2"
            }, b"MThd" + b"\x00" * 20),
            ("3" * 64, {
                "title": "Cornfield Chase",
                "artist": "Hans Zimmer",
                "category": "Film & TV",
                "origin_country": "United States",
                "origin_region": "North America",
                "language": "Instrumental",
                "musical_style": "Soundtrack",
                "culture_tags": "Interstellar"
            }, b"MThd" + b"\x00" * 20),
        ]

        with patch.object(server, "static_catalog", return_value=mock_catalog):
            # 1. Filter by country China
            res_china = server.list_songs(country="China")
            self.assertEqual(len(res_china), 1)
            self.assertEqual(res_china[0]["title"], "Mo Li Hua (Jasmine Flower)")

            # 2. Filter by country India
            res_india = server.list_songs(country="India")
            self.assertEqual(len(res_india), 1)
            self.assertEqual(res_india[0]["title"], "Tum Hi Ho")

            # 3. Filter by language Hindi
            res_hindi = server.list_songs(language="Hindi")
            self.assertEqual(len(res_hindi), 1)
            self.assertEqual(res_hindi[0]["title"], "Tum Hi Ho")

            # 4. Search by keyword
            res_search = server.list_songs(search="Interstellar")
            self.assertEqual(len(res_search), 1)
            self.assertEqual(res_search[0]["title"], "Cornfield Chase")

            # 5. Combined filter
            res_combined = server.list_songs(country="India", language="Hindi")
            self.assertEqual(len(res_combined), 1)

            res_combined_none = server.list_songs(country="India", language="Mandarin")
            self.assertEqual(len(res_combined_none), 0)


if __name__ == "__main__":
    unittest.main()
