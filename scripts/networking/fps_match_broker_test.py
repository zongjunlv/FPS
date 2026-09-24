#!/usr/bin/env python3
"""Self-hosted control-plane regression tests; never launches Unity."""

import argparse
from http.server import ThreadingHTTPServer
import json
import pathlib
import sys
import tempfile
import threading
import urllib.error
import urllib.request
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import fps_control_backup
import fps_control_store
import fps_match_broker as broker


class BackendTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = pathlib.Path(self.tmp.name)
        self.database = root / "data" / "accounts.sqlite3"
        self.store = fps_control_store.ControlStore(self.database)
        # Some tests reopen the database; close the current connection, not
        # only the one that existed when setUp registered cleanup.
        self.addCleanup(lambda: self.store.close())
        self.manager_calls = []
        args = argparse.Namespace(
            database=self.database, state_root=root / "runtime",
            public_host="127.0.0.1", game_port=17777,
            application_version="0.1.0", protocol_version="1",
            content_version="citynew-v1", ticket_lifetime=600,
            binary=root / "server", manager=root / "manager.py",
            tick_rate=60, idle_timeout=120, startup_timeout=2)
        args.state_root.mkdir()

        def fake_manager(match_id, seed, roster):
            self.manager_calls.append((match_id, seed, roster))
            return {"matchId": match_id, "statePath": str(root / "fake-state.json")}

        self.broker = broker.MatchBroker(
            args, store=self.store, manager_runner=fake_manager,
            secret="x" * 32)

    def _account(self, username="User123"):
        return self.store.register(username, "secret123")

    def _room(self, account, seed=18018):
        return self.store.create_room(account["accountId"], {
            "displayName": "测试房间", "mapId": "CityNew",
            "seed": seed, "appearanceId": "operative-alpha"})

    def _allocation_payload(self, room):
        return {
            "sessionId": room["id"], "seed": room["seed"],
            "players": [{"accountId": p["accountId"],
                         "appearanceId": p["appearanceId"]}
                        for p in room["players"]],
            "applicationVersion": "0.1.0", "protocolVersion": "1",
            "contentVersion": "citynew-v1",
        }

    def test_account_password_hash_tokens_logout_and_casefold_unique(self):
        account = self._account()
        self.assertEqual(self.store.authenticate(account["accessToken"])["accountId"],
                         account["accountId"])
        self.assertEqual(self.store.login("user123", "secret123")["accountId"],
                         account["accountId"])
        with self.assertRaises(fps_control_store.ConflictError):
            self._account("USER123")
        with self.assertRaises(PermissionError):
            self.store.login("User123", "wrongpass123")
        row = self.store.db.execute("SELECT password_hash,password_salt,password_iterations FROM accounts").fetchone()
        self.assertNotEqual(row["password_hash"], b"secret123")
        self.assertEqual(len(row["password_salt"]), 32)
        self.assertGreaterEqual(row["password_iterations"], 600_000)
        token_row = self.store.db.execute("SELECT token_hash FROM access_tokens LIMIT 1").fetchone()
        self.assertNotIn(account["accessToken"].encode(), token_row["token_hash"])
        self.store.logout(account["accessToken"])
        with self.assertRaises(PermissionError):
            self.store.authenticate(account["accessToken"])

    def test_password_and_unicode_username_match_client_rules(self):
        account = self._account("玩家.测试1")
        self.assertEqual(self.store.login("玩家.测试1", "secret123")["accountId"],
                         account["accountId"])
        with self.assertRaises(ValueError):
            self.store.register("Ab", "secret123")
        with self.assertRaises(ValueError):
            self.store.register("abc", "passwordonly")

    def test_room_capacity_roster_host_and_reconnect(self):
        host = self._account()
        guest = self._account("Guest123")
        third = self._account("Third123")
        room = self._room(host)
        joined = self.store.join_room(guest["accountId"], {
            "joinCode": room["joinCode"], "appearanceId": "operative-bravo"})
        self.assertEqual([p["accountId"] for p in joined["players"]],
                         [host["accountId"], guest["accountId"]])
        with self.assertRaises(fps_control_store.ConflictError):
            self.store.join_room(third["accountId"], {
                "sessionId": room["id"], "appearanceId": "operative-alpha"})
        again = self.store.join_room(guest["accountId"], {
            "sessionId": room["id"], "appearanceId": "operative-bravo"})
        self.assertEqual(len(again["players"]), 2)
        with self.assertRaises(PermissionError):
            self.store.get_room(room["id"], third["accountId"])
        self.store.leave_room(room["id"], host["accountId"])
        self.assertEqual(self.store.get_room(room["id"])["hostId"], guest["accountId"])

    def test_room_snapshot_persists_across_restart(self):
        account = self._account()
        room = self._room(account)
        self.store.close()
        self.store = fps_control_store.ControlStore(self.database)
        self.broker.store = self.store
        self.assertEqual(self.store.get_room(room["id"])["joinCode"], room["joinCode"])
        self.assertEqual(self.store.authenticate(account["accessToken"])["accountId"],
                         account["accountId"])

    def test_relogin_can_recover_current_room_and_leave_to_create_new(self):
        account = self._account()
        first = self._room(account)
        self.store.close()
        self.store = fps_control_store.ControlStore(self.database)
        self.broker.store = self.store
        relogin = self.store.login("User123", "secret123")
        current = self.store.current_room(relogin["accountId"])
        self.assertEqual(current["id"], first["id"])
        self.store.leave_room(first["id"], relogin["accountId"])
        self.assertIsNone(self.store.current_room(relogin["accountId"]))
        second = self._room(relogin)
        self.assertNotEqual(second["id"], first["id"])

    def test_allocate_requires_authoritative_ready_roster_and_seed(self):
        host = self._account()
        guest = self._account("Guest123")
        room = self._room(host)
        room = self.store.join_room(guest["accountId"], {
            "sessionId": room["id"], "appearanceId": "operative-bravo"})
        payload = self._allocation_payload(room)
        with self.assertRaises(PermissionError):
            self.broker.allocate(host["accountId"], payload)
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        self.store.update_player(room["id"], guest["accountId"], {"ready": True})
        bad = dict(payload)
        bad["players"] = payload["players"][:1]
        with self.assertRaises(PermissionError):
            self.broker.allocate(host["accountId"], bad)
        bad = dict(payload)
        bad["seed"] = 99
        with self.assertRaises(PermissionError):
            self.broker.allocate(host["accountId"], bad)
        with self.assertRaises(PermissionError):
            self.broker.allocate(guest["accountId"], payload)
        result = self.broker.allocate(host["accountId"], payload)
        self.assertEqual(result["room"]["phase"], "loading")
        self.assertEqual(result["room"]["serverAllocation"], result["connection"])
        self.assertEqual(len(result["room"]["loadEpoch"]), 32)
        self.assertEqual(len(self.manager_calls), 1)

    def test_phase_requires_loading_barrier_and_host(self):
        host = self._account()
        room = self._room(host)
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        result = self.broker.allocate(host["accountId"], self._allocation_payload(room))
        epoch = result["room"]["loadEpoch"]
        with self.assertRaises(fps_control_store.ConflictError):
            self.store.set_phase(room["id"], host["accountId"], {"phase": "battle"})
        with self.assertRaises(fps_control_store.ConflictError):
            self.store.update_player(room["id"], host["accountId"], {"readyEpoch": "wrong"})
        self.store.update_player(room["id"], host["accountId"], {"readyEpoch": epoch})
        battle = self.store.set_phase(room["id"], host["accountId"], {"phase": "battle"})
        self.assertEqual(battle["phase"], "battle")
        cancelled = self.store.set_phase(room["id"], host["accountId"], {
            "phase": "cancelled", "loadFailure": "测试取消"})
        self.assertEqual(cancelled["loadFailure"], "测试取消")
        reset = self.store.set_phase(room["id"], host["accountId"], {"phase": "lobby"})
        self.assertFalse(reset["serverAllocation"])
        self.assertFalse(reset["players"][0]["ready"])

    def test_restarting_same_room_uses_fresh_match_id(self):
        host = self._account()
        room = self._room(host)
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        first = self.broker.allocate(host["accountId"],
                                     self._allocation_payload(room))
        self.broker.change_phase(host["accountId"], room["id"],
                                 {"phase": "lobby"})
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        second = self.broker.allocate(host["accountId"],
                                      self._allocation_payload(room))
        self.assertNotEqual(first["connection"]["matchId"],
                            second["connection"]["matchId"])
        self.assertEqual(len(self.manager_calls), 2)

    def test_missing_server_after_restart_recovers_room_without_losing_members(self):
        host = self._account()
        guest = self._account("Guest123")
        room = self._room(host)
        room = self.store.join_room(guest["accountId"], {
            "sessionId": room["id"], "appearanceId": "operative-bravo"})
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        self.store.update_player(room["id"], guest["accountId"], {"ready": True})
        allocated = self.broker.allocate(host["accountId"],
                                         self._allocation_payload(room))
        self.assertEqual(allocated["room"]["phase"], "loading")
        restarted = broker.MatchBroker(self.broker.args, store=self.store,
                                       manager_runner=self.broker.manager_runner,
                                       secret="x" * 32)
        recovered = self.store.get_room(room["id"])
        self.assertEqual(recovered["phase"], "lobby")
        self.assertEqual(len(recovered["players"]), 2)
        self.assertTrue(all(not player["ready"] for player in recovered["players"]))
        self.assertIsNone(recovered["serverAllocation"])
        self.assertEqual(restarted.store.current_room(host["accountId"])["id"],
                         room["id"])

    def test_guest_joins_allocated_match_with_own_ticket(self):
        host = self._account()
        guest = self._account("Guest123")
        room = self._room(host)
        room = self.store.join_room(guest["accountId"], {
            "sessionId": room["id"], "appearanceId": "operative-bravo"})
        self.store.update_player(room["id"], host["accountId"], {"ready": True})
        self.store.update_player(room["id"], guest["accountId"], {"ready": True})
        with mock.patch.object(self.broker, "_match_ready", return_value=True):
            allocated = self.broker.allocate(
                host["accountId"], self._allocation_payload(room))
            joined = self.broker.join(guest["accountId"], {
                "sessionId": room["id"],
                "matchId": allocated["connection"]["matchId"],
                "applicationVersion": "0.1.0", "protocolVersion": "1",
                "contentVersion": "citynew-v1"})
        self.assertEqual(joined["connection"], allocated["connection"])
        self.assertNotEqual(joined["connectionTicket"],
                            allocated["connectionTicket"])
        with self.assertRaises(PermissionError):
            self.broker.release(guest["accountId"], {
                "sessionId": room["id"],
                "matchId": allocated["connection"]["matchId"]})
        released = self.broker.release(host["accountId"], {
            "sessionId": room["id"],
            "matchId": allocated["connection"]["matchId"]})
        self.assertTrue(released["released"])

    def test_online_backup_and_integrity(self):
        account = self._account()
        room = self._room(account)
        backup = pathlib.Path(self.tmp.name) / "backup.sqlite3"
        fps_control_backup.backup(self.database, backup)
        self.assertEqual(fps_control_backup.verify(backup),
                         fps_control_store.SCHEMA_VERSION)
        restored = fps_control_store.ControlStore(backup)
        try:
            self.assertEqual(restored.get_room(room["id"])["joinCode"], room["joinCode"])
            self.assertEqual(restored.login("User123", "secret123")["accountId"],
                             account["accountId"])
        finally:
            restored.close()

    def test_rate_limit_and_token_expiry(self):
        account = self._account()
        self.store.db.execute("UPDATE access_tokens SET expires_at=0")
        with self.assertRaises(PermissionError):
            self.store.authenticate(account["accessToken"])
        for _ in range(5):
            self.broker.rate_limit.check("test-key", 5)
        with self.assertRaises(broker.RateLimited):
            self.broker.rate_limit.check("test-key", 5)

    def test_room_list_does_not_expose_private_match_details(self):
        host = self._account()
        guest = self._account("Guest123")
        room = self._room(host)
        public = self.store.list_rooms()[0]
        self.assertEqual(public["id"], room["id"])
        for private in ("players", "joinCode", "serverAllocation", "hostId"):
            self.assertNotIn(private, public)
        self.store.join_room(guest["accountId"], {
            "sessionId": room["id"], "appearanceId": "operative-bravo"})
        self.assertFalse(self.store.list_rooms())

    def test_http_api_register_login_rooms_and_logout(self):
        server = ThreadingHTTPServer(("127.0.0.1", 0), broker.BrokerHandler)
        server.broker = self.broker
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        base = f"http://127.0.0.1:{server.server_address[1]}"

        def request(path, body=None, token=None):
            headers = {"Content-Type": "application/json"}
            if token:
                headers["Authorization"] = "Bearer " + token
            data = json.dumps(body).encode() if body is not None else None
            req = urllib.request.Request(base + path, data=data, headers=headers)
            with urllib.request.urlopen(req, timeout=5) as response:
                return json.load(response)

        account = request("/v1/auth/register", {
            "username": "HttpUser1", "password": "secret123"})
        token = account["accessToken"]
        self.assertEqual(request("/v1/auth/me", token=token)["accountId"],
                         account["accountId"])
        room = request("/v1/rooms", {
            "displayName": "HTTP 房间", "mapId": "CityNew",
            "seed": 1, "appearanceId": "operative-alpha"}, token)
        listed = request("/v1/rooms", token=token)["rooms"][0]
        self.assertEqual(listed["id"], room["id"])
        self.assertEqual(listed["playerCount"], 1)
        self.assertNotIn("serverAllocation", listed)
        self.assertNotIn("players", listed)
        self.assertNotIn("joinCode", listed)
        self.assertTrue(request("/v1/rooms/" + room["id"], token=token)["players"])
        self.assertEqual(request("/v1/rooms/current", token=token)["id"], room["id"])
        request("/v1/rooms/" + room["id"] + "/leave", {}, token)
        empty = urllib.request.Request(base + "/v1/rooms/current", headers={
            "Authorization": "Bearer " + token})
        with urllib.request.urlopen(empty, timeout=5) as response:
            self.assertEqual(response.status, 204)
        request("/v1/auth/logout", {}, token)
        with self.assertRaises(urllib.error.HTTPError) as context:
            request("/v1/auth/me", token=token)
        self.assertEqual(context.exception.code, 403)
        context.exception.close()


if __name__ == "__main__":
    unittest.main()
