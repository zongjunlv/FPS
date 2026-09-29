"""Local-only checks: never launch a client for an incompatible allocation."""

import contextlib
import io
import unittest
from unittest import mock

import issue101_server_manager as server_manager
import run_issue101_remote_clients as remote_clients


class RemoteClientCompatibilityTests(unittest.TestCase):
    def allocation(self, protocol):
        return {
            "schemaVersion": server_manager.SCHEMA,
            "protocolVersion": protocol,
            "players": [{"accountId": "a"}, {"accountId": "b"}],
        }

    def run_main(self, allocation, binary):
        arguments = ["remote-clients", "--allocation", "unused.json",
                     "--client", "unused-client"]
        with mock.patch("sys.argv", arguments), \
                mock.patch.object(remote_clients, "read_json", return_value=allocation), \
                mock.patch.object(remote_clients, "executable", binary), \
                contextlib.redirect_stderr(io.StringIO()):
            return remote_clients.main()

    def test_legacy_allocation_is_rejected_before_resolving_or_launching_binary(self):
        binary = mock.Mock(side_effect=AssertionError("不得启动旧协议客户端"))
        self.assertEqual(self.run_main(self.allocation("1"), binary), 2)
        binary.assert_not_called()

    def test_current_allocation_reaches_normal_binary_validation(self):
        binary = mock.Mock(side_effect=FileNotFoundError("故意不提供客户端产物"))
        self.assertEqual(self.run_main(
            self.allocation(server_manager.BATTLE_PROTOCOL_VERSION), binary), 2)
        binary.assert_called_once()

    def test_missing_protocol_is_rejected_before_resolving_or_launching_binary(self):
        allocation = self.allocation(server_manager.BATTLE_PROTOCOL_VERSION)
        del allocation["protocolVersion"]
        binary = mock.Mock(side_effect=AssertionError("协议缺失时不得启动客户端"))
        self.assertEqual(self.run_main(allocation, binary), 2)
        binary.assert_not_called()


if __name__ == "__main__":
    unittest.main()
