#!/usr/bin/env python3

import argparse
import base64
import hashlib
import hmac
import importlib.util
import pathlib
import unittest
from unittest import mock


PATH = pathlib.Path(__file__).with_name("issue101_server_manager.py")
SPEC = importlib.util.spec_from_file_location("issue101_server_manager", PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


def decode(value: str) -> bytes:
    return base64.urlsafe_b64decode(value + "=" * (-len(value) % 4))


class Issue101ServerManagerTests(unittest.TestCase):
    def test_ticket_matches_server_codec_format_and_signature(self):
        secret = "issue101-secret-is-longer-than-thirty-two-bytes"
        compatibility = MODULE.compatibility_token("1.2.3", "net-7",
                                                   "citynew-9")
        ticket = MODULE.issue_ticket(secret, "player-a", compatibility,
                                     "match-101", 2_000_000_000, 600)
        prefix, encoded_payload, signature = ticket.split(".")
        values = decode(encoded_payload).decode().split("\n")
        expected = hmac.new(secret.encode(),
                            f"{prefix}.{encoded_payload}".encode(),
                            hashlib.sha256).digest()

        self.assertEqual(prefix, "fps1")
        self.assertEqual(decode(values[0]).decode(), "player-a")
        self.assertEqual(decode(values[1]).decode(), compatibility)
        self.assertEqual(values[3:5], ["2000000000", "2000000600"])
        self.assertEqual(decode(values[5]).decode(), "match-101")
        self.assertTrue(hmac.compare_digest(decode(signature), expected))

    def test_server_command_contains_versions_but_never_secret(self):
        args = argparse.Namespace(
            port=18801, match_id="match-101", maximum_players=2,
            seed=18018, application_version="1.2.3",
            protocol_version="net-7", content_version="citynew-9",
            tick_rate=60, idle_timeout=120,
        )
        command = MODULE.server_command(args, pathlib.Path("/game/server"),
                                        pathlib.Path("/state/diag.json"))
        rendered = " ".join(command)

        self.assertIn("-server-protocol-version net-7", rendered)
        self.assertIn("-server-content-version citynew-9", rendered)
        self.assertNotIn(MODULE.SECRET_ENV, rendered)
        self.assertNotIn("secret", rendered.lower())

    def test_identifiers_reject_shell_and_path_characters(self):
        for value in ("bad value", "bad/path", "bad;value", ""):
            with self.assertRaises(ValueError):
                MODULE.validate_identifier(value, "test")

    def test_foreground_runner_maps_terminal_lifecycle_to_exit_code(self):
        for lifecycle in ("launching", "starting", "ready", "unknown"):
            self.assertIsNone(MODULE.lifecycle_exit_code(lifecycle))
        for lifecycle in ("stopped", "recycled"):
            self.assertEqual(MODULE.lifecycle_exit_code(lifecycle), 0)
        self.assertEqual(MODULE.lifecycle_exit_code("crashed"), 1)

    def test_active_state_requires_a_live_recorded_process(self):
        state = {
            "lifecycleStatus": "ready",
            "serverPid": 101,
            "launcherPid": 102,
            "processGroupId": 101,
        }
        with mock.patch.object(MODULE, "process_is_alive",
                               side_effect=lambda pid: pid == 102):
            self.assertTrue(MODULE.state_has_live_process(state))
        with mock.patch.object(MODULE, "process_is_alive",
                               return_value=False):
            self.assertFalse(MODULE.state_has_live_process(state))


if __name__ == "__main__":
    unittest.main()
