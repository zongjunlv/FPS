#!/usr/bin/env python3
"""Run Issue100 as one real dedicated server and two independent clients."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import secrets
import shutil
import signal
import subprocess
import sys
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any

from issue100_report_gate import SCHEMA_VERSION, to_markdown, validate


SCENARIOS = (
    ("rtt-000-loss-00", 0, 0),
    ("rtt-080-loss-00", 80, 0),
    ("rtt-150-loss-00", 150, 0),
    ("rtt-080-loss-05", 80, 500),
)


@dataclass
class ManagedProcess:
    role: str
    process: subprocess.Popen[Any]
    directory: pathlib.Path
    started_ms: int
    ended_ms: int = 0

    def finish_time(self) -> int:
        if not self.ended_ms:
            self.ended_ms = int(time.time() * 1000)
        return self.ended_ms


@dataclass
class VideoPipeline:
    process: subprocess.Popen[Any]
    fifo_path: pathlib.Path
    video_path: pathlib.Path
    log_path: pathlib.Path
    log_file: Any
    completed: bool = False


def utc_run_id() -> str:
    return datetime.now(timezone.utc).strftime("issue100-%Y%m%dT%H%M%SZ")


def read_json(path: pathlib.Path) -> dict[str, Any]:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def executable(artifact: pathlib.Path) -> pathlib.Path:
    if artifact.is_file() and os.access(artifact, os.X_OK):
        return artifact
    macos = artifact / "Contents" / "MacOS"
    if macos.is_dir():
        candidates = sorted(path for path in macos.iterdir()
                            if path.is_file() and os.access(path, os.X_OK))
        if candidates:
            return candidates[0]
    raise FileNotFoundError(f"找不到可执行构建产物：{artifact}")


def spawn(role: str, binary: pathlib.Path, arguments: list[str],
          directory: pathlib.Path, environment: dict[str, str]) -> ManagedProcess:
    directory.mkdir(parents=True, exist_ok=True)
    command = [str(binary), *arguments, "-logFile", str(directory / "player.log")]
    started = int(time.time() * 1000)
    process = subprocess.Popen(
        command,
        cwd=str(binary.parent),
        env=environment,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        start_new_session=True,
    )
    return ManagedProcess(role, process, directory, started)


def stop(managed: ManagedProcess, grace_seconds: float = 8.0) -> None:
    if managed.process.poll() is None:
        try:
            os.killpg(managed.process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            managed.process.wait(timeout=grace_seconds)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(managed.process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            managed.process.wait(timeout=3)
    managed.finish_time()


def start_video_pipeline(video_path: pathlib.Path) -> VideoPipeline:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("找不到 ffmpeg，无法生成连续演示录像")
    video_path.parent.mkdir(parents=True, exist_ok=True)
    fifo_path = video_path.parent / "issue100-bgra.rawpipe"
    if fifo_path.exists():
        fifo_path.unlink()
    os.mkfifo(fifo_path)
    log_path = video_path.parent / "ffmpeg.log"
    log_file = log_path.open("wb")
    process = subprocess.Popen(
        [
            ffmpeg, "-y",
            "-f", "rawvideo",
            "-pixel_format", "bgra",
            "-video_size", "1280x720",
            "-framerate", "30",
            "-i", str(fifo_path),
            "-an",
            # Unity AsyncGPUReadback returns the render target from the
            # bottom-left origin, while rawvideo assumes top-left scanlines.
            "-vf", "vflip",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "20",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            str(video_path),
        ],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=log_file,
        start_new_session=True,
    )
    return VideoPipeline(process, fifo_path, video_path, log_path, log_file)


def finish_video_pipeline(pipeline: VideoPipeline,
                          grace_seconds: float = 20.0) -> None:
    if pipeline.completed:
        return
    try:
        try:
            pipeline.process.wait(timeout=grace_seconds)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(pipeline.process.pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            try:
                pipeline.process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(pipeline.process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                pipeline.process.wait(timeout=3)
    finally:
        pipeline.log_file.close()
        if pipeline.fifo_path.exists():
            pipeline.fifo_path.unlink()
        pipeline.completed = True


def probe_video(video_path: pathlib.Path) -> tuple[dict[str, Any], list[str]]:
    ffprobe = shutil.which("ffprobe")
    errors: list[str] = []
    if not ffprobe:
        return {}, ["找不到 ffprobe，无法验证连续演示录像"]
    if not video_path.is_file() or video_path.stat().st_size <= 0:
        return {}, ["连续演示录像不存在或为空"]
    result = subprocess.run(
        [
            ffprobe, "-v", "error", "-show_streams", "-show_format",
            "-of", "json", str(video_path),
        ],
        text=True, capture_output=True, check=False,
    )
    if result.returncode != 0:
        return {}, ["ffprobe 无法解析连续演示录像"]
    try:
        payload = json.loads(result.stdout)
    except json.JSONDecodeError:
        return {}, ["ffprobe 返回了无效 JSON"]
    streams = payload.get("streams") or []
    videos = [stream for stream in streams
              if stream.get("codec_type") == "video"]
    audios = [stream for stream in streams
              if stream.get("codec_type") == "audio"]
    if len(videos) != 1:
        errors.append("连续演示录像必须恰好包含一条视频轨")
    if audios:
        errors.append("静默验收录像不得包含音轨")
    if videos:
        stream = videos[0]
        if stream.get("width") != 1280 or stream.get("height") != 720:
            errors.append("连续演示录像必须为 1280x720")
        rate = str(stream.get("avg_frame_rate") or "0/1")
        try:
            numerator, denominator = rate.split("/", 1)
            frames_per_second = float(numerator) / max(1.0, float(denominator))
        except (ValueError, ZeroDivisionError):
            frames_per_second = 0.0
        if not 29.0 <= frames_per_second <= 31.0:
            errors.append("连续演示录像平均帧率必须接近 30 FPS")
    try:
        duration = float((payload.get("format") or {}).get("duration", 0))
    except (TypeError, ValueError):
        duration = 0.0
    if duration < 3.0:
        errors.append("连续演示录像时长不足 3 秒")
    return payload, errors


def wait_server_ready(server: ManagedProcess, timeout: float) -> bool:
    deadline = time.monotonic() + timeout
    log_path = server.directory / "player.log"
    while time.monotonic() < deadline:
        if server.process.poll() is not None:
            server.finish_time()
            return False
        try:
            if "[DEDICATED_SERVER][READY]" in log_path.read_text(
                    encoding="utf-8", errors="replace"):
                return True
        except OSError:
            pass
        time.sleep(0.1)
    return False


def wait_clients(clients: list[ManagedProcess], timeout: float) -> bool:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        for client in clients:
            if client.process.poll() is not None and not client.ended_ms:
                client.finish_time()
        if all(client.process.poll() is not None for client in clients):
            return all(client.process.returncode == 0 for client in clients)
        if any(client.process.poll() not in (None, 0) for client in clients):
            return False
        time.sleep(0.2)
    return False


def process_evidence(managed: ManagedProcess) -> dict[str, Any]:
    recorded = read_json(managed.directory / "process.json")
    recorded.update({
        "role": managed.role,
        "processId": managed.process.pid,
        "startedUnixMilliseconds": managed.started_ms,
        "endedUnixMilliseconds": managed.finish_time(),
        "exitCode": managed.process.returncode,
        "logPath": str((managed.directory / "player.log").resolve()),
        "snapshotPath": str((managed.directory / "latest-snapshot.json").resolve()),
        "timelinePath": str((managed.directory / "timeline.ndjson").resolve()),
    })
    return recorded


def read_timeline(path: pathlib.Path) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    try:
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                item = json.loads(line)
                result.append({
                    "stepId": item.get("step", ""),
                    "role": item.get("role", ""),
                    "scenario": item.get("scenario", ""),
                    "state": item.get("state", ""),
                    "authoritativeTick": item.get("authoritativeTick", 0),
                    "timestampUtc": item.get("timestampUtc", ""),
                    "detail": item.get("detail", ""),
                })
    except (OSError, json.JSONDecodeError):
        return result
    return result


def merge_metrics(paths: list[pathlib.Path], scenario: str,
                  rtt: int, loss: int,
                  server_rejected_commands: int = 0) -> dict[str, Any]:
    values = [read_json(path) for path in paths]
    values = [value for value in values if value]
    if not values:
        return {}

    samples = sum(int(value.get("hitFeedbackSampleCount", 0)) for value in values)
    weighted_mean = (
        sum(float(value.get("meanHitFeedbackMilliseconds", 0))
            * int(value.get("hitFeedbackSampleCount", 0)) for value in values)
        / max(1, samples)
    )
    state_comparisons = sum(int(value.get("stateComparisonCount", 0)) for value in values)
    state_divergences = sum(int(value.get("stateDivergenceCount", 0)) for value in values)
    sent_commands = sum(int(value.get("sentCommandCount", 0)) for value in values)
    acknowledged_commands = sum(int(value.get("acceptedCommandCount", 0)) for value in values)
    dropped_commands = sum(int(value.get("droppedCommandCount", 0)) for value in values)
    rejected_commands = max(0, server_rejected_commands)
    accepted_commands = max(0, min(acknowledged_commands,
                                   sent_commands - dropped_commands) -
                            rejected_commands)
    return {
        "stableId": scenario,
        "durationSeconds": max(float(value.get("durationSeconds", 0)) for value in values),
        "hitFeedbackSampleCount": samples,
        "meanHitFeedbackMilliseconds": weighted_mean,
        "p95HitFeedbackMilliseconds": max(float(value.get("p95HitFeedbackMilliseconds", 0)) for value in values),
        "p99HitFeedbackMilliseconds": max(float(value.get("p99HitFeedbackMilliseconds", 0)) for value in values),
        "correctionCount": sum(int(value.get("correctionCount", 0)) for value in values),
        "correctionsPerMinute": max(float(value.get("correctionsPerMinute", 0)) for value in values),
        "meanCorrectionMagnitude": max(float(value.get("meanCorrectionMagnitude", 0)) for value in values),
        "p95CorrectionMagnitude": max(float(value.get("p95CorrectionMagnitude", 0)) for value in values),
        "maximumCorrectionMagnitude": max(float(value.get("maximumCorrectionMagnitude", 0)) for value in values),
        "uplinkBytes": sum(int(value.get("uplinkBytes", 0)) for value in values),
        "downlinkBytes": sum(int(value.get("downlinkBytes", 0)) for value in values),
        "uplinkBytesPerSecond": max(float(value.get("uplinkBytesPerSecond", 0)) for value in values),
        "downlinkBytesPerSecond": max(float(value.get("downlinkBytesPerSecond", 0)) for value in values),
        "stateComparisonCount": state_comparisons,
        "stateDivergenceCount": state_divergences,
        "stateDivergenceRate": state_divergences / max(1, state_comparisons),
        "maximumStateDivergenceMagnitude": max(float(value.get("maximumStateDivergenceMagnitude", 0)) for value in values),
        "maximumStateDivergenceDurationMilliseconds": max(float(value.get("maximumStateDivergenceDurationMilliseconds", 0)) for value in values),
        "sentCommandCount": sent_commands,
        "acceptedCommandCount": accepted_commands,
        "droppedCommandCount": dropped_commands,
        "rejectedCommandCount": rejected_commands,
        "meanTransportRttMilliseconds": max(float(value.get("meanTransportRttMilliseconds", 0)) for value in values),
        "maximumTransportRttMilliseconds": max(float(value.get("maximumTransportRttMilliseconds", 0)) for value in values),
        "configuredRttMilliseconds": rtt,
        "configuredPacketLossBasisPoints": loss,
    }


def run_scenario(server_binary: pathlib.Path, client_binary: pathlib.Path,
                 root: pathlib.Path, run_id: str, scenario: str, rtt: int,
                 loss: int, port: int, secret: str, timeout: int,
                 video_path: pathlib.Path | None = None
                 ) -> tuple[dict[str, Any], list[dict[str, Any]], bool]:
    scenario_root = root / "scenarios" / scenario
    server_dir = scenario_root / "server"
    client_a_dir = scenario_root / "client-a"
    client_b_dir = scenario_root / "client-b"
    match_id = f"{run_id}-{scenario}"[:64]
    environment = os.environ.copy()
    environment["FPS_SERVER_AUTH_SECRET"] = secret
    common = [
        "-batchmode", "-disable-audio",
        "-issue100-acceptance",
        "-issue100-run-id", run_id,
        "-issue100-scenario", scenario,
        "-issue99-match", match_id,
        "-issue100-timeout", str(timeout),
    ]
    server = spawn("server", server_binary, [
        *common, "-nographics",
        "-issue100-role", "server",
        "-issue100-output", str(server_dir),
        "-fps-server",
        "-server-map", "CityNew",
        "-server-port", str(port),
        "-server-match", match_id,
        "-server-max-players", "2",
        "-server-seed", "18018",
        "-server-version", "local-dev",
        "-server-tick-rate", "60",
        "-server-diagnostics", str(server_dir / "server-diagnostics.json"),
    ], server_dir, environment)
    processes = [server]
    video_pipeline: VideoPipeline | None = None
    passed = False
    try:
        if not wait_server_ready(server, 60):
            return scenario_failure(scenario, rtt, loss, processes, scenario_root,
                                    "server-not-ready")
        if video_path is not None:
            try:
                video_pipeline = start_video_pipeline(video_path)
            except (OSError, RuntimeError) as exc:
                return scenario_failure(scenario, rtt, loss, processes,
                                        scenario_root, "video-start-failed:" +
                                        str(exc))
        clients: list[ManagedProcess] = []
        for role, account, directory in (
            ("client-a", "issue100-client-a", client_a_dir),
            ("client-b", "issue100-client-b", client_b_dir),
        ):
            presentation_arguments = ["-nographics"]
            if role == "client-a" and video_pipeline is not None:
                presentation_arguments = [
                    "-screen-fullscreen", "0",
                    "-screen-width", "1280",
                    "-screen-height", "720",
                    "-issue100-record-video",
                    "-issue100-video-pipe", str(video_pipeline.fifo_path),
                ]
            client = spawn(role, client_binary, [
                *common, *presentation_arguments,
                "-issue100-role", role,
                "-issue100-output", str(directory),
                "-issue100-appearance", "character.quaternius.male-light",
                "-issue65-role", "client",
                "-issue65-port", str(port),
                "-issue86-account", account,
                "-issue86-version", "local-dev",
            ], directory, environment)
            clients.append(client)
            processes.append(client)
        passed = wait_clients(clients, timeout + 30)
        for client in clients:
            if client.process.poll() is None:
                stop(client)
            else:
                client.finish_time()
        if video_pipeline is not None:
            finish_video_pipeline(video_pipeline)
            passed = passed and video_pipeline.process.returncode == 0
    finally:
        stop(server)
        for process in processes[1:]:
            if process.process.poll() is None:
                stop(process)
        if video_pipeline is not None and not video_pipeline.completed:
            finish_video_pipeline(video_pipeline)

    process_records = [process_evidence(process) for process in processes]
    timeline_records: list[dict[str, Any]] = []
    raw_lines: list[str] = []
    for process in processes:
        timeline_path = process.directory / "timeline.ndjson"
        timeline_records.extend(read_timeline(timeline_path))
        try:
            raw_lines.extend(timeline_path.read_text(encoding="utf-8").splitlines())
        except OSError:
            pass
    timeline_records.sort(key=lambda item: item.get("timestampUtc", ""))
    merged_timeline = scenario_root / "timeline.ndjson"
    merged_timeline.parent.mkdir(parents=True, exist_ok=True)
    merged_timeline.write_text("\n".join(raw_lines) + ("\n" if raw_lines else ""),
                               encoding="utf-8")
    server_log = server_dir / "player.log"
    try:
        server_rejected_commands = server_log.read_text(
            encoding="utf-8", errors="replace").count(
                "[ISSUE100][COMMAND_REJECTED]")
    except OSError:
        server_rejected_commands = 0
    metrics = merge_metrics([
        client_a_dir / "metrics.json", client_b_dir / "metrics.json"
    ], scenario, rtt, loss, server_rejected_commands)
    scenario_record = {
        "stableId": scenario,
        "roundTripLatencyMilliseconds": rtt,
        "packetLossBasisPoints": loss,
        "processes": process_records,
        "metrics": metrics,
        "timelinePath": str(merged_timeline.resolve()),
        "runnerPassed": passed,
    }
    (scenario_root / "result.json").write_text(
        json.dumps(scenario_record, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    return scenario_record, timeline_records, passed


def scenario_failure(scenario: str, rtt: int, loss: int,
                     processes: list[ManagedProcess], root: pathlib.Path,
                     reason: str) -> tuple[dict[str, Any], list[dict[str, Any]], bool]:
    for process in processes:
        stop(process)
    root.mkdir(parents=True, exist_ok=True)
    record = {
        "stableId": scenario,
        "roundTripLatencyMilliseconds": rtt,
        "packetLossBasisPoints": loss,
        "processes": [process_evidence(process) for process in processes],
        "metrics": {},
        "timelinePath": str((root / "timeline.ndjson").resolve()),
        "runnerPassed": False,
        "failureReason": reason,
    }
    (root / "timeline.ndjson").touch()
    return record, [], False


def git_commit(project: pathlib.Path) -> str:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"], cwd=project,
        text=True, capture_output=True, check=False,
    )
    return result.stdout.strip() if result.returncode == 0 else "unknown"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--project", type=pathlib.Path,
                        default=pathlib.Path(__file__).resolve().parents[2])
    parser.add_argument("--build-manifest", type=pathlib.Path)
    parser.add_argument("--output", type=pathlib.Path)
    parser.add_argument("--run-id", default=utc_run_id())
    parser.add_argument("--base-port", type=int, default=18770)
    parser.add_argument("--timeout", type=int, default=240)
    parser.add_argument(
        "--scenario",
        choices=[scenario[0] for scenario in SCENARIOS],
        help="仅运行指定网络档位用于诊断；省略时执行完整验收矩阵",
    )
    parser.add_argument("--record-video", action="store_true",
                        help="在正常网络档由 Client A 离屏录制连续演示视频")
    args = parser.parse_args()

    project = args.project.resolve()
    manifest_path = (args.build_manifest or
                     project / "Builds" / "Issue100" / "build-manifest.json")
    manifest = read_json(manifest_path)
    if not manifest:
        print(f"[FAIL] 缺少构建清单：{manifest_path}", file=sys.stderr)
        return 2
    try:
        server_binary = executable(pathlib.Path(manifest["serverOutput"]))
        client_binary = executable(pathlib.Path(manifest["clientOutput"]))
    except (KeyError, FileNotFoundError) as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return 2

    output = (args.output or project / "artifacts" / "networking" /
              "issue100" / args.run_id).resolve()
    output.mkdir(parents=True, exist_ok=True)
    video_path = (output / "video" / "issue100-coop-demo.mp4"
                  if args.record_video else None)
    secret = secrets.token_hex(32)
    scenarios: list[dict[str, Any]] = []
    flow: list[dict[str, Any]] = []
    runner_passed = True
    selected_scenarios = [entry for entry in SCENARIOS
                          if not args.scenario or entry[0] == args.scenario]
    for index, (scenario, rtt, loss) in enumerate(selected_scenarios):
        print(f"[Issue100] 运行 {scenario} ...", flush=True)
        record, events, passed = run_scenario(
            server_binary, client_binary, output, args.run_id, scenario,
            rtt, loss, args.base_port + index, secret, args.timeout,
            video_path if scenario == "rtt-000-loss-00" else None,
        )
        scenarios.append(record)
        flow.extend(events)
        runner_passed = runner_passed and passed

    build_hash = f"server:{manifest.get('serverSha256', '')};client:{manifest.get('clientSha256', '')}"
    video_probe: dict[str, Any] = {}
    video_errors: list[str] = []
    if video_path is not None:
        video_probe, video_errors = probe_video(video_path)
        probe_path = video_path.parent / "ffprobe.json"
        probe_path.write_text(
            json.dumps(video_probe, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
    else:
        probe_path = None

    report = {
        "schemaVersion": SCHEMA_VERSION,
        "metadata": {
            "runId": args.run_id,
            "evidenceKind": "MultiProcessPlayer",
            "topology": "dedicated-server-plus-two-clients",
            "processesPerScenario": 3,
            "controlPlane": "local-acceptance",
            "dataPlane": "UnityTransport",
            "measurementScope": "real-player-process-utp-driver-statistics",
            "commit": git_commit(project),
            "buildHash": build_hash,
            "unityVersion": manifest.get("unityVersion", ""),
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
        },
        "scenarios": scenarios,
        "flow": flow,
        "artifacts": {
            "root": str(output),
            "videoPath": str(video_path.resolve()) if video_path else "",
            "videoProbePath": str(probe_path.resolve()) if probe_path else "",
        },
        "runnerPassed": runner_passed,
    }
    diagnostic_run = bool(args.scenario)
    errors = [] if diagnostic_run else validate(report, args.record_video)
    errors.extend(video_errors)
    if not runner_passed:
        errors.append("一个或多个玩家进程以失败状态退出")
    errors = sorted(set(errors))
    if diagnostic_run:
        report["gate"] = {
            "outcome": "DiagnosticPass" if not errors else "DiagnosticFail",
            "acceptanceEligible": False,
            "reasons": (["单场景诊断结果不能替代完整四档验收"]
                        if not errors else errors),
        }
    else:
        report["gate"] = {
            "outcome": "Pass" if not errors else "Fail",
            "acceptanceEligible": not errors,
            "reasons": errors,
        }
    report_path = output / "report.json"
    report_path.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    (output / "report.md").write_text(to_markdown(report, errors),
                                       encoding="utf-8")
    if errors:
        for error in errors:
            print(f"[FAIL] {error}", file=sys.stderr)
        print(f"证据已保留：{output}", file=sys.stderr)
        return 1
    if diagnostic_run:
        print(f"[PASS] Issue100 单场景诊断通过：{report_path}")
    else:
        print(f"[PASS] Issue100 真实三进程验收通过：{report_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
