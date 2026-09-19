#!/usr/bin/env python3
"""Start, inspect and stop one remotely reachable FPS dedicated match.

The signing secret is read only from FPS_SERVER_AUTH_SECRET. It is inherited by
the supervised Unity process but is never written to the state file or argv.
"""

from __future__ import annotations

import argparse
import base64
import hashlib
import hmac
import json
import os
import pathlib
import re
import secrets
import signal
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone
from typing import Any


SCHEMA = "fps-remote-match-v1"
SECRET_ENV = "FPS_SERVER_AUTH_SECRET"
IDENTIFIER = re.compile(r"^[A-Za-z0-9._-]{1,64}$")


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def iso(value: datetime | None = None) -> str:
    return (value or utc_now()).isoformat().replace("+00:00", "Z")


def b64(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")


def compatibility_token(application: str, protocol: str, content: str) -> str:
    return "|".join(("fps2", b64(application.encode()),
                     b64(protocol.encode()), b64(content.encode())))


def issue_ticket(secret: str, account: str, compatibility: str,
                 match_id: str, issued: int, lifetime: int = 600) -> str:
    nonce = secrets.token_hex(16)
    payload = "\n".join((
        b64(account.encode()),
        b64(compatibility.encode()),
        nonce,
        str(issued),
        str(issued + lifetime),
        b64(match_id.encode()),
    )).encode()
    signed = "fps1." + b64(payload)
    signature = hmac.new(secret.encode(), signed.encode(),
                         hashlib.sha256).digest()
    return signed + "." + b64(signature)


def read_json(path: pathlib.Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def write_state(path: pathlib.Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2) +
                         "\n", encoding="utf-8")
    os.chmod(temporary, 0o600)
    temporary.replace(path)


def executable(artifact: pathlib.Path) -> pathlib.Path:
    artifact = artifact.resolve()
    if artifact.is_file() and os.access(artifact, os.X_OK):
        return artifact
    macos = artifact / "Contents" / "MacOS"
    if macos.is_dir():
        candidates = sorted(path for path in macos.iterdir()
                            if path.is_file() and os.access(path, os.X_OK))
        if candidates:
            return candidates[0]
    raise FileNotFoundError(f"找不到可执行服务器产物：{artifact}")


def validate_identifier(value: str, label: str) -> str:
    normalized = (value or "").strip()
    if not IDENTIFIER.fullmatch(normalized):
        raise ValueError(f"{label} 必须为 1—64 个字母、数字、点、短横线或下划线")
    return normalized


def require_secret() -> str:
    value = os.environ.get(SECRET_ENV, "")
    if len(value.encode()) < 32:
        raise ValueError(f"环境变量 {SECRET_ENV} 必须至少包含 32 字节")
    return value


def process_is_alive(pid: Any) -> bool:
    """Return whether a recorded process still exists.

    Lifecycle JSON survives machine and service restarts, so it cannot be used
    as the sole source of truth for the single-match guard.
    """
    try:
        normalized = int(pid or 0)
    except (TypeError, ValueError):
        return False
    if normalized <= 0:
        return False
    try:
        os.kill(normalized, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def state_has_live_process(state: dict[str, Any]) -> bool:
    return any(process_is_alive(state.get(key)) for key in (
        "serverPid", "launcherPid", "processGroupId"))


def server_command(args: argparse.Namespace, binary: pathlib.Path,
                   diagnostics: pathlib.Path) -> list[str]:
    return [
        str(binary), "-batchmode", "-nographics", "-disable-audio",
        "-fps-server",
        "-server-map", "CityNew",
        "-server-port", str(args.port),
        "-server-match", args.match_id,
        "-server-max-players", str(args.maximum_players),
        "-server-seed", str(args.seed),
        "-server-version", args.application_version,
        "-server-protocol-version", args.protocol_version,
        "-server-content-version", args.content_version,
        "-server-tick-rate", str(args.tick_rate),
        "-server-idle-timeout", str(args.idle_timeout),
        "-server-diagnostics", str(diagnostics),
    ]


def monitor(state_path: pathlib.Path) -> int:
    state = read_json(state_path)
    command = state.get("command") or []
    log_path = pathlib.Path(state.get("logPath", ""))
    diagnostics_path = pathlib.Path(state.get("diagnosticsPath", ""))
    if not command or not str(log_path):
        return 2
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = [*command, "-logFile", str(log_path)]
    process = subprocess.Popen(
        command,
        cwd=str(pathlib.Path(command[0]).parent),
        env=os.environ.copy(),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        start_new_session=True,
    )
    state.update({
        "serverPid": process.pid,
        "processGroupId": process.pid,
        "lifecycleStatus": "starting",
        "startedAtUtc": iso(),
    })
    write_state(state_path, state)

    ready_recorded = False
    while process.poll() is None:
        diagnostics = read_json(diagnostics_path)
        if diagnostics.get("status") == "ready" and not ready_recorded:
            state = read_json(state_path) or state
            state["lifecycleStatus"] = "ready"
            state["readyAtUtc"] = iso()
            write_state(state_path, state)
            ready_recorded = True
        time.sleep(0.2)

    exit_code = process.returncode
    diagnostics = read_json(diagnostics_path)
    state = read_json(state_path) or state
    stop_requested = bool(state.get("stopRequested"))
    diagnostic_status = diagnostics.get("status", "")
    diagnostic_reason = diagnostics.get("reason", "")
    if stop_requested:
        lifecycle = "stopped"
        reason = state.get("stopReason") or "operator-request"
    elif diagnostic_reason == "idle-timeout":
        lifecycle = "recycled"
        reason = "idle-timeout"
    elif exit_code == 0 and diagnostic_status in ("stopped", "not-ready"):
        lifecycle = "stopped"
        reason = diagnostic_reason or "normal-shutdown"
    else:
        lifecycle = "crashed"
        reason = diagnostic_reason or f"process-exit-{exit_code}"
    state.update({
        "lifecycleStatus": lifecycle,
        "reason": reason,
        "exitCode": exit_code,
        "endedAtUtc": iso(),
    })
    state.pop("command", None)
    write_state(state_path, state)
    return 0


def start(args: argparse.Namespace) -> int:
    secret = require_secret()
    binary = executable(args.binary)
    args.match_id = validate_identifier(args.match_id, "战局 ID")
    args.application_version = validate_identifier(
        args.application_version, "应用版本")
    args.protocol_version = validate_identifier(args.protocol_version,
                                                  "协议版本")
    args.content_version = validate_identifier(args.content_version,
                                                "内容版本")
    accounts = [validate_identifier(value, "账号 ID")
                for value in args.account]
    if len(set(accounts)) != len(accounts):
        raise ValueError("账号 ID 不能重复")
    if not accounts:
        raise ValueError("至少需要一个 --account 才能签发连接票据")

    match_root = args.state_root.resolve() / args.match_id
    state_path = match_root / "server-state.json"
    diagnostics_path = match_root / "server-diagnostics.json"
    log_path = match_root / "server.log"
    existing = read_json(state_path)
    if existing.get("lifecycleStatus") in ("launching", "starting", "ready"):
        if state_has_live_process(existing):
            raise ValueError("同名战局仍在运行，不能重复启动")
        existing.update({
            "lifecycleStatus": "stopped",
            "reason": "stale-state-recovered",
            "endedAtUtc": iso(),
        })
        existing.pop("command", None)
        write_state(state_path, existing)
    command = server_command(args, binary, diagnostics_path)
    state = {
        "schemaVersion": SCHEMA,
        "matchId": args.match_id,
        "publicHost": args.public_host,
        "port": args.port,
        "applicationVersion": args.application_version,
        "protocolVersion": args.protocol_version,
        "contentVersion": args.content_version,
        "maximumPlayers": args.maximum_players,
        "diagnosticsPath": str(diagnostics_path),
        "logPath": str(log_path),
        "lifecycleStatus": "launching",
        "requestedAtUtc": iso(),
        "command": command,
    }
    write_state(state_path, state)
    launcher = subprocess.Popen(
        [sys.executable, str(pathlib.Path(__file__).resolve()), "monitor",
         "--state", str(state_path)],
        env=os.environ.copy(),
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        start_new_session=True,
    )
    state = read_json(state_path) or state
    state["launcherPid"] = launcher.pid
    write_state(state_path, state)

    deadline = time.monotonic() + args.startup_timeout
    current: dict[str, Any] = state
    while time.monotonic() < deadline:
        current = read_json(state_path)
        lifecycle = current.get("lifecycleStatus")
        if lifecycle == "ready":
            break
        if lifecycle in ("crashed", "stopped", "recycled"):
            raise RuntimeError("服务器启动失败：" +
                               str(current.get("reason", lifecycle)))
        time.sleep(0.2)
    else:
        stop_process(current, "startup-timeout")
        raise TimeoutError("服务器未在时限内进入 ready")

    now = int(time.time())
    compatibility = compatibility_token(args.application_version,
                                         args.protocol_version,
                                         args.content_version)
    expiry = utc_now() + timedelta(seconds=args.ticket_lifetime)
    allocation = {
        "schemaVersion": SCHEMA,
        "host": args.public_host,
        "port": args.port,
        "matchId": args.match_id,
        "applicationVersion": args.application_version,
        "protocolVersion": args.protocol_version,
        "contentVersion": args.content_version,
        "maximumPlayers": args.maximum_players,
        "expiresAtUtc": iso(expiry),
        "players": [
            {
                "accountId": account,
                "connectionTicket": issue_ticket(
                    secret, account, compatibility, args.match_id, now,
                    args.ticket_lifetime),
                "reconnectTicket": issue_ticket(
                    secret, account, compatibility, args.match_id, now,
                    args.ticket_lifetime),
            }
            for account in accounts
        ],
        "statePath": str(state_path),
    }
    rendered = json.dumps(allocation, ensure_ascii=False, indent=2) + "\n"
    if args.allocation_output:
        destination = args.allocation_output.resolve()
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(rendered, encoding="utf-8")
        os.chmod(destination, 0o600)
        print(json.dumps({
            "schemaVersion": SCHEMA,
            "matchId": args.match_id,
            "host": args.public_host,
            "port": args.port,
            "playerCount": len(accounts),
            "allocationPath": str(destination),
        }, ensure_ascii=False, indent=2))
    else:
        print(rendered, end="")
    return 0


def stop_process(state: dict[str, Any], reason: str) -> None:
    state_path = pathlib.Path(state.get("statePath") or "")
    if not str(state_path):
        diagnostics = pathlib.Path(state.get("diagnosticsPath", ""))
        state_path = diagnostics.parent / "server-state.json"
    state["stopRequested"] = True
    state["stopReason"] = reason
    write_state(state_path, state)
    process_group = int(state.get("processGroupId") or 0)
    if process_group > 0:
        try:
            os.killpg(process_group, signal.SIGTERM)
        except ProcessLookupError:
            pass


def stop(args: argparse.Namespace) -> int:
    state_path = args.state.resolve()
    state = read_json(state_path)
    if not state:
        raise ValueError(f"找不到战局状态：{state_path}")
    state["statePath"] = str(state_path)
    stop_process(state, args.reason)
    print(json.dumps({"matchId": state.get("matchId"),
                      "stopRequested": True,
                      "reason": args.reason}, ensure_ascii=False, indent=2))
    return 0


def status(args: argparse.Namespace) -> int:
    state_path = args.state.resolve()
    state = read_json(state_path)
    if not state:
        raise ValueError(f"找不到战局状态：{state_path}")
    state.pop("command", None)
    diagnostics = read_json(pathlib.Path(state.get("diagnosticsPath", "")))
    payload = {"state": state, "diagnostics": diagnostics}
    print(json.dumps(payload, ensure_ascii=False, indent=2))
    return 0


def lifecycle_exit_code(lifecycle: str) -> int | None:
    if lifecycle in ("launching", "starting", "ready"):
        return None
    if lifecycle in ("stopped", "recycled"):
        return 0
    if lifecycle == "crashed":
        return 1
    return None


def run(args: argparse.Namespace) -> int:
    """Start one match and remain in the foreground for a service manager."""
    result = start(args)
    if result != 0:
        return result
    state_path = args.state_root.resolve() / args.match_id / "server-state.json"
    while True:
        state = read_json(state_path)
        exit_code = lifecycle_exit_code(str(state.get("lifecycleStatus", "")))
        if exit_code is not None:
            return exit_code
        time.sleep(0.5)


def add_start_arguments(command: argparse.ArgumentParser) -> None:
    command.add_argument("--binary", type=pathlib.Path, required=True)
    command.add_argument("--public-host", required=True)
    command.add_argument("--port", type=int, default=7777)
    command.add_argument("--match-id", default="remote-" +
                         utc_now().strftime("%Y%m%dT%H%M%SZ"))
    command.add_argument("--account", action="append", default=[])
    command.add_argument("--maximum-players", type=int, default=2)
    command.add_argument("--seed", type=int, default=18018)
    command.add_argument("--application-version", default="development")
    command.add_argument("--protocol-version", default="1")
    command.add_argument("--content-version", default="citynew-v1")
    command.add_argument("--tick-rate", type=int, default=60)
    command.add_argument("--idle-timeout", type=int, default=120)
    command.add_argument("--ticket-lifetime", type=int, default=600)
    command.add_argument("--startup-timeout", type=int, default=90)
    command.add_argument("--state-root", type=pathlib.Path,
                         default=pathlib.Path(
                             "artifacts/networking/issue101/runtime"))
    command.add_argument("--allocation-output", type=pathlib.Path)


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser()
    commands = root.add_subparsers(dest="command", required=True)
    start_parser = commands.add_parser("start")
    add_start_arguments(start_parser)
    start_parser.set_defaults(handler=start)

    run_parser = commands.add_parser(
        "run", help="启动战局并保持前台运行，供 systemd 等服务管理器监管")
    add_start_arguments(run_parser)
    run_parser.set_defaults(handler=run)

    monitor_parser = commands.add_parser("monitor", help=argparse.SUPPRESS)
    monitor_parser.add_argument("--state", type=pathlib.Path, required=True)
    monitor_parser.set_defaults(handler=lambda args: monitor(args.state))

    stop_parser = commands.add_parser("stop")
    stop_parser.add_argument("--state", type=pathlib.Path, required=True)
    stop_parser.add_argument("--reason", default="operator-request")
    stop_parser.set_defaults(handler=stop)

    status_parser = commands.add_parser("status")
    status_parser.add_argument("--state", type=pathlib.Path, required=True)
    status_parser.set_defaults(handler=status)
    return root


def main() -> int:
    args = parser().parse_args()
    try:
        return int(args.handler(args))
    except (OSError, ValueError, RuntimeError, TimeoutError) as error:
        print(json.dumps({"error": str(error)}, ensure_ascii=False),
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
