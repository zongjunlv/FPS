#!/usr/bin/env python3
"""Diagnostic-only two-client UDP forwarder scoped to a macOS interface.

Unity clients connect to 127.0.0.1:17777. Each distinct client source port
gets its own upstream UDP socket, bound with IP_BOUND_IF before connecting to
the public server. This is a UDP data path only: no SSH, TCP, or game code.

Example (do not start while another process owns local UDP 17777)::

    python3 scripts/networking/local_interface_udp_proxy.py \
        --interface en0 --upstream-host 43.128.141.28 \
        --upstream-port 17777 --listen-port 17777

Stop with Ctrl-C. The final JSON line reports bidirectional packet/byte counts.
IP_BOUND_IF selects the egress interface; a third-party network extension may
still intercept traffic, so this tool alone does not prove packet-level routing.
"""

from __future__ import annotations

import argparse
import ipaddress
import json
import selectors
import signal
import socket
import sys
import threading
import time
from dataclasses import dataclass
from typing import Callable


LOOPBACK = "127.0.0.1"
DEFAULT_UPSTREAM = "43.128.141.28"
MAX_DATAGRAM = 65535
InterfaceBinder = Callable[[socket.socket, str], None]


def bind_mac_interface(sock: socket.socket, interface: str) -> None:
    """Scope an IPv4 UDP socket to one macOS network interface."""
    if sys.platform != "darwin":
        raise OSError("IP_BOUND_IF egress scoping requires macOS")
    index = socket.if_nametoindex(interface)
    if index <= 0:
        raise OSError(f"interface {interface!r} has no usable index")
    # Darwin IP_BOUND_IF is 25; some Python builds do not export the name.
    option = getattr(socket, "IP_BOUND_IF", 25)
    sock.setsockopt(socket.IPPROTO_IP, option, index)


@dataclass
class ClientSession:
    address: tuple[str, int]
    upstream: socket.socket
    last_activity: float


class LocalInterfaceUdpProxy:
    """Forward at most two localhost UDP clients over separate upstream sockets."""

    def __init__(
        self,
        *,
        upstream_host: str = DEFAULT_UPSTREAM,
        upstream_port: int = 17777,
        listen_port: int = 17777,
        interface: str = "en0",
        max_clients: int = 2,
        idle_seconds: float = 300.0,
        interface_binder: InterfaceBinder = bind_mac_interface,
    ) -> None:
        ipaddress.IPv4Address(upstream_host)
        if not (1 <= upstream_port <= 65535):
            raise ValueError("upstream_port must be 1..65535")
        if not (0 <= listen_port <= 65535):
            raise ValueError("listen_port must be 0..65535")
        if max_clients != 2:
            raise ValueError("diagnostic proxy supports exactly two clients")
        if idle_seconds <= 0:
            raise ValueError("idle_seconds must be positive")
        self.upstream_endpoint = (upstream_host, upstream_port)
        self.interface = interface
        self.idle_seconds = idle_seconds
        self.interface_binder = interface_binder
        self.selector = selectors.DefaultSelector()
        self.listener = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            self.listener.bind((LOOPBACK, listen_port))
            self._listen_address = self.listener.getsockname()
            self.listener.setblocking(False)
            self.selector.register(self.listener, selectors.EVENT_READ)
        except BaseException:
            self.listener.close()
            self.selector.close()
            raise
        self.sessions: dict[tuple[str, int], ClientSession] = {}
        self.up_packets = 0
        self.up_bytes = 0
        self.down_packets = 0
        self.down_bytes = 0
        self.dropped_packets = 0
        self.socket_errors = 0
        self.created_sessions = 0
        self.expired_sessions = 0
        self._closed = False

    @property
    def listen_address(self) -> tuple[str, int]:
        return self._listen_address

    def snapshot(self) -> dict[str, object]:
        return {
            "listen": f"{self.listen_address[0]}:{self.listen_address[1]}",
            "upstream": f"{self.upstream_endpoint[0]}:{self.upstream_endpoint[1]}",
            "interface": self.interface,
            "activeClients": len(self.sessions),
            "createdClients": self.created_sessions,
            "expiredClients": self.expired_sessions,
            "upPackets": self.up_packets,
            "upBytes": self.up_bytes,
            "downPackets": self.down_packets,
            "downBytes": self.down_bytes,
            "droppedPackets": self.dropped_packets,
            "socketErrors": self.socket_errors,
        }

    def _open_session(self, address: tuple[str, int]) -> ClientSession | None:
        if len(self.sessions) >= 2:
            return None
        upstream = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        try:
            self.interface_binder(upstream, self.interface)
            upstream.connect(self.upstream_endpoint)
            upstream.setblocking(False)
            session = ClientSession(address, upstream, time.monotonic())
            self.selector.register(upstream, selectors.EVENT_READ, session)
        except BaseException:
            upstream.close()
            raise
        self.sessions[address] = session
        self.created_sessions += 1
        return session

    def _close_session(self, session: ClientSession) -> None:
        self.sessions.pop(session.address, None)
        try:
            self.selector.unregister(session.upstream)
        except KeyError:
            pass
        session.upstream.close()

    def _from_client(self) -> None:
        data, address = self.listener.recvfrom(MAX_DATAGRAM)
        if address[0] != LOOPBACK:
            self.dropped_packets += 1
            return
        session = self.sessions.get(address)
        if session is None:
            session = self._open_session(address)
            if session is None:
                self.dropped_packets += 1
                return
        session.last_activity = time.monotonic()
        try:
            sent = session.upstream.send(data)
        except OSError:
            self.socket_errors += 1
            self.dropped_packets += 1
            return
        self.up_packets += 1
        self.up_bytes += sent

    def _from_upstream(self, session: ClientSession) -> None:
        try:
            data = session.upstream.recv(MAX_DATAGRAM)
            sent = self.listener.sendto(data, session.address)
        except OSError:
            self.socket_errors += 1
            self.dropped_packets += 1
            return
        session.last_activity = time.monotonic()
        self.down_packets += 1
        self.down_bytes += sent

    def _expire_idle(self) -> None:
        now = time.monotonic()
        for session in list(self.sessions.values()):
            if now - session.last_activity > self.idle_seconds:
                self._close_session(session)
                self.expired_sessions += 1

    def serve_forever(self, stop: threading.Event) -> None:
        try:
            while not stop.is_set():
                for key, _ in self.selector.select(timeout=0.2):
                    if key.fileobj is self.listener:
                        self._from_client()
                    else:
                        self._from_upstream(key.data)
                self._expire_idle()
        finally:
            self.close()

    def close(self) -> None:
        if self._closed:
            return
        for session in list(self.sessions.values()):
            self._close_session(session)
        try:
            self.selector.unregister(self.listener)
        except KeyError:
            pass
        self.listener.close()
        self.selector.close()
        self._closed = True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--interface", default="en0")
    parser.add_argument("--upstream-host", default=DEFAULT_UPSTREAM,
                        help="numeric IPv4 address; no DNS or SSH relay")
    parser.add_argument("--upstream-port", type=int, default=17777)
    parser.add_argument("--listen-port", type=int, default=17777)
    parser.add_argument("--idle-seconds", type=float, default=300.0)
    args = parser.parse_args()
    proxy = LocalInterfaceUdpProxy(
        upstream_host=args.upstream_host,
        upstream_port=args.upstream_port,
        listen_port=args.listen_port,
        interface=args.interface,
        idle_seconds=args.idle_seconds,
    )
    stop = threading.Event()
    signal.signal(signal.SIGINT, lambda *_: stop.set())
    signal.signal(signal.SIGTERM, lambda *_: stop.set())
    print(json.dumps({"status": "listening", **proxy.snapshot()}), flush=True)
    try:
        proxy.serve_forever(stop)
    finally:
        print(json.dumps({"status": "stopped", **proxy.snapshot()}), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
