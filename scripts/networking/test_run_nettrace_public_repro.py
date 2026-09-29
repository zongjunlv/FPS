#!/usr/bin/env python3
"""Local-only tests for the public nettrace broker harness."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import pathlib
import stat
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import run_nettrace_public_repro as repro
import issue101_server_manager as server_manager


def settings(fingerprint: str = "0" * 64) -> repro.Settings:
    return repro.Settings("127.0.0.1", 18443, fingerprint,
                          "0.1.0", server_manager.BATTLE_PROTOCOL_VERSION,
                          "citynew-v1", 3)


class FakeBroker:
    def __init__(self) -> None:
        self.settings = settings()
        self.calls: list[str] = []
        self.passwords: list[str] = []
        self.accounts: list[str] = []
        self.ready: set[str] = set()
        self.connection = {
            "schemaVersion": "fps-remote-match-v1", "host": "203.0.113.7",
            "port": 17777, "matchId": "coop-local-test", "maximumPlayers": 2,
            **self.settings.compatibility,
        }
        self.ticket_number = 0

    def post(self, path: str, payload: dict[str, object],
             token: str | None = None) -> dict[str, object]:
        self.calls.append(path)
        if path == "/v1/auth/register":
            username, password = payload["username"], payload["password"]
            assert isinstance(username, str) and len(username) <= 20
            assert isinstance(password, str) and 8 <= len(password) <= 30
            assert any(char.isalpha() for char in password)
            assert any(char.isdigit() for char in password)
            self.passwords.append(password)
            account = f"acc-{len(self.accounts)}"
            self.accounts.append(account)
            return {"accountId": account, "accessToken": f"token-{account}"}
        assert token in {f"token-{account}" for account in self.accounts}
        if path == "/v1/rooms":
            return {"id": "room-local-test"}
        if path == "/v1/rooms/join":
            return {"id": "room-local-test"}
        if path.endswith("/player"):
            account = token.removeprefix("token-")
            self.ready.add(account)
            return {
                "id": "room-local-test", "seed": 123,
                "players": [
                    {"accountId": aid, "appearanceId": repro.APPEARANCE,
                     "ready": aid in self.ready}
                    for aid in self.accounts],
            }
        if path == "/v1/matches/allocate" or path == "/v1/matches/join":
            self.ticket_number += 1
            return {"connection": self.connection.copy(),
                    "connectionTicket": f"secret-ticket-{self.ticket_number}"}
        if path.endswith("/leave"):
            return {"left": True}
        if path == "/v1/auth/logout":
            return {"loggedOut": True}
        raise AssertionError(path)


class LocalHarnessTests(unittest.TestCase):
    def test_bundled_protocol_matches_current_verification_harness(self) -> None:
        loaded = repro.load_settings()
        self.assertEqual(loaded.protocol_version,
                         server_manager.BATTLE_PROTOCOL_VERSION)
        self.assertEqual(repro.VERSION[1], server_manager.BATTLE_PROTOCOL_VERSION)

    def test_settings_are_read_and_validated(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "settings.json"
            value = {
                "brokerUrl": "https://127.0.0.1:18443",
                "pinnedCertificateSha256": "A" * 64,
                "applicationVersion": "0.1.0",
                "protocolVersion": server_manager.BATTLE_PROTOCOL_VERSION,
                "contentVersion": "citynew-v1", "requestTimeoutSeconds": 4,
            }
            path.write_text(json.dumps(value), encoding="utf-8")
            loaded = repro.load_settings(path)
            self.assertEqual((loaded.host, loaded.port), ("127.0.0.1", 18443))
            self.assertEqual(loaded.fingerprint, "a" * 64)
            value["brokerUrl"] = "http://127.0.0.1:18443"
            path.write_text(json.dumps(value), encoding="utf-8")
            with self.assertRaises(repro.HarnessError):
                repro.load_settings(path)

    def test_pinned_connection_checks_each_tls_connection(self) -> None:
        certificate = b"local-only-fake-DER"
        peer = mock.Mock()
        peer.getpeercert.return_value = certificate

        def connect(connection: repro.PinnedHTTPSConnection) -> None:
            connection.sock = peer

        with mock.patch.object(repro.http.client.HTTPSConnection,
                               "connect", autospec=True, side_effect=connect):
            correct = repro.PinnedHTTPSConnection(
                settings(hashlib.sha256(certificate).hexdigest()))
            correct.connect()
            correct.close()
            wrong = repro.PinnedHTTPSConnection(settings())
            with self.assertRaisesRegex(repro.HarnessError, "指纹不匹配"):
                wrong.connect()
        self.assertEqual(peer.getpeercert.call_count, 2)
        self.assertIsNone(wrong.sock)

    def test_request_pins_before_sending_and_closes(self) -> None:
        events: list[str] = []

        class FakeConnection:
            def __init__(self, configured: repro.Settings) -> None:
                self.configured = configured

            def connect(self) -> None:
                events.append("pin")

            def request(self, method: str, path: str, *, body: bytes,
                        headers: dict[str, str]) -> None:
                events.append("request")
                self._headers = headers

            def getresponse(self) -> object:
                return mock.Mock(status=200, read=lambda length: b'{"ok":true}')

            def close(self) -> None:
                events.append("close")

        with mock.patch.object(repro, "PinnedHTTPSConnection", FakeConnection):
            result = repro.Broker(settings()).post("/v1/rooms", {}, "secret-token")
        self.assertEqual(result, {"ok": True})
        self.assertEqual(events, ["pin", "request", "close"])

    def test_workflow_removes_temporary_secrets_and_cleans_broker(self) -> None:
        for should_fail in (False, True):
            with self.subTest(should_fail=should_fail):
                with tempfile.TemporaryDirectory() as directory:
                    fake = FakeBroker()
                    paths: list[pathlib.Path] = []

                    def client_runner(args: argparse.Namespace,
                                      allocation_path: pathlib.Path,
                                      run_dir: pathlib.Path) -> int:
                        paths.append(allocation_path)
                        self.assertEqual(stat.S_IMODE(allocation_path.stat().st_mode),
                                         0o600)
                        allocation = json.loads(allocation_path.read_text())
                        self.assertEqual(allocation["schemaVersion"],
                                         "fps-remote-match-v1")
                        self.assertEqual(len(allocation["players"]), 2)
                        tickets = [item[key] for item in allocation["players"]
                                   for key in ("connectionTicket", "reconnectTicket")]
                        self.assertEqual(len(set(tickets)), 4)
                        for role in ("client-a", "client-b"):
                            folder = run_dir / role
                            folder.mkdir()
                            (folder / "connection.ticket").write_text(tickets[0])
                            (folder / "reconnect.ticket").write_text(tickets[1])
                        if should_fail:
                            raise repro.HarnessError("本地模拟客户端失败")
                        return 0

                    args = argparse.Namespace(
                        output=pathlib.Path(directory), client=pathlib.Path("fake.app"),
                        build_manifest=None, timeout=1, ssh_target=None,
                        host_override="")
                    with mock.patch.object(repro, "run_clients", side_effect=client_runner), \
                         mock.patch.object(repro, "analyze", return_value={
                             "client-a": "green", "client-b": "green"}):
                        code, report_path = repro.run_workflow(args, fake)
                    self.assertEqual(code, 1 if should_fail else 0)
                    self.assertFalse(paths[0].exists())
                    self.assertFalse((report_path.parent / "client-a/connection.ticket").exists())
                    self.assertFalse((report_path.parent / "client-b/reconnect.ticket").exists())
                    report_text = report_path.read_text()
                    self.assertNotIn("secret-ticket", report_text)
                    self.assertNotIn("token-acc", report_text)
                    for password in fake.passwords:
                        self.assertNotIn(password, report_text)
                    self.assertEqual(fake.calls[-3:], [
                        "/v1/rooms/room-local-test/leave",
                        "/v1/auth/logout", "/v1/auth/logout"])

    def test_host_override_is_local_only_and_forwarded_verbatim(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            args = argparse.Namespace(
                output=root, client=pathlib.Path("fake.app"),
                build_manifest=None, timeout=1, ssh_target=None,
                host_override="")
            commands: list[list[str]] = []

            def fake_run(command: list[str], **kwargs: object) -> object:
                commands.append(command)
                return mock.Mock(returncode=0)

            with mock.patch.object(repro.subprocess, "run", side_effect=fake_run):
                repro.run_clients(args, root / "allocation.json", root)
                args.host_override = "127.0.0.1"
                repro.run_clients(args, root / "allocation.json", root)
            self.assertNotIn("--host-override", commands[0])
            self.assertEqual(commands[1][-2:], ["--host-override", "127.0.0.1"])
            for invalid in ("localhost", "127.0.0.2", "::1",
                            "127.0.0.1:17777", " 127.0.0.1"):
                with self.subTest(invalid=invalid):
                    args.host_override = invalid
                    with mock.patch.object(repro.subprocess, "run") as runner:
                        with self.assertRaises(repro.HarnessError):
                            repro.run_clients(args, root / "allocation.json", root)
                        runner.assert_not_called()
                    broker = FakeBroker()
                    with self.assertRaises(repro.HarnessError):
                        repro.run_workflow(args, broker)
                    self.assertEqual(broker.calls, [])

    def test_proxy_environment_is_not_inherited(self) -> None:
        with mock.patch.dict(os.environ, {"HTTP_PROXY": "http://proxy.invalid",
                                               "https_proxy": "http://proxy.invalid",
                                               "ALL_PROXY": "http://proxy.invalid"}):
            environment = repro.subprocess_environment()
        self.assertEqual(environment["FPS_NETTRACE"], "1")
        self.assertFalse(any(key.lower().endswith("_proxy")
                             for key in environment))

    def test_ssh_reads_only_the_validated_match_log_with_noninteractive_sudo(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            destination = pathlib.Path(directory) / "server.log"
            commands: list[list[str]] = []

            def fake_run(command: list[str], **kwargs: object) -> object:
                commands.append(command)
                stream = kwargs["stdout"]
                assert hasattr(stream, "write")
                stream.write(b"[NETTRACE-v1] role=server kind=enemy\n")
                return mock.Mock(returncode=0)

            with mock.patch.object(repro.subprocess, "run", side_effect=fake_run):
                repro.pull_server_log("ubuntu@127.0.0.1", "coop-local-test",
                                      destination)
            self.assertEqual(commands[0][-1],
                             "sudo -n cat -- /srv/fps/data/broker/matches/"
                             "coop-local-test/server.log")
            self.assertEqual(commands[0][-2], "ubuntu@127.0.0.1")
            self.assertEqual(stat.S_IMODE(destination.stat().st_mode), 0o600)
            with self.assertRaises(repro.HarnessError):
                repro.pull_server_log("ubuntu@127.0.0.1;touch /tmp/unsafe",
                                      "coop-local-test", destination)


if __name__ == "__main__":
    unittest.main()
