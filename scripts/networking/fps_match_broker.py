#!/usr/bin/env python3
"""HTTPS match allocator for the two-player authoritative FPS demo.

The broker verifies Unity Authentication JWTs with Unity's public JWKS,
allocates one Linux headless match, and returns a short-lived HMAC ticket for
the authenticated room member. Access tokens and signing secrets are never
written to disk or logs.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import pathlib
import ssl
import subprocess
import sys
import threading
import time
import urllib.request
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Any

import issue101_server_manager as server_manager


JWKS_URL = "https://player-auth.services.api.unity.com/.well-known/jwks.json"
SHA256_DIGEST_INFO = bytes.fromhex("3031300d060960864801650304020105000420")
MAXIMUM_BODY_BYTES = 32 * 1024


def b64decode(value: str) -> bytes:
    normalized = value + "=" * ((4 - len(value) % 4) % 4)
    return base64.urlsafe_b64decode(normalized.encode("ascii"))


def safe_identifier(value: Any, label: str, maximum: int = 128) -> str:
    normalized = str(value or "").strip()
    if not normalized or len(normalized) > maximum or any(
            character not in
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"
            for character in normalized):
        raise ValueError(f"{label} 格式无效")
    return normalized


class UnityTokenVerifier:
    def __init__(self, project_id: str, issuer: str,
                 jwks_url: str = JWKS_URL) -> None:
        self.project_id = project_id
        self.issuer = issuer.rstrip("/")
        self.jwks_url = jwks_url
        self.keys: dict[str, dict[str, str]] = {}
        self.refreshed_at = 0.0
        self.lock = threading.Lock()

    def verify(self, token: str) -> str:
        segments = (token or "").split(".")
        if len(segments) != 3:
            raise PermissionError("登录令牌格式无效")
        try:
            header = json.loads(b64decode(segments[0]))
            claims = json.loads(b64decode(segments[1]))
            signature = b64decode(segments[2])
        except (ValueError, TypeError, json.JSONDecodeError) as error:
            raise PermissionError("登录令牌无法解析") from error
        if header.get("alg") != "RS256":
            raise PermissionError("登录令牌签名算法无效")
        kid = str(header.get("kid") or "")
        key = self._key(kid)
        signed = f"{segments[0]}.{segments[1]}".encode("ascii")
        if not self._verify_rs256(signed, signature, key):
            self._refresh(force=True)
            key = self._key(kid, allow_refresh=False)
            if not self._verify_rs256(signed, signature, key):
                raise PermissionError("登录令牌签名无效")

        now = int(time.time())
        if int(claims.get("exp") or 0) <= now:
            raise PermissionError("登录令牌已过期")
        if int(claims.get("nbf") or 0) > now + 30:
            raise PermissionError("登录令牌尚未生效")
        if str(claims.get("iss") or "").rstrip("/") != self.issuer:
            raise PermissionError("登录令牌签发方无效")
        if str(claims.get("project_id") or "") != self.project_id:
            raise PermissionError("登录令牌不属于当前项目")
        return safe_identifier(claims.get("sub"), "玩家账号")

    def _key(self, kid: str, allow_refresh: bool = True) -> dict[str, str]:
        self._refresh(force=False)
        key = self.keys.get(kid)
        if key is None and allow_refresh:
            self._refresh(force=True)
            key = self.keys.get(kid)
        if key is None:
            raise PermissionError("找不到登录令牌公钥")
        return key

    def _refresh(self, force: bool) -> None:
        with self.lock:
            if not force and self.keys and time.monotonic() - self.refreshed_at < 8 * 3600:
                return
            request = urllib.request.Request(
                self.jwks_url, headers={"User-Agent": "fps-match-broker/1"})
            with urllib.request.urlopen(request, timeout=8) as response:
                payload = json.loads(response.read(MAXIMUM_BODY_BYTES))
            keys = {
                str(value.get("kid")): value
                for value in payload.get("keys", [])
                if value.get("alg") == "RS256" and value.get("kty") == "RSA"
            }
            if not keys:
                raise PermissionError("Unity 登录公钥暂时不可用")
            self.keys = keys
            self.refreshed_at = time.monotonic()

    @staticmethod
    def _verify_rs256(message: bytes, signature: bytes,
                      key: dict[str, str]) -> bool:
        try:
            modulus = int.from_bytes(b64decode(key["n"]), "big")
            exponent = int.from_bytes(b64decode(key["e"]), "big")
            size = (modulus.bit_length() + 7) // 8
            if len(signature) != size:
                return False
            encoded = pow(int.from_bytes(signature, "big"), exponent,
                          modulus).to_bytes(size, "big")
            digest = hashlib.sha256(message).digest()
            padding = b"\xff" * (size - len(SHA256_DIGEST_INFO) - len(digest) - 3)
            expected = b"\x00\x01" + padding + b"\x00" + \
                SHA256_DIGEST_INFO + digest
            return hmac.compare_digest(encoded, expected)
        except (KeyError, ValueError, OverflowError):
            return False


class MatchBroker:
    def __init__(self, args: argparse.Namespace) -> None:
        self.args = args
        self.secret = server_manager.require_secret()
        self.verifier = UnityTokenVerifier(
            args.project_id, args.unity_issuer, args.jwks_url)
        self.state_path = args.state_root / "broker-state.json"
        self.lock = threading.Lock()

    def allocate(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        seed = int(payload.get("seed") or 18018)
        roster = self._roster(payload.get("players"))
        if not any(player["accountId"] == account for player in roster):
            raise PermissionError("房主账号不在战局名单中")
        self._validate_compatibility(payload)
        match_id = "coop-" + hashlib.sha256(
            session_id.encode("utf-8")).hexdigest()[:24]

        with self.lock:
            current = server_manager.read_json(self.state_path)
            if current and current.get("sessionId") == session_id and \
                    self._match_ready(current):
                self._assert_member(current.get("roster", []), account)
                return self._response(current, account)
            if current and self._match_running(current):
                raise RuntimeError("专用服务器正在承载另一场战局，请稍后重试")

            allocation_path = (self.args.state_root / "allocations" /
                               f"{match_id}.json")
            command = [
                sys.executable, str(self.args.manager), "start",
                "--binary", str(self.args.binary),
                "--public-host", self.args.public_host,
                "--port", str(self.args.game_port),
                "--match-id", match_id,
                "--maximum-players", "2",
                "--seed", str(seed),
                "--application-version", self.args.application_version,
                "--protocol-version", self.args.protocol_version,
                "--content-version", self.args.content_version,
                "--tick-rate", str(self.args.tick_rate),
                "--idle-timeout", str(self.args.idle_timeout),
                "--ticket-lifetime", str(self.args.ticket_lifetime),
                "--state-root", str(self.args.state_root / "matches"),
                "--allocation-output", str(allocation_path),
            ]
            for player in roster:
                command.extend(("--player",
                                f"{player['accountId']}={player['appearanceId']}"))
            completed = subprocess.run(
                command, stdin=subprocess.DEVNULL, capture_output=True,
                text=True, timeout=self.args.startup_timeout + 10,
                env=os.environ.copy(), check=False)
            if completed.returncode != 0:
                error = completed.stderr.strip() or completed.stdout.strip()
                raise RuntimeError("专用服务器启动失败：" + error[:300])
            allocation = server_manager.read_json(allocation_path)
            if allocation.get("matchId") != match_id:
                raise RuntimeError("专用服务器没有返回有效分配结果")
            state = {
                "schemaVersion": server_manager.SCHEMA,
                "sessionId": session_id,
                "matchId": match_id,
                "host": self.args.public_host,
                "port": self.args.game_port,
                "applicationVersion": self.args.application_version,
                "protocolVersion": self.args.protocol_version,
                "contentVersion": self.args.content_version,
                "maximumPlayers": 2,
                "roster": roster,
                "serverStatePath": allocation.get("statePath", ""),
            }
            server_manager.write_state(self.state_path, state)
            return self._response(state, account)

    def join(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        match_id = safe_identifier(payload.get("matchId"), "战局 ID")
        self._validate_compatibility(payload)
        with self.lock:
            state = server_manager.read_json(self.state_path)
            if state.get("sessionId") != session_id or \
                    state.get("matchId") != match_id:
                raise PermissionError("战局与当前房间不匹配")
            self._assert_member(state.get("roster", []), account)
            if not self._match_ready(state):
                raise RuntimeError("专用服务器尚未就绪或已经回收")
            return self._response(state, account)

    def release(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        match_id = safe_identifier(payload.get("matchId"), "战局 ID")
        with self.lock:
            state = server_manager.read_json(self.state_path)
            roster = state.get("roster", [])
            if state.get("sessionId") != session_id or \
                    state.get("matchId") != match_id:
                raise PermissionError("战局与当前房间不匹配")
            if not roster or roster[0].get("accountId") != account:
                raise PermissionError("只有房主可以结束专用服务器战局")
            server_state_path = pathlib.Path(
                str(state.get("serverStatePath") or ""))
            server_state = server_manager.read_json(server_state_path)
            if server_manager.state_has_live_process(server_state):
                server_state["statePath"] = str(server_state_path)
                server_manager.stop_process(server_state, "lobby-return")
            state["releasedAtUtc"] = server_manager.iso()
            state["releasedBy"] = account
            server_manager.write_state(self.state_path, state)
            return {"released": True, "matchId": match_id}

    def _response(self, state: dict[str, Any], account: str) -> dict[str, Any]:
        now = int(time.time())
        compatibility = server_manager.compatibility_token(
            self.args.application_version,
            self.args.protocol_version,
            self.args.content_version)
        return {
            "connection": {
                "schemaVersion": server_manager.SCHEMA,
                "host": state["host"],
                "port": int(state["port"]),
                "matchId": state["matchId"],
                "applicationVersion": self.args.application_version,
                "protocolVersion": self.args.protocol_version,
                "contentVersion": self.args.content_version,
                "maximumPlayers": 2,
            },
            "connectionTicket": server_manager.issue_ticket(
                self.secret, account, compatibility, state["matchId"], now,
                self.args.ticket_lifetime),
        }

    def _validate_compatibility(self, payload: dict[str, Any]) -> None:
        expected = (
            self.args.application_version,
            self.args.protocol_version,
            self.args.content_version,
        )
        actual = (
            str(payload.get("applicationVersion") or ""),
            str(payload.get("protocolVersion") or ""),
            str(payload.get("contentVersion") or ""),
        )
        if actual != expected:
            raise ValueError("客户端版本、协议或内容与专用服务器不一致")

    @staticmethod
    def _roster(value: Any) -> list[dict[str, str]]:
        if not isinstance(value, list) or not 1 <= len(value) <= 2:
            raise ValueError("战局需要 1—2 名房间成员")
        result: list[dict[str, str]] = []
        accounts: set[str] = set()
        for raw in value:
            if not isinstance(raw, dict):
                raise ValueError("战局成员格式无效")
            account = safe_identifier(raw.get("accountId"), "玩家账号")
            appearance = safe_identifier(
                raw.get("appearanceId"), "角色外观")
            if account in accounts:
                raise ValueError("战局成员账号不能重复")
            accounts.add(account)
            result.append({"accountId": account,
                           "appearanceId": appearance})
        return result

    @staticmethod
    def _assert_member(roster: list[dict[str, str]], account: str) -> None:
        if not any(player.get("accountId") == account for player in roster):
            raise PermissionError("当前账号不属于这场战局")

    @staticmethod
    def _match_running(state: dict[str, Any]) -> bool:
        path = pathlib.Path(str(state.get("serverStatePath") or ""))
        server = server_manager.read_json(path)
        return server.get("lifecycleStatus") in (
            "launching", "starting", "ready") and \
            server_manager.state_has_live_process(server)

    @classmethod
    def _match_ready(cls, state: dict[str, Any]) -> bool:
        path = pathlib.Path(str(state.get("serverStatePath") or ""))
        server = server_manager.read_json(path)
        return server.get("lifecycleStatus") == "ready" and \
            server_manager.state_has_live_process(server)


class BrokerHandler(BaseHTTPRequestHandler):
    server_version = "FPSMatchBroker/1"

    def do_GET(self) -> None:
        if self.path != "/healthz":
            self._json(HTTPStatus.NOT_FOUND, {"error": "not-found"})
            return
        self._json(HTTPStatus.OK, {"status": "ok"})

    def do_POST(self) -> None:
        if self.path not in ("/v1/matches/allocate", "/v1/matches/join",
                             "/v1/matches/release"):
            self._json(HTTPStatus.NOT_FOUND, {"error": "not-found"})
            return
        try:
            authorization = self.headers.get("Authorization", "")
            if not authorization.startswith("Bearer "):
                raise PermissionError("缺少 Unity 登录令牌")
            account = self.server.broker.verifier.verify(
                authorization[7:].strip())
            length = int(self.headers.get("Content-Length") or 0)
            if length < 1 or length > MAXIMUM_BODY_BYTES:
                raise ValueError("请求内容大小无效")
            payload = json.loads(self.rfile.read(length))
            if self.path.endswith("/allocate"):
                result = self.server.broker.allocate(account, payload)
            elif self.path.endswith("/release"):
                result = self.server.broker.release(account, payload)
            else:
                result = self.server.broker.join(account, payload)
            self._json(HTTPStatus.OK, result)
        except PermissionError as error:
            self._json(HTTPStatus.FORBIDDEN, {"error": str(error)})
        except (ValueError, json.JSONDecodeError) as error:
            self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)})
        except subprocess.TimeoutExpired:
            self._json(HTTPStatus.GATEWAY_TIMEOUT,
                       {"error": "专用服务器启动超时"})
        except RuntimeError as error:
            self._json(HTTPStatus.CONFLICT, {"error": str(error)})
        except Exception:
            self._json(HTTPStatus.INTERNAL_SERVER_ERROR,
                       {"error": "战局分配服务暂时不可用"})

    def log_message(self, format: str, *args: Any) -> None:
        # Avoid logging authorization headers or request bodies.
        sys.stderr.write("%s - %s\n" % (self.address_string(), format % args))

    def _json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
        rendered = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(int(status))
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(rendered)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(rendered)


class BrokerServer(ThreadingHTTPServer):
    def __init__(self, address: tuple[str, int], broker: MatchBroker) -> None:
        super().__init__(address, BrokerHandler)
        self.broker = broker


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--listen", default="0.0.0.0")
    result.add_argument("--port", type=int, default=18443)
    result.add_argument("--cert", type=pathlib.Path, required=True)
    result.add_argument("--key", type=pathlib.Path, required=True)
    result.add_argument("--project-id", required=True)
    result.add_argument("--unity-issuer",
                        default="https://player-auth.services.api.unity.com")
    result.add_argument("--jwks-url", default=JWKS_URL)
    result.add_argument("--public-host", required=True)
    result.add_argument("--game-port", type=int, default=17777)
    result.add_argument("--binary", type=pathlib.Path, required=True)
    result.add_argument("--manager", type=pathlib.Path, required=True)
    result.add_argument("--state-root", type=pathlib.Path,
                        default=pathlib.Path("/srv/fps/data/broker"))
    result.add_argument("--application-version", default="0.1.0")
    result.add_argument("--protocol-version", default="1")
    result.add_argument("--content-version", default="citynew-v1")
    result.add_argument("--tick-rate", type=int, default=60)
    result.add_argument("--idle-timeout", type=int, default=120)
    result.add_argument("--ticket-lifetime", type=int, default=600)
    result.add_argument("--startup-timeout", type=int, default=90)
    return result


def main() -> int:
    args = parser().parse_args()
    args.state_root.mkdir(parents=True, exist_ok=True)
    broker = MatchBroker(args)
    server = BrokerServer((args.listen, args.port), broker)
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(str(args.cert), str(args.key))
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
