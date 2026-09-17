#!/usr/bin/env python3
"""Run two independent clients against an Issue101 remote allocation."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone
from typing import Any


def read_json(path: pathlib.Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def executable(artifact: pathlib.Path) -> pathlib.Path:
    if artifact.is_file() and os.access(artifact, os.X_OK):
        return artifact.resolve()
    macos = artifact / "Contents" / "MacOS"
    if macos.is_dir():
        candidates = sorted(path for path in macos.iterdir()
                            if path.is_file() and os.access(path, os.X_OK))
        if candidates:
            return candidates[0].resolve()
    raise FileNotFoundError(f"找不到客户端构建产物：{artifact}")


def stop(process: subprocess.Popen[Any]) -> None:
    if process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
        process.wait(timeout=8)
    except (ProcessLookupError, subprocess.TimeoutExpired):
        if process.poll() is None:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait(timeout=3)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--allocation", type=pathlib.Path, required=True)
    parser.add_argument("--client", type=pathlib.Path)
    parser.add_argument("--build-manifest", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path,
                        default=pathlib.Path("artifacts/networking/issue101/remote-acceptance"))
    parser.add_argument("--timeout", type=int, default=300)
    args = parser.parse_args()

    allocation = read_json(args.allocation.resolve())
    if allocation.get("schemaVersion") != "fps-remote-match-v1":
        print("[FAIL] allocation schema 无效", file=sys.stderr)
        return 2
    players = allocation.get("players") or []
    if len(players) < 2:
        print("[FAIL] allocation 至少需要两名玩家票据", file=sys.stderr)
        return 2
    client_path = args.client
    if client_path is None and args.build_manifest:
        manifest = read_json(args.build_manifest.resolve())
        value = manifest.get("clientOutput")
        client_path = pathlib.Path(value) if value else None
    if client_path is None:
        print("[FAIL] 缺少 --client 或 --build-manifest", file=sys.stderr)
        return 2
    try:
        binary = executable(client_path)
    except FileNotFoundError as error:
        print(f"[FAIL] {error}", file=sys.stderr)
        return 2

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    run_id = "issue101-remote-" + datetime.now(timezone.utc).strftime(
        "%Y%m%dT%H%M%SZ")
    processes: list[tuple[str, subprocess.Popen[Any], pathlib.Path]] = []
    credential_files: list[pathlib.Path] = []
    try:
        for index, role in enumerate(("client-a", "client-b")):
            player = players[index]
            directory = output / role
            directory.mkdir(parents=True, exist_ok=True)
            connection_ticket = directory / "connection.ticket"
            reconnect_ticket = directory / "reconnect.ticket"
            connection_ticket.write_text(str(player["connectionTicket"]),
                                         encoding="utf-8")
            reconnect_ticket.write_text(str(player["reconnectTicket"]),
                                        encoding="utf-8")
            os.chmod(connection_ticket, 0o600)
            os.chmod(reconnect_ticket, 0o600)
            credential_files.extend((connection_ticket, reconnect_ticket))
            command = [
                str(binary), "-batchmode", "-nographics", "-disable-audio",
                "-issue100-acceptance",
                "-issue100-run-id", run_id,
                "-issue100-role", role,
                "-issue100-scenario", "rtt-000-loss-00",
                "-issue100-output", str(directory),
                "-issue100-timeout", str(args.timeout),
                "-issue100-appearance", "character.quaternius.male-light",
                "-issue65-role", "client",
                "-issue65-address", str(allocation["host"]),
                "-issue65-port", str(allocation["port"]),
                "-issue86-account", str(player["accountId"]),
                "-issue86-ticket-file", str(connection_ticket),
                "-issue101-reconnect-ticket-file", str(reconnect_ticket),
                "-issue86-version", str(allocation["applicationVersion"]),
                "-issue101-protocol-version",
                str(allocation["protocolVersion"]),
                "-issue101-content-version",
                str(allocation["contentVersion"]),
                "-issue101-server-version",
                str(allocation["applicationVersion"]),
                "-issue101-server-protocol-version",
                str(allocation["protocolVersion"]),
                "-issue101-server-content-version",
                str(allocation["contentVersion"]),
                "-issue99-match", str(allocation["matchId"]),
                "-logFile", str(directory / "player.log"),
            ]
            process = subprocess.Popen(
                command, cwd=str(binary.parent),
                stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL, start_new_session=True)
            processes.append((role, process, directory))

        deadline = time.monotonic() + args.timeout + 45
        while time.monotonic() < deadline:
            if all(process.poll() is not None
                   for _, process, _ in processes):
                break
            if any(process.poll() not in (None, 0)
                   for _, process, _ in processes):
                break
            time.sleep(0.25)
    finally:
        for _, process, _ in processes:
            stop(process)
        for credential_file in credential_files:
            try:
                credential_file.unlink()
            except FileNotFoundError:
                pass

    records: list[dict[str, Any]] = []
    errors: list[str] = []
    for role, process, directory in processes:
        snapshot = read_json(directory / "latest-snapshot.json")
        process_record = read_json(directory / "process.json")
        status = process_record.get("status", "")
        if process.returncode != 0:
            errors.append(f"{role} 退出码为 {process.returncode}")
        if status != "passed":
            errors.append(f"{role} 没有完成完整流程：{status or 'missing'}")
        if snapshot.get("missionPhase") != "Victory":
            errors.append(f"{role} 最终任务阶段不是 Victory")
        records.append({
            "role": role,
            "accountId": players[len(records)].get("accountId", ""),
            "exitCode": process.returncode,
            "status": status,
            "missionPhase": snapshot.get("missionPhase", ""),
            "serverTick": snapshot.get("serverTick", 0),
            "logPath": str((directory / "player.log").resolve()),
        })

    report = {
        "schemaVersion": "issue101-remote-acceptance-v1",
        "runId": run_id,
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        "topology": "remote-dedicated-server-plus-two-independent-clients",
        "endpoint": {
            "host": allocation.get("host"),
            "port": allocation.get("port"),
            "matchId": allocation.get("matchId"),
        },
        "compatibility": {
            "applicationVersion": allocation.get("applicationVersion"),
            "protocolVersion": allocation.get("protocolVersion"),
            "contentVersion": allocation.get("contentVersion"),
        },
        "clients": records,
        "outcome": "Pass" if not errors else "Fail",
        "errors": errors,
    }
    report_path = output / "remote-acceptance-report.json"
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2) +
                           "\n", encoding="utf-8")
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return 0 if not errors else 1


if __name__ == "__main__":
    raise SystemExit(main())
