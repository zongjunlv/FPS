#!/usr/bin/env python3
"""Local-only tests; never contact the configured public game server."""

import socket
import threading
import unittest
from unittest import mock

from local_interface_udp_proxy import (
    LocalInterfaceUdpProxy,
    bind_mac_interface,
)


class LocalEcho:
    def __init__(self):
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.socket.bind(("127.0.0.1", 0))
        self.socket.settimeout(0.1)
        self.received = []
        self.stop = threading.Event()
        self.thread = threading.Thread(target=self._serve, daemon=True)
        self.thread.start()

    @property
    def address(self):
        return self.socket.getsockname()

    def _serve(self):
        while not self.stop.is_set():
            try:
                data, address = self.socket.recvfrom(65535)
            except socket.timeout:
                continue
            self.received.append((data, address))
            self.socket.sendto(data, address)

    def close(self):
        self.stop.set()
        self.thread.join(timeout=2)
        self.socket.close()


class LocalInterfaceUdpProxyTests(unittest.TestCase):
    def setUp(self):
        self.echo = LocalEcho()
        self.stop = threading.Event()
        self.bound_interfaces = []

        def record_bind(_sock, interface):
            self.bound_interfaces.append(interface)

        self.proxy = LocalInterfaceUdpProxy(
            upstream_host="127.0.0.1",
            upstream_port=self.echo.address[1],
            listen_port=0,
            interface="en0",
            idle_seconds=1.0,
            interface_binder=record_bind,
        )
        self.thread = threading.Thread(
            target=self.proxy.serve_forever,
            args=(self.stop,),
            daemon=True,
        )
        self.thread.start()
        self.clients = []

    def tearDown(self):
        for client in self.clients:
            client.close()
        self.stop.set()
        self.thread.join(timeout=2)
        self.proxy.close()
        self.echo.close()

    def client(self):
        client = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        client.bind(("127.0.0.1", 0))
        client.settimeout(1.0)
        self.clients.append(client)
        return client

    def test_two_clients_have_distinct_upstream_sockets_and_demuxed_replies(self):
        self.assertEqual("127.0.0.1", self.proxy.listen_address[0])
        first = self.client()
        second = self.client()
        first.sendto(b"first-a", self.proxy.listen_address)
        second.sendto(b"second-a", self.proxy.listen_address)
        self.assertEqual(b"first-a", first.recvfrom(100)[0])
        self.assertEqual(b"second-a", second.recvfrom(100)[0])
        first.sendto(b"first-b", self.proxy.listen_address)
        self.assertEqual(b"first-b", first.recvfrom(100)[0])

        stats = self.proxy.snapshot()
        self.assertEqual(2, stats["activeClients"])
        self.assertEqual(2, stats["createdClients"])
        self.assertEqual(3, stats["upPackets"])
        self.assertEqual(3, stats["downPackets"])
        self.assertEqual(len(b"first-a") + len(b"second-a") + len(b"first-b"),
                         stats["upBytes"])
        self.assertEqual(stats["upBytes"], stats["downBytes"])
        self.assertEqual(["en0", "en0"], self.bound_interfaces)
        upstream_ports = {address[1] for _, address in self.echo.received}
        self.assertEqual(2, len(upstream_ports))

    def test_third_client_is_dropped_without_cross_talk(self):
        first, second, third = self.client(), self.client(), self.client()
        for client, payload in ((first, b"one"), (second, b"two")):
            client.sendto(payload, self.proxy.listen_address)
            self.assertEqual(payload, client.recvfrom(100)[0])
        third.settimeout(0.3)
        third.sendto(b"three", self.proxy.listen_address)
        with self.assertRaises(socket.timeout):
            third.recvfrom(100)
        self.assertEqual(1, self.proxy.snapshot()["droppedPackets"])
        self.assertEqual(2, self.proxy.snapshot()["activeClients"])

    def test_shutdown_is_safe_and_counters_remain_readable(self):
        client = self.client()
        client.sendto(b"ok", self.proxy.listen_address)
        self.assertEqual(b"ok", client.recvfrom(100)[0])
        self.stop.set()
        self.thread.join(timeout=2)
        self.assertFalse(self.thread.is_alive())
        self.proxy.close()
        stats = self.proxy.snapshot()
        self.assertEqual(0, stats["activeClients"])
        self.assertEqual(1, stats["upPackets"])
        self.assertEqual(1, stats["downPackets"])


class InterfaceBindingTests(unittest.TestCase):
    def test_mac_binding_uses_interface_index_and_ip_bound_if(self):
        fake_socket = mock.Mock()
        with mock.patch("local_interface_udp_proxy.sys.platform", "darwin"), \
             mock.patch("local_interface_udp_proxy.socket.if_nametoindex",
                        return_value=9):
            bind_mac_interface(fake_socket, "en0")
        fake_socket.setsockopt.assert_called_once_with(
            socket.IPPROTO_IP, getattr(socket, "IP_BOUND_IF", 25), 9
        )

    def test_non_mac_default_binding_fails_closed(self):
        with mock.patch("local_interface_udp_proxy.sys.platform", "linux"):
            with self.assertRaises(OSError):
                bind_mac_interface(mock.Mock(), "en0")


if __name__ == "__main__":
    unittest.main()
