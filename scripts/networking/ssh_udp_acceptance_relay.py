#!/usr/bin/env python3
"""Temporary two-client UDP-over-SSH relay for restricted test networks.

This is only a diagnostic transport. Normal clients connect directly to the
dedicated server over UDP; the relay is not part of the shipped game.
"""

from __future__ import annotations

import argparse
import os
import select
import socket
import subprocess
import sys
import time


def frame(slot: int, payload: bytes) -> bytes:
    if not 0 <= slot < 2 or len(payload) > 65507:
        raise ValueError("invalid relay frame")
    return bytes((slot,)) + len(payload).to_bytes(2, "big") + payload


def drain(buffer: bytearray):
    while len(buffer) >= 3:
        slot = buffer[0]
        length = int.from_bytes(buffer[1:3], "big")
        if slot >= 2 or length > 65507:
            raise ValueError("invalid relay stream")
        if len(buffer) < length + 3:
            break
        payload = bytes(buffer[3:length + 3])
        del buffer[:length + 3]
        yield slot, payload


def write_all(fd: int, data: bytes) -> None:
    while data:
        count = os.write(fd, data)
        if count <= 0:
            raise BrokenPipeError("relay pipe closed")
        data = data[count:]


def remote(port: int) -> int:
    sockets = []
    for _ in range(2):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.connect(("127.0.0.1", port))
        sockets.append(sock)
    pending = bytearray()
    while True:
        readable, _, _ = select.select([0, *sockets], [], [])
        if 0 in readable:
            chunk = os.read(0, 65536)
            if not chunk:
                return 0
            pending.extend(chunk)
            for slot, payload in drain(pending):
                try:
                    sockets[slot].send(payload)
                except ConnectionRefusedError:
                    # The previous short-lived dedicated process exited.
                    # A later match may bind the same UDP port again.
                    continue
        for slot, sock in enumerate(sockets):
            if sock in readable:
                try:
                    payload = sock.recv(65507)
                except ConnectionRefusedError:
                    continue
                write_all(1, frame(slot, payload))


def local(host: str, remote_script: str, port: int) -> int:
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", port))
    command = ["ssh", "-T", "-o", "BatchMode=yes", "-o",
               "StrictHostKeyChecking=yes", host, "python3", "-u",
               remote_script, "--remote", "--port", str(port)]
    process = subprocess.Popen(command, stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE)
    addresses: list[tuple[str, int] | None] = [None, None]
    last_seen = [0.0, 0.0]
    pending = bytearray()
    try:
        while process.poll() is None:
            readable, _, _ = select.select([sock, process.stdout], [], [], 1)
            if sock in readable:
                payload, address = sock.recvfrom(65507)
                now = time.monotonic()
                if address in addresses:
                    slot = addresses.index(address)
                else:
                    slot = next((index for index in range(2)
                                 if addresses[index] is None or
                                 now - last_seen[index] > 10), -1)
                    if slot < 0:
                        continue
                    addresses[slot] = address
                last_seen[slot] = now
                write_all(process.stdin.fileno(),
                          frame(slot, payload))
            if process.stdout in readable:
                chunk = os.read(process.stdout.fileno(), 65536)
                if not chunk:
                    break
                pending.extend(chunk)
                for slot, payload in drain(pending):
                    if addresses[slot] is not None:
                        sock.sendto(payload, addresses[slot])
    finally:
        sock.close()
        process.terminate()
        process.wait(timeout=5)
    return process.returncode or 0


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--remote", action="store_true")
    parser.add_argument("--host")
    parser.add_argument("--remote-script")
    parser.add_argument("--port", type=int, default=17777)
    args = parser.parse_args()
    if args.remote:
        return remote(args.port)
    if not args.host or not args.remote_script:
        parser.error("local relay requires --host and --remote-script")
    return local(args.host, args.remote_script, args.port)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(0)
