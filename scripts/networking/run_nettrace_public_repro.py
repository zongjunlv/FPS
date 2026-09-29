#!/usr/bin/env python3
"""Run a pinned-HTTPS public broker match with two nettrace clients.

This script performs real broker operations only when invoked as a program.
It leaves no password or ticket in its persistent report. The temporary
allocation exists only while the existing two-client runner is executing.
"""

from __future__ import annotations

import argparse
import hashlib
import http.client
import json
import os
import pathlib
import re
import secrets
import shlex
import ssl
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from typing import Any
from urllib.parse import urlsplit
from issue101_server_manager import BATTLE_PROTOCOL_VERSION


PROJECT = pathlib.Path(__file__).resolve().parents[2]
SETTINGS_PATH = PROJECT / "Assets/Resources/Networking/CoopDedicatedServerSettings.json"
RUNNER = pathlib.Path(__file__).with_name("run_issue101_remote_clients.py")
ANALYZER = pathlib.Path(__file__).with_name("analyze_nettrace.py")
VERSION = ("0.1.0", BATTLE_PROTOCOL_VERSION, "citynew-v1")
APPEARANCE = "character.quaternius.male-light"
MAX_RESPONSE_BYTES = 1024 * 1024
SSH_LOG_ROOT = "/srv/fps/data/broker/matches"


class HarnessError(RuntimeError):
    """An error whose message contains no credentials or tickets."""


@dataclass(frozen=True)
class Settings:
    host: str
    port: int
    fingerprint: str
    application_version: str
    protocol_version: str
    content_version: str
    timeout: int

    @property
    def compatibility(self) -> dict[str, str]:
        return {
            "applicationVersion": self.application_version,
            "protocolVersion": self.protocol_version,
            "contentVersion": self.content_version,
        }


@dataclass(frozen=True)
class Account:
    account_id: str
    access_token: str = field(repr=False)


def load_settings(path: pathlib.Path = SETTINGS_PATH) -> Settings:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        parsed = urlsplit(value["brokerUrl"])
        host = parsed.hostname
        port = 443 if parsed.port is None else parsed.port
        fingerprint = value["pinnedCertificateSha256"].lower()
        versions = tuple(value[name] for name in (
            "applicationVersion", "protocolVersion", "contentVersion"))
        timeout = int(value.get("requestTimeoutSeconds", 100))
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as error:
        raise HarnessError("无法读取完整的 broker 配置") from error
    if (parsed.scheme != "https" or not host or parsed.username is not None or
            parsed.password is not None or parsed.path not in ("", "/") or
            parsed.query or parsed.fragment or not 1 <= port <= 65535):
        raise HarnessError("brokerUrl 必须是没有路径和凭据的 HTTPS 地址")
    if not re.fullmatch(r"[0-9a-f]{64}", fingerprint):
        raise HarnessError("必须配置 64 位 SHA-256 证书指纹")
    if versions != VERSION:
        raise HarnessError("broker 配置版本不是 " + " / ".join(VERSION))
    if not 1 <= timeout <= 120:
        raise HarnessError("broker 请求超时配置无效")
    return Settings(host, port, fingerprint, *versions, timeout)


class PinnedHTTPSConnection(http.client.HTTPSConnection):
    """Verify the leaf certificate on every direct TLS connection."""

    def __init__(self, settings: Settings) -> None:
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        context.minimum_version = ssl.TLSVersion.TLSv1_2
        # This self-hosted broker may use a private CA. A configured pin is
        # mandatory, so the exact DER certificate replaces CA trust here.
        context.check_hostname = False
        context.verify_mode = ssl.CERT_NONE
        super().__init__(settings.host, settings.port, timeout=settings.timeout,
                         context=context)
        self.fingerprint = settings.fingerprint

    def connect(self) -> None:
        super().connect()
        try:
            certificate = self.sock.getpeercert(binary_form=True) if self.sock else None
            if not certificate or hashlib.sha256(certificate).hexdigest() != self.fingerprint:
                raise HarnessError("broker TLS 证书指纹不匹配；未发送请求")
        except Exception:
            self.close()
            raise


class Broker:
    def __init__(self, settings: Settings) -> None:
        self.settings = settings

    def post(self, path: str, payload: dict[str, Any],
             token: str | None = None) -> dict[str, Any]:
        if not path.startswith("/") or "?" in path or "#" in path:
            raise HarnessError("broker 请求路径无效")
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        headers = {"Content-Type": "application/json", "Accept": "application/json"}
        if token:
            headers["Authorization"] = "Bearer " + token
        connection = PinnedHTTPSConnection(self.settings)
        try:
            # connect() pins the certificate before any request bytes, and
            # its override also covers an implicit reconnect from request().
            connection.connect()
            connection.request("POST", path, body=data, headers=headers)
            response = connection.getresponse()
            body = response.read(MAX_RESPONSE_BYTES + 1)
            if len(body) > MAX_RESPONSE_BYTES:
                raise HarnessError("broker 响应过大")
            if response.status != 200:
                raise HarnessError(f"broker {path} 返回 HTTP {response.status}")
            value = json.loads(body)
            if not isinstance(value, dict):
                raise HarnessError("broker 响应不是 JSON 对象")
            return value
        except HarnessError:
            raise
        except (OSError, ssl.SSLError, http.client.HTTPException,
                json.JSONDecodeError) as error:
            raise HarnessError(f"broker {path} 请求失败（{type(error).__name__}）") from error
        finally:
            connection.close()


def require_string(value: dict[str, Any], name: str) -> str:
    result = value.get(name)
    if not isinstance(result, str) or not result:
        raise HarnessError(f"broker 响应缺少 {name}")
    return result


def register(broker: Broker) -> Account:
    username = "nt" + secrets.token_hex(7)
    # The service requires 8–30 characters with at least one letter and digit.
    password = "A1" + secrets.token_urlsafe(18)
    response = broker.post("/v1/auth/register", {
        "username": username, "password": password,
    })
    return Account(require_string(response, "accountId"),
                   require_string(response, "accessToken"))


def validate_connection(value: dict[str, Any], settings: Settings) -> dict[str, Any]:
    connection = value.get("connection")
    if not isinstance(connection, dict):
        raise HarnessError("broker 未返回连接信息")
    if (connection.get("schemaVersion") != "fps-remote-match-v1" or
            any(connection.get(name) != expected
                for name, expected in settings.compatibility.items()) or
            not isinstance(connection.get("host"), str) or
            not connection["host"] or
            type(connection.get("port")) is not int or
            not 1 <= connection["port"] <= 65535 or
            not re.fullmatch(r"[A-Za-z0-9._-]{1,64}",
                             str(connection.get("matchId", "")))):
        raise HarnessError("broker 连接信息与本地配置不兼容")
    return connection


def obtain_allocation(broker: Broker, settings: Settings,
                      host: Account, guest: Account,
                      room: dict[str, Any]) -> dict[str, Any]:
    room_id = require_string(room, "id")
    roster = room.get("players")
    if (not isinstance(roster, list) or len(roster) != 2 or
            [p.get("accountId") for p in roster] !=
            [host.account_id, guest.account_id] or
            not all(p.get("ready") is True for p in roster)):
        raise HarnessError("房间尚未由两名测试账号准备完成")
    players = [{"accountId": p["accountId"],
                "appearanceId": p["appearanceId"]} for p in roster]
    seed = room.get("seed")
    if type(seed) is not int:
        raise HarnessError("房间没有有效 seed")
    allocated = broker.post("/v1/matches/allocate", {
        "sessionId": room_id, "seed": seed, "players": players,
        **settings.compatibility,
    }, host.access_token)
    connection = validate_connection(allocated, settings)
    match_id = require_string(connection, "matchId")
    join_payload = {"sessionId": room_id, "matchId": match_id,
                    **settings.compatibility}
    # allocate gives only the host's first ticket. Each join signs a fresh
    # ticket; the second ticket for each account is used for reconnect.
    host_reconnect = broker.post("/v1/matches/join", join_payload,
                                 host.access_token)
    if validate_connection(host_reconnect, settings) != connection:
        raise HarnessError("房主重连票据的连接信息不一致")
    tickets = [(host.account_id, require_string(allocated, "connectionTicket"),
                require_string(host_reconnect, "connectionTicket"))]
    guest_connection = broker.post("/v1/matches/join", join_payload,
                                   guest.access_token)
    if validate_connection(guest_connection, settings) != connection:
        raise HarnessError("两名玩家获得的连接信息不一致")
    guest_reconnect = broker.post("/v1/matches/join", join_payload,
                                  guest.access_token)
    if validate_connection(guest_reconnect, settings) != connection:
        raise HarnessError("访客重连票据的连接信息不一致")
    tickets.append((guest.account_id,
                    require_string(guest_connection, "connectionTicket"),
                    require_string(guest_reconnect, "connectionTicket")))
    if len({ticket for _, first, second in tickets
            for ticket in (first, second)}) != 4:
        raise HarnessError("broker 没有签发四张独立票据")
    return {
        "schemaVersion": connection["schemaVersion"],
        "host": connection["host"], "port": connection["port"],
        "matchId": match_id, **settings.compatibility,
        "maximumPlayers": 2,
        "players": [{"accountId": account_id,
                     "connectionTicket": first,
                     "reconnectTicket": second}
                    for account_id, first, second in tickets],
    }


def write_private_json(path: pathlib.Path, value: dict[str, Any]) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "w", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False)
            stream.write("\n")
    except Exception:
        path.unlink(missing_ok=True)
        raise


def subprocess_environment() -> dict[str, str]:
    environment = {key: value for key, value in os.environ.items()
                   if key.lower() not in ("http_proxy", "https_proxy",
                                          "all_proxy", "no_proxy")}
    environment["FPS_NETTRACE"] = "1"
    return environment


def checked_host_override(value: str | None) -> str:
    if value in (None, ""):
        return ""
    if value != "127.0.0.1":
        raise HarnessError("--host-override 仅允许 127.0.0.1")
    return value


def run_clients(args: argparse.Namespace, allocation_path: pathlib.Path,
                run_dir: pathlib.Path) -> int:
    host_override = checked_host_override(args.host_override)
    command = [sys.executable, str(RUNNER), "--allocation", str(allocation_path),
               "--output", str(run_dir), "--timeout", str(args.timeout)]
    if args.client:
        command.extend(("--client", str(args.client)))
    else:
        command.extend(("--build-manifest", str(args.build_manifest)))
    if host_override:
        command.extend(("--host-override", host_override))
    try:
        completed = subprocess.run(command, stdin=subprocess.DEVNULL,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   text=True, check=False,
                                   env=subprocess_environment())
    except OSError as error:
        raise HarnessError(f"无法启动双客户端验收（{type(error).__name__}）") from error
    return completed.returncode


def pull_server_log(target: str, match_id: str,
                    destination: pathlib.Path) -> None:
    if not re.fullmatch(r"(?:[A-Za-z0-9_.-]+@)?[A-Za-z0-9_.-]+", target) or target.startswith("-"):
        raise HarnessError("SSH 目标格式无效")
    if not re.fullmatch(r"[A-Za-z0-9._-]{1,64}", match_id):
        raise HarnessError("战局 ID 无法安全用于 SSH 日志路径")
    remote_path = f"{SSH_LOG_ROOT}/{match_id}/server.log"
    command = ["ssh", "-oBatchMode=yes", "-oConnectTimeout=10",
               "-oStrictHostKeyChecking=yes", "--", target,
               "sudo -n cat -- " + shlex.quote(remote_path)]
    descriptor = os.open(destination, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    try:
        with os.fdopen(descriptor, "wb") as stream:
            completed = subprocess.run(command, stdin=subprocess.DEVNULL,
                                       stdout=stream, stderr=subprocess.PIPE,
                                       timeout=60, check=False)
        if completed.returncode != 0:
            raise HarnessError("SSH 无法读取同场 server.log")
        if destination.stat().st_size > 100 * 1024 * 1024:
            raise HarnessError("server.log 超过 100 MiB")
    except (OSError, subprocess.TimeoutExpired) as error:
        destination.unlink(missing_ok=True)
        raise HarnessError(f"拉取 server.log 失败（{type(error).__name__}）") from error
    except Exception:
        destination.unlink(missing_ok=True)
        raise


def analyze(run_dir: pathlib.Path, server_log: pathlib.Path | None) -> dict[str, str]:
    verdicts: dict[str, str] = {}
    for role in ("client-a", "client-b"):
        log_path = run_dir / role / "player.log"
        if not log_path.is_file():
            verdicts[role] = "missing-log"
            continue
        command = [sys.executable, str(ANALYZER), "--client-log", str(log_path)]
        if server_log:
            command.extend(("--server-log", str(server_log)))
        try:
            completed = subprocess.run(command, stdin=subprocess.DEVNULL,
                                       stdout=subprocess.PIPE,
                                       stderr=subprocess.PIPE, text=True,
                                       check=False)
            result = json.loads(completed.stdout)
            if not isinstance(result, dict) or result.get("verdict") not in (
                    "green", "red", "insufficient"):
                raise ValueError("invalid analyzer result")
        except (OSError, ValueError, json.JSONDecodeError) as error:
            raise HarnessError(f"{role} nettrace 分析失败（{type(error).__name__}）") from error
        (run_dir / f"{role}-nettrace.json").write_text(
            json.dumps(result, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8")
        verdicts[role] = str(result["verdict"])
    return verdicts


def run_workflow(args: argparse.Namespace, broker: Broker) -> tuple[int, pathlib.Path]:
    checked_host_override(args.host_override)
    run_dir = args.output.resolve() / ("nettrace-public-" + secrets.token_hex(6))
    original_umask = os.umask(0o077)
    try:
        run_dir.mkdir(parents=True, mode=0o700)
        temporary = tempfile.TemporaryDirectory(prefix="fps-nettrace-allocation-")
    except Exception:
        os.umask(original_umask)
        raise
    allocation_path = pathlib.Path(temporary.name) / "allocation.json"
    accounts: list[Account] = []
    room_id = ""
    match_id = ""
    client_exit: int | None = None
    verdicts: dict[str, str] = {}
    errors: list[str] = []
    cleanup_errors: list[str] = []
    try:
        host = register(broker)
        accounts.append(host)
        guest = register(broker)
        accounts.append(guest)
        room = broker.post("/v1/rooms", {
            "displayName": "nettrace-" + secrets.token_hex(4),
            "mapId": "CityNew", "seed": secrets.randbelow(2_147_483_648),
            "appearanceId": APPEARANCE,
        }, host.access_token)
        room_id = require_string(room, "id")
        broker.post("/v1/rooms/join", {
            "sessionId": room_id, "appearanceId": APPEARANCE,
        }, guest.access_token)
        broker.post(f"/v1/rooms/{room_id}/player", {"ready": True},
                    host.access_token)
        room = broker.post(f"/v1/rooms/{room_id}/player", {"ready": True},
                           guest.access_token)
        allocation = obtain_allocation(broker, broker.settings, host, guest, room)
        match_id = allocation["matchId"]
        write_private_json(allocation_path, allocation)
        client_exit = run_clients(args, allocation_path, run_dir)
        allocation_path.unlink(missing_ok=True)
        server_log: pathlib.Path | None = None
        if args.ssh_target:
            try:
                server_log = run_dir / "server.log"
                pull_server_log(args.ssh_target, match_id, server_log)
            except HarnessError as error:
                errors.append(str(error))
                server_log = None
        verdicts = analyze(run_dir, server_log)
        if client_exit != 0:
            errors.append(f"双客户端验收退出码 {client_exit}")
        if any(verdict != "green" for verdict in verdicts.values()):
            errors.append("nettrace 未全部达到 green")
    except Exception as error:
        errors.append(str(error) if isinstance(error, HarnessError)
                      else f"运行失败（{type(error).__name__}）")
    finally:
        try:
            allocation_path.unlink(missing_ok=True)
        except OSError as error:
            cleanup_errors.append(f"临时分配文件删除失败（{type(error).__name__}）")
        for role in ("client-a", "client-b"):
            for name in ("connection.ticket", "reconnect.ticket"):
                try:
                    (run_dir / role / name).unlink(missing_ok=True)
                except OSError as error:
                    cleanup_errors.append(f"{role} 票据删除失败（{type(error).__name__}）")
        if room_id and accounts:
            try:
                broker.post(f"/v1/rooms/{room_id}/leave", {},
                            accounts[0].access_token)
            except Exception as error:
                cleanup_errors.append(f"房主退出失败（{type(error).__name__}）")
        for index, account in enumerate(accounts):
            try:
                broker.post("/v1/auth/logout", {}, account.access_token)
            except Exception as error:
                cleanup_errors.append(f"账号 {index + 1} 注销失败（{type(error).__name__}）")
        try:
            temporary.cleanup()
        except OSError as error:
            cleanup_errors.append(f"临时目录删除失败（{type(error).__name__}）")
        finally:
            os.umask(original_umask)
    report = {
        "schemaVersion": "nettrace-public-repro-v1",
        "roomId": room_id, "matchId": match_id,
        "accountIds": [account.account_id for account in accounts],
        "clientExitCode": client_exit, "nettraceVerdicts": verdicts,
        "errors": errors, "cleanupErrors": cleanup_errors,
        "outcome": "Pass" if not errors and not cleanup_errors else "Fail",
    }
    report_path = run_dir / "public-repro-report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                           encoding="utf-8")
    return (0 if report["outcome"] == "Pass" else 1), report_path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--client", type=pathlib.Path, help="Unity 客户端可执行文件或 .app")
    source.add_argument("--build-manifest", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path,
                        default=PROJECT / "artifacts/networking/nettrace-public")
    parser.add_argument("--timeout", type=int, default=300)
    parser.add_argument("--ssh-target", help="可选，只读拉取同场 server.log；如 user@host")
    parser.add_argument("--host-override", default="",
                        help="仅允许 127.0.0.1，用于临时 SSH UDP 诊断中继；默认直连")
    args = parser.parse_args()
    if args.timeout < 1 or args.timeout > 540:
        parser.error("--timeout 必须为 1—540 秒（票据有效期为 600 秒）")
    try:
        settings = load_settings()
        code, report_path = run_workflow(args, Broker(settings))
    except HarnessError as error:
        print(f"[FAIL] {error}", file=sys.stderr)
        return 2
    print(f"{'[PASS]' if code == 0 else '[FAIL]'} 结果：{report_path}")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
