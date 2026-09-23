#!/usr/bin/env python3

import argparse
import pathlib
import socket
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import fps_match_broker as broker


def lobby():
    return {
        "id": "room-1", "upid": "project-1", "environmentId": "env-1",
        "hostId": "host-1", "maxPlayers": 2,
        "data": {"map": {"value": "CityNew"},
                 "phase": {"value": "lobby"}},
        "players": [
            {"id": "guest-1", "data": {
                "appearance": {"value": "operative-bravo"},
                "ready": {"value": "1"}}},
            {"id": "host-1", "data": {
                "appearance": {"value": "operative-alpha"},
                "ready": {"value": "1"}}},
        ],
    }


class BrokerTrustTests(unittest.TestCase):
    def test_tls_handshake_is_bounded_per_connection(self):
        with mock.patch.object(socket, "getfqdn",
                               return_value="localhost"):
            server = broker.BrokerServer(("127.0.0.1", 0), mock.Mock())
        try:
            request = mock.Mock()
            secure = mock.MagicMock()
            secure.__enter__.return_value = secure
            server.tls_context = mock.Mock()
            server.tls_context.wrap_socket.return_value = secure
            self.assertTrue(server.connection_slots.acquire(False))
            with mock.patch.object(broker.ThreadingHTTPServer,
                                   "process_request_thread") as dispatch:
                server.process_request_thread(request, ("127.0.0.1", 1))
            request.settimeout.assert_called_once_with(8)
            server.tls_context.wrap_socket.assert_called_once_with(
                request, server_side=True)
            dispatch.assert_called_once_with(secure, ("127.0.0.1", 1))
        finally:
            server.server_close()

    def test_excess_connections_are_closed_without_blocking_accept(self):
        with mock.patch.object(socket, "getfqdn",
                               return_value="localhost"):
            server = broker.BrokerServer(("127.0.0.1", 0), mock.Mock())
        try:
            for _ in range(server.maximum_connections):
                self.assertTrue(server.connection_slots.acquire(False))
            request = mock.Mock()
            server.process_request(request, ("127.0.0.1", 1))
            request.close.assert_called_once_with()
        finally:
            server.server_close()

    def test_roster_is_derived_from_lobby_with_host_first(self):
        self.assertEqual(broker.UnityLobbyVerifier.roster(
            lobby(), "host-1", True), [
                {"accountId": "host-1", "appearanceId": "operative-alpha"},
                {"accountId": "guest-1", "appearanceId": "operative-bravo"},
            ])

    def test_guest_cannot_allocate_and_unready_member_blocks_start(self):
        with self.assertRaises(PermissionError):
            broker.UnityLobbyVerifier.roster(lobby(), "guest-1", True)
        data = lobby()
        data["players"][0]["data"]["ready"]["value"] = "0"
        with self.assertRaises(PermissionError):
            broker.UnityLobbyVerifier.roster(data, "host-1", True)

    def test_wrong_map_or_nonmember_is_rejected(self):
        data = lobby()
        data["data"]["map"]["value"] = "TestMap"
        with self.assertRaises(PermissionError):
            broker.UnityLobbyVerifier.roster(data, "host-1", True)
        with self.assertRaises(PermissionError):
            broker.UnityLobbyVerifier.roster(lobby(), "stranger", False)

    def test_get_checks_project_and_environment(self):
        verifier = broker.UnityLobbyVerifier("project-1", "env-1")
        response = mock.MagicMock()
        response.read.return_value = broker.json.dumps(lobby()).encode()
        response.__enter__.return_value = response
        with mock.patch.object(broker.urllib.request, "urlopen",
                               return_value=response) as urlopen:
            self.assertEqual(verifier.get("room-1", "token")["id"], "room-1")
        request = urlopen.call_args.args[0]
        self.assertEqual(request.get_header("Authorization"), "Bearer token")
        data = lobby()
        data["environmentId"] = "other"
        response.read.return_value = broker.json.dumps(data).encode()
        with mock.patch.object(broker.urllib.request, "urlopen",
                               return_value=response):
            with self.assertRaises(PermissionError):
                verifier.get("room-1", "token")

    def test_allocate_rejects_client_roster_tampering(self):
        instance = broker.MatchBroker.__new__(broker.MatchBroker)
        instance.args = argparse.Namespace(application_version="0.1.0",
                                           protocol_version="1",
                                           content_version="citynew-v1")
        instance.lobbies = mock.Mock()
        instance.lobbies.get.return_value = lobby()
        instance.lobbies.roster.return_value = \
            broker.UnityLobbyVerifier.roster(lobby(), "host-1", True)
        payload = {"sessionId": "room-1", "seed": 123,
                   "applicationVersion": "0.1.0", "protocolVersion": "1",
                   "contentVersion": "citynew-v1", "players": [
                       {"accountId": "host-1",
                        "appearanceId": "operative-alpha"}]}
        with self.assertRaises(PermissionError):
            instance.allocate("host-1", "token", payload)


if __name__ == "__main__":
    unittest.main()
