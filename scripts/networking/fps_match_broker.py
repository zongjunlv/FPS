#!/usr/bin/env python3
"""Self-hosted HTTPS accounts, rooms and authoritative match allocation."""

from __future__ import annotations

import argparse
from collections import defaultdict, deque
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import pathlib
import secrets
import ssl
import subprocess
import sys
import threading
import time
from typing import Any, Callable
from urllib.parse import urlsplit

import fps_control_store
import issue101_server_manager as server_manager

MAXIMUM_BODY_BYTES = 32 * 1024


def safe_identifier(value: Any, label: str, maximum: int = 128) -> str:
    normalized = str(value or "").strip()
    allowed = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"
    if not normalized or len(normalized) > maximum or any(c not in allowed for c in normalized):
        raise ValueError(f"{label} 格式无效")
    return normalized


class RateLimited(PermissionError):
    pass


class SlidingRateLimit:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.attempts: dict[str, deque[float]] = defaultdict(deque)

    def check(self, key: str, limit: int, interval: int = 900) -> None:
        now = time.monotonic()
        with self.lock:
            values = self.attempts[key]
            while values and now - values[0] > interval:
                values.popleft()
            if len(values) >= limit:
                raise RateLimited("请求过于频繁，请稍后再试")
            values.append(now)
            if len(self.attempts) > 10_000:
                self.attempts = defaultdict(deque, {
                    k: v for k, v in self.attempts.items()
                    if v and now - v[-1] <= interval})


class MatchBroker:
    def __init__(self, args: argparse.Namespace,
                 store: fps_control_store.ControlStore | None = None,
                 manager_runner: Callable[[str, int, list[dict[str, str]]],
                                          dict[str, Any]] | None = None,
                 secret: str | None = None) -> None:
        self.args = args
        self.secret = secret if secret is not None else server_manager.require_secret()
        self.store = store or fps_control_store.ControlStore(args.database)
        self.manager_runner = manager_runner or self._start_manager
        self.state_path = args.state_root / "broker-state.json"
        self.lock = threading.RLock()
        self.rate_limit = SlidingRateLimit()
        self.reconcile_orphaned_rooms()

    def reconcile_orphaned_rooms(self) -> list[str]:
        with self.lock:
            current = server_manager.read_json(self.state_path)
            live = bool(current) and self._match_running(current)
            return self.store.recover_orphaned_matches(
                current.get("sessionId") if live else None,
                current.get("matchId") if live else None)

    def register(self, ip: str, payload: dict[str, Any]) -> dict[str, Any]:
        self.rate_limit.check("register-ip:" + ip, 5)
        return self.store.register(payload.get("username"), payload.get("password"))

    def login(self, ip: str, payload: dict[str, Any]) -> dict[str, Any]:
        username = str(payload.get("username") or "").casefold()[:64]
        self.rate_limit.check("login-ip:" + ip, 15)
        self.rate_limit.check("login-user:" + username, 10)
        return self.store.login(payload.get("username"), payload.get("password"))

    def allocate(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        self._validate_compatibility(payload)
        room = self.store.get_room(session_id, account)
        if room["mapId"] != "CityNew" or room["hostId"] != account or room["phase"] not in ("lobby", "loading", "battle"):
            raise PermissionError("只有当前房主可以分配 CityNew 战局")
        roster = [{"accountId": p["accountId"], "appearanceId": p["appearanceId"]}
                  for p in room["players"]]
        if self._roster(payload.get("players")) != roster:
            raise PermissionError("客户端名单与房间实际成员不一致")
        seed = int(payload.get("seed"))
        if seed != room["seed"]:
            raise PermissionError("战局随机种子与房间不一致")
        with self.lock:
            current = server_manager.read_json(self.state_path)
            if current and current.get("sessionId") == session_id and self._match_ready(current) and room["phase"] in ("loading", "battle"):
                self._assert_member(current.get("roster", []), account)
                return self._response(current, account, room)
            if room["phase"] != "lobby" or not roster or not all(p["ready"] for p in room["players"]):
                raise PermissionError("只有全员准备的房主可以分配战局")
            if current and self._match_running(current):
                raise fps_control_store.ConflictError("专用服务器正在承载另一场战局，请稍后重试")
            # A room can run multiple rounds. A fresh ID also gives each
            # Dedicated Server launch its own diagnostics and state directory;
            # a prior round's ready marker must never satisfy this launch.
            match_id = "coop-" + secrets.token_hex(12)
            allocation = self.manager_runner(match_id, seed, roster)
            if allocation.get("matchId") != match_id:
                raise RuntimeError("专用服务器没有返回有效分配结果")
            state = {
                "schemaVersion": server_manager.SCHEMA,
                "sessionId": session_id, "matchId": match_id,
                "host": self.args.public_host, "port": self.args.game_port,
                "applicationVersion": self.args.application_version,
                "protocolVersion": self.args.protocol_version,
                "contentVersion": self.args.content_version,
                "maximumPlayers": 2, "roster": roster,
                "serverStatePath": allocation.get("statePath", ""),
            }
            server_manager.write_state(self.state_path, state)
            try:
                room = self.store.mark_allocated(
                    session_id, account, self._connection(state),
                    room["revision"], roster)
            except Exception:
                server_state_path = pathlib.Path(str(state["serverStatePath"] or ""))
                server_state = server_manager.read_json(server_state_path)
                if server_manager.state_has_live_process(server_state):
                    server_state["statePath"] = str(server_state_path)
                    server_manager.stop_process(server_state, "room-changed")
                raise
            return self._response(state, account, room)

    def _start_manager(self, match_id: str, seed: int,
                       roster: list[dict[str, str]]) -> dict[str, Any]:
        allocation_path = self.args.state_root / "allocations" / f"{match_id}.json"
        command = [
            sys.executable, str(self.args.manager), "start",
            "--binary", str(self.args.binary),
            "--public-host", self.args.public_host,
            "--port", str(self.args.game_port),
            "--match-id", match_id, "--maximum-players", "2",
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
            command.extend(("--player", f"{player['accountId']}={player['appearanceId']}"))
        completed = subprocess.run(
            command, stdin=subprocess.DEVNULL, capture_output=True,
            text=True, timeout=self.args.startup_timeout + 10,
            env=os.environ.copy(), check=False)
        if completed.returncode != 0:
            error = completed.stderr.strip() or completed.stdout.strip()
            raise RuntimeError("专用服务器启动失败：" + error[:300])
        return server_manager.read_json(allocation_path)

    def join(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        match_id = safe_identifier(payload.get("matchId"), "战局 ID")
        self._validate_compatibility(payload)
        room = self.store.get_room(session_id, account)
        if room["phase"] not in ("loading", "battle") or not room["serverAllocation"] or room["serverAllocation"].get("matchId") != match_id:
            raise PermissionError("战局与当前房间不匹配")
        with self.lock:
            state = server_manager.read_json(self.state_path)
            if state.get("sessionId") != session_id or state.get("matchId") != match_id:
                raise PermissionError("战局与当前房间不匹配")
            self._assert_member(state.get("roster", []), account)
            if not self._match_ready(state):
                raise fps_control_store.ConflictError("专用服务器尚未就绪或已经回收")
            return self._response(state, account, room)

    def release(self, account: str, payload: dict[str, Any]) -> dict[str, Any]:
        session_id = safe_identifier(payload.get("sessionId"), "房间 ID")
        match_id = safe_identifier(payload.get("matchId"), "战局 ID")
        room = self.store.get_room(session_id, account)
        if room["hostId"] != account or not room["serverAllocation"] or room["serverAllocation"].get("matchId") != match_id:
            raise PermissionError("只有当前房主可以结束战局")
        with self.lock:
            state = server_manager.read_json(self.state_path)
            if state.get("sessionId") != session_id or state.get("matchId") != match_id:
                raise PermissionError("战局与当前房间不匹配")
            server_state_path = pathlib.Path(str(state.get("serverStatePath") or ""))
            server_state = server_manager.read_json(server_state_path)
            if server_manager.state_has_live_process(server_state):
                server_state["statePath"] = str(server_state_path)
                server_manager.stop_process(server_state, "lobby-return")
            state["releasedAtUtc"] = server_manager.iso()
            state["releasedBy"] = account
            server_manager.write_state(self.state_path, state)
            return {"released": True, "matchId": match_id, "room": room}

    def change_phase(self, account: str, room_id: str,
                     payload: dict[str, Any]) -> dict[str, Any]:
        if payload.get("phase") in ("lobby", "cancelled"):
            room = self.store.get_room(room_id, account)
            allocation = room["serverAllocation"]
            self._release_if_current(account, room_id, allocation)
        return self.store.set_phase(room_id, account, payload)

    def leave_room(self, account: str, room_id: str) -> dict[str, bool]:
        room = self.store.get_room(room_id, account)
        allocation = room["serverAllocation"]
        if room["hostId"] == account and allocation:
            self._release_if_current(account, room_id, allocation)
        return self.store.leave_room(room_id, account)

    def _release_if_current(self, account: str, room_id: str,
                            allocation: dict[str, Any] | None) -> None:
        if not allocation:
            return
        current = server_manager.read_json(self.state_path)
        if current.get("sessionId") == room_id and \
                current.get("matchId") == allocation.get("matchId"):
            self.release(account, {"sessionId": room_id,
                                   "matchId": allocation["matchId"]})
        # An old room snapshot can survive migration or a crash even when the
        # transient match state cannot. The host may reset it to lobby; there
        # is no running allocation in this broker state to release.

    def _response(self, state: dict[str, Any], account: str,
                  room: dict[str, Any]) -> dict[str, Any]:
        compatibility = server_manager.compatibility_token(
            self.args.application_version, self.args.protocol_version,
            self.args.content_version)
        return {
            "connection": self._connection(state),
            "connectionTicket": server_manager.issue_ticket(
                self.secret, account, compatibility, state["matchId"],
                int(time.time()), self.args.ticket_lifetime),
            "room": room,
        }

    def _connection(self, state: dict[str, Any]) -> dict[str, Any]:
        return {
            "schemaVersion": server_manager.SCHEMA,
            "host": state["host"], "port": int(state["port"]),
            "matchId": state["matchId"],
            "applicationVersion": self.args.application_version,
            "protocolVersion": self.args.protocol_version,
            "contentVersion": self.args.content_version,
            "maximumPlayers": 2,
        }

    def _validate_compatibility(self, payload: dict[str, Any]) -> None:
        expected = (self.args.application_version,
                    self.args.protocol_version, self.args.content_version)
        actual = (str(payload.get("applicationVersion") or ""),
                  str(payload.get("protocolVersion") or ""),
                  str(payload.get("contentVersion") or ""))
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
            appearance = safe_identifier(raw.get("appearanceId"), "角色外观")
            if account in accounts:
                raise ValueError("战局成员账号不能重复")
            accounts.add(account)
            result.append({"accountId": account, "appearanceId": appearance})
        return result

    @staticmethod
    def _assert_member(roster: list[dict[str, str]], account: str) -> None:
        if not any(p.get("accountId") == account for p in roster):
            raise PermissionError("当前账号不属于这场战局")

    @staticmethod
    def _match_running(state: dict[str, Any]) -> bool:
        path = pathlib.Path(str(state.get("serverStatePath") or ""))
        server = server_manager.read_json(path)
        return server.get("lifecycleStatus") in ("launching", "starting", "ready") and server_manager.state_has_live_process(server)

    @classmethod
    def _match_ready(cls, state: dict[str, Any]) -> bool:
        path = pathlib.Path(str(state.get("serverStatePath") or ""))
        server = server_manager.read_json(path)
        return server.get("lifecycleStatus") == "ready" and server_manager.state_has_live_process(server)


class BrokerHandler(BaseHTTPRequestHandler):
    server_version = "FPSGameBackend/1"

    def do_GET(self) -> None:
        path = urlsplit(self.path).path
        try:
            if path == "/healthz":
                self._json(HTTPStatus.OK, {"status": "ok"})
                return
            account, _ = self._authorize()
            store = self.server.broker.store
            if path == "/v1/auth/me":
                result = account
            elif path == "/v1/rooms":
                self.server.broker.reconcile_orphaned_rooms()
                result = {"rooms": store.list_rooms()}
            elif path == "/v1/rooms/current":
                self.server.broker.reconcile_orphaned_rooms()
                result = store.current_room(account["accountId"])
                if result is None:
                    self.send_response(int(HTTPStatus.NO_CONTENT))
                    self.send_header("Cache-Control", "no-store")
                    self.end_headers()
                    return
            elif path.startswith("/v1/rooms/") and path.count("/") == 3:
                self.server.broker.reconcile_orphaned_rooms()
                result = store.get_room(
                    safe_identifier(path.rsplit("/", 1)[1], "房间 ID"),
                    account["accountId"])
            else:
                self._json(HTTPStatus.NOT_FOUND, {"error": "not-found"})
                return
            self._json(HTTPStatus.OK, result)
        except Exception as error:
            self._error(error)

    def do_POST(self) -> None:
        path = urlsplit(self.path).path
        try:
            payload = self._payload()
            broker = self.server.broker
            if path == "/v1/auth/register":
                result = broker.register(self.client_address[0], payload)
            elif path == "/v1/auth/login":
                result = broker.login(self.client_address[0], payload)
            else:
                account, token = self._authorize()
                aid, store = account["accountId"], broker.store
                if path == "/v1/auth/logout":
                    store.logout(token)
                    result = {"loggedOut": True}
                elif path == "/v1/rooms":
                    result = store.create_room(aid, payload)
                elif path == "/v1/rooms/join":
                    result = store.join_room(aid, payload)
                elif path.startswith("/v1/rooms/") and path.count("/") == 4:
                    parts = path.split("/")
                    rid, action = safe_identifier(parts[3], "房间 ID"), parts[4]
                    if action == "player":
                        result = store.update_player(rid, aid, payload)
                    elif action == "phase":
                        result = broker.change_phase(aid, rid, payload)
                    elif action == "leave":
                        result = broker.leave_room(aid, rid)
                    else:
                        self._json(HTTPStatus.NOT_FOUND, {"error": "not-found"})
                        return
                elif path == "/v1/matches/allocate":
                    result = broker.allocate(aid, payload)
                elif path == "/v1/matches/join":
                    result = broker.join(aid, payload)
                elif path == "/v1/matches/release":
                    result = broker.release(aid, payload)
                else:
                    self._json(HTTPStatus.NOT_FOUND, {"error": "not-found"})
                    return
            self._json(HTTPStatus.OK, result)
        except Exception as error:
            self._error(error)

    def _authorize(self) -> tuple[dict[str, str], str]:
        authorization = self.headers.get("Authorization", "")
        if not authorization.startswith("Bearer "):
            raise PermissionError("缺少登录令牌")
        token = authorization[7:].strip()
        return self.server.broker.store.authenticate(token), token

    def _payload(self) -> dict[str, Any]:
        try:
            length = int(self.headers.get("Content-Length") or 0)
        except ValueError as error:
            raise ValueError("请求内容大小无效") from error
        if length < 0 or length > MAXIMUM_BODY_BYTES:
            raise ValueError("请求内容大小无效")
        if length == 0:
            return {}
        payload = json.loads(self.rfile.read(length))
        if not isinstance(payload, dict):
            raise ValueError("请求内容须为 JSON 对象")
        return payload

    def _error(self, error: Exception) -> None:
        if isinstance(error, RateLimited):
            status = HTTPStatus.TOO_MANY_REQUESTS
        elif isinstance(error, PermissionError):
            status = HTTPStatus.FORBIDDEN
        elif isinstance(error, LookupError):
            status = HTTPStatus.NOT_FOUND
        elif isinstance(error, (ValueError, json.JSONDecodeError)):
            status = HTTPStatus.BAD_REQUEST
        elif isinstance(error, subprocess.TimeoutExpired):
            status = HTTPStatus.GATEWAY_TIMEOUT
        elif isinstance(error, fps_control_store.ConflictError):
            status = HTTPStatus.CONFLICT
        else:
            status = HTTPStatus.INTERNAL_SERVER_ERROR
        message = str(error) if status < 500 else "服务暂时不可用"
        self._json(status, {"error": message})

    def log_message(self, format: str, *args: Any) -> None:
        # Do not log request targets, bodies or credentials.
        status = str(args[1]) if len(args) > 1 else "?"
        sys.stderr.write(f"{self.client_address[0]} {self.command} {status}\n")

    def _json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
        rendered = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(int(status))
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(rendered)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(rendered)


class BrokerServer(ThreadingHTTPServer):
    daemon_threads = True
    maximum_connections = 32

    def __init__(self, address: tuple[str, int], broker: MatchBroker) -> None:
        super().__init__(address, BrokerHandler)
        self.broker = broker
        self.tls_context: ssl.SSLContext | None = None
        self.connection_slots = threading.BoundedSemaphore(self.maximum_connections)

    def process_request(self, request: Any, client_address: tuple[str, int]) -> None:
        if not self.connection_slots.acquire(blocking=False):
            request.close()
            return
        try:
            super().process_request(request, client_address)
        except Exception:
            self.connection_slots.release()
            raise

    def process_request_thread(self, request: Any, client_address: tuple[str, int]) -> None:
        try:
            request.settimeout(8)
            context = self.tls_context
            if context is None:
                raise RuntimeError("TLS 未配置")
            with context.wrap_socket(request, server_side=True) as secure:
                super().process_request_thread(secure, client_address)
        except (OSError, ssl.SSLError):
            request.close()
        finally:
            self.connection_slots.release()


def parser() -> argparse.ArgumentParser:
    result = argparse.ArgumentParser()
    result.add_argument("--listen", default="0.0.0.0")
    result.add_argument("--port", type=int, default=18443)
    result.add_argument("--cert", type=pathlib.Path, required=True)
    result.add_argument("--key", type=pathlib.Path, required=True)
    result.add_argument("--database", type=pathlib.Path,
                        default=pathlib.Path("/srv/fps/data/fps-control.sqlite3"))
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
    server.tls_context = context
    server.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
