#!/usr/bin/env python3
"""Regression test: an idle TCP peer must not block HTTPS health checks."""

from __future__ import annotations

import importlib.util
import json
import os
import pathlib
import socket
import ssl
import subprocess
import sys
import tempfile
import threading
import time
import unittest
import urllib.request


DIRECTORY = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(DIRECTORY))
MODULE_PATH = pathlib.Path(os.environ.get(
    "FPS_BROKER_TEST_MODULE", DIRECTORY / "fps_match_broker.py"))
spec = importlib.util.spec_from_file_location("fps_broker_under_test", MODULE_PATH)
assert spec is not None and spec.loader is not None
broker_module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(broker_module)


class BrokerTlsTests(unittest.TestCase):
    def test_idle_peer_does_not_block_next_health_check(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            cert = pathlib.Path(temporary) / "cert.pem"
            key = pathlib.Path(temporary) / "key.pem"
            subprocess.run([
                "openssl", "req", "-x509", "-newkey", "rsa:2048",
                "-nodes", "-days", "1", "-subj", "/CN=localhost",
                "-keyout", str(key), "-out", str(cert),
            ], check=True, capture_output=True)

            context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
            context.load_cert_chain(str(cert), str(key))
            server = broker_module.BrokerServer(("127.0.0.1", 0), object())
            if hasattr(server, "tls_context"):
                server.tls_context = context
            else:
                # Match the old production main(): TLS on the listening socket.
                server.socket = context.wrap_socket(server.socket, server_side=True)

            thread = threading.Thread(target=server.serve_forever, daemon=True)
            thread.start()
            idle = socket.create_connection(server.server_address, timeout=1)
            try:
                # Give the accept loop time to enter the idle peer's handshake.
                time.sleep(0.15)
                url = f"https://127.0.0.1:{server.server_address[1]}/healthz"
                with urllib.request.urlopen(
                    url, timeout=1,
                    context=ssl._create_unverified_context(),
                ) as response:
                    self.assertEqual(response.status, 200)
                    self.assertEqual(json.load(response), {"status": "ok"})
            finally:
                idle.close()
                server.shutdown()
                server.server_close()
                thread.join(timeout=2)


if __name__ == "__main__":
    unittest.main()
