#!/usr/bin/env python3
"""Classify opt-in [NETTRACE-v1] combat latency and enemy motion traces.

One client log and (optionally) one server log must cover the same match.
The report deliberately does not subtract clocks from different machines.
"""

from __future__ import annotations

import argparse
import json
import math
from collections import defaultdict
from pathlib import Path

PREFIX = "[NETTRACE-v1] "


def parse_events(path: Path) -> list[dict[str, str]]:
    events: list[dict[str, str]] = []
    with path.open(encoding="utf-8", errors="replace") as stream:
        for line in stream:
            marker = line.find(PREFIX)
            if marker < 0:
                continue
            fields: dict[str, str] = {}
            for token in line[marker + len(PREFIX) :].split():
                if "=" not in token:
                    continue
                name, value = token.split("=", 1)
                fields[name] = value
            if fields:
                events.append(fields)
    return events


def number(event: dict[str, str], name: str) -> float:
    try:
        value = float(event[name])
    except (KeyError, ValueError) as error:
        raise ValueError(f"trace missing numeric {name}: {event}") from error
    if not math.isfinite(value):
        raise ValueError(f"trace has non-finite {name}: {event}")
    return value


def model_number(event: dict[str, str], name: str) -> float | None:
    """Optional visual telemetry; an unloaded view root is not model data."""
    if event.get("modelReady") == "0":
        return None
    try:
        value = float(event[name])
    except (KeyError, ValueError):
        return None
    return value if math.isfinite(value) else None


def enemy_key(event: dict[str, str]) -> tuple[str, str]:
    return event["id"], event["gen"]


def analyse(
    client: list[dict[str, str]],
    server: list[dict[str, str]],
    *,
    max_shot_feedback_ms: float = 750.0,
    max_p95_rtt_ms: float = 400.0,
    max_tick_age_ms: float = 500.0,
    max_world_apply_gap_ms: float = 500.0,
    max_vertical_offset_m: float = 2.0,
    max_horizontal_offset_m: float = 3.0,
    max_vertical_step_m: float = 2.0,
    max_model_relative_step_m: float = 1.5,
    max_overdue_shots: int = 2,
    max_server_unseen_shots: int = 2,
    max_incomplete_ms: float = 1000.0,
    max_sample_gap_ms: float = 500.0,
    min_trajectory_span_ms: float = 1500.0,
    min_server_motion_m: float = 3.0,
    max_client_still_m: float = 0.5,
    min_enemy_samples: int = 5,
    min_world_samples: int = 5,
    require_server: bool = False,
) -> dict[str, object]:
    world = [event for event in client if event.get("kind") == "world"]
    world_applies = [
        event for event in client if event.get("kind") == "worldApply"
    ]
    world_apply_baselines = [
        event for event in client if event.get("kind") == "worldApplyBaseline"
    ]
    incomplete = [
        event for event in client if event.get("kind") == "incomplete"
    ]
    client_enemies = [
        event for event in client if event.get("kind") == "enemy"
    ]
    server_enemies = [
        event for event in server if event.get("kind") == "enemy"
    ]
    legacy_sends = {
        (event.get("player"), event.get("seq")): event
        for event in client
        if event.get("kind") == "shotSend"
    }
    submitted = {
        (event.get("player"), event.get("seq")): event
        for event in client
        if event.get("kind") == "shotSubmit"
    }
    # New records replace legacy attempts when both appear for one sequence.
    sends = {**legacy_sends, **submitted}
    acknowledgements = [
        event for event in client if event.get("kind") == "shotAck"
    ]
    server_shots = [
        event for event in server if event.get("kind") == "shot"
    ]
    server_receives = [
        event for event in server if event.get("kind") == "shotReceive"
    ]
    server_receive_keys = {
        (event.get("player"), event.get("seq"))
        for event in server_receives
    }
    server_queued_keys = {
        (event.get("player"), event.get("seq"))
        for event in server_receives if event.get("queued") == "1"
    }
    server_queue_rejected_keys = {
        (event.get("player"), event.get("seq"))
        for event in server_receives if event.get("queued") == "0"
    } - server_queued_keys
    server_shot_keys = {
        (event.get("player"), event.get("cmdSeq"))
        for event in server_shots
    }
    server_accepted_keys = {
        (event.get("player"), event.get("cmdSeq"))
        for event in server_shots if event.get("accepted") == "1"
    }
    server_rejected_keys = {
        (event.get("player"), event.get("cmdSeq"))
        for event in server_shots if event.get("accepted") == "0"
    } - server_accepted_keys
    server_reject_reasons: dict[str, int] = defaultdict(int)
    for event in server_shots:
        if event.get("accepted") == "0":
            server_reject_reasons[event.get("reject", "unspecified")] += 1
    invalid_submit_keys: set[tuple[str | None, str | None]] = set()
    invalid_submit_reasons: dict[str, int] = defaultdict(int)
    for key, event in submitted.items():
        for field in ("connected", "listening", "spawned"):
            if event.get(field) != "1":
                invalid_submit_keys.add(key)
                invalid_submit_reasons[field] += 1
    violations: list[str] = []
    missing: list[str] = []
    if len(world) < min_world_samples:
        missing.append(f"world samples {len(world)} < {min_world_samples}")
    if len(client_enemies) < min_enemy_samples:
        missing.append(
            f"client enemy samples {len(client_enemies)} < {min_enemy_samples}"
        )
    if require_server and len(server_enemies) < min_enemy_samples:
        missing.append(
            f"server enemy samples {len(server_enemies)} < {min_enemy_samples}"
        )
    if not sends:
        missing.append("no shotSubmit/legacy shotSend attempt event")
    if invalid_submit_keys:
        missing.append(
            f"{len(invalid_submit_keys)} local shot submit attempts failed "
            "connection/listening/spawn preflight; network delivery unknown"
        )
    if require_server and sends and not server_shots and not server_receives:
        missing.append("no server RPC receipt or shot resolution event")
    if world_apply_baselines and not world_applies:
        missing.append(
            "worldApplyBaseline is present but no NGO worldApply event followed"
        )
    client_end_ms = max(
        (number(event, "monoMs") for event in client if "monoMs" in event),
        default=0.0,
    )
    rtts = sorted(
        number(event, "rttMs")
        for event in world
        if event.get("connected") == "1" and "rttMs" in event
        and number(event, "rttMs") > 0
    )
    if not rtts:
        missing.append("no connected UDP RTT sample")
    p95_rtt = rtts[math.ceil(len(rtts) * 0.95) - 1] if rtts else 0.0
    if p95_rtt > max_p95_rtt_ms:
        violations.append(f"UDP RTT p95 {p95_rtt:.1f} ms")

    worst_tick_age = max(
        (number(event, "tickAgeMs") for event in world), default=0.0
    )
    if worst_tick_age > max_tick_age_ms:
        violations.append(
            f"replicated world tick stalled {worst_tick_age:.1f} ms"
        )
    worst_world_apply_gap = max(
        (
            max(number(event, "arrivalGapMs"), number(event, "maxGapMs"))
            for event in world_applies
        ),
        default=0.0,
    )
    worst_world_apply_tick_delta = max(
        (number(event, "maxTickDelta") for event in world_applies),
        default=0.0,
    )
    applied_world_changes = sum(
        number(event, "appliedChanges") for event in world_applies
    )
    if world_applies and worst_world_apply_gap > max_world_apply_gap_ms:
        violations.append(
            "NGO main-thread world state application gap "
            f"{worst_world_apply_gap:.1f} ms"
        )

    longest_incomplete_ms = consecutive_duration(incomplete, max_sample_gap_ms)
    if longest_incomplete_ms > max_incomplete_ms:
        violations.append(
            f"replicated enemy snapshot incomplete for "
            f"{longest_incomplete_ms:.1f} ms"
        )

    worst_shot_feedback = 0.0
    matched_shots = 0
    acknowledged_keys: set[tuple[str | None, str | None]] = set()
    for ack in acknowledgements:
        key = (ack.get("player"), ack.get("seq"))
        send = sends.get(key)
        if send is None:
            continue
        delay = number(ack, "monoMs") - number(send, "monoMs")
        if delay < 0:
            continue
        acknowledged_keys.add(key)
        matched_shots += 1
        worst_shot_feedback = max(worst_shot_feedback, delay)
    known_no_ack_keys = server_rejected_keys | server_queue_rejected_keys
    ack_expected_keys = set(sends) - known_no_ack_keys - invalid_submit_keys
    if ack_expected_keys and not acknowledgements:
        missing.append("no shotAck event for non-rejected sends")
    if ack_expected_keys and matched_shots == 0:
        missing.append("no shotSend/shotAck sequence matched")
    if worst_shot_feedback > max_shot_feedback_ms:
        violations.append(
            f"shot feedback took {worst_shot_feedback:.1f} ms"
        )
    overdue_keys = {
        key for key, send in sends.items()
        if client_end_ms - number(send, "monoMs") > max_shot_feedback_ms
    }
    overdue_unacked = overdue_keys - acknowledged_keys - known_no_ack_keys - invalid_submit_keys
    overdue_accepted_unacked = overdue_unacked & server_accepted_keys
    overdue_received_unprocessed = (
        overdue_unacked & server_queued_keys - server_shot_keys
    )
    overdue_unclassified = (
        overdue_unacked - server_shot_keys - server_queued_keys
    )
    in_flight = set(sends) - acknowledged_keys - overdue_keys - known_no_ack_keys - invalid_submit_keys
    server_unseen = (
        overdue_keys - server_receive_keys - server_shot_keys - invalid_submit_keys
        if require_server else set()
    )
    if len(overdue_accepted_unacked) > max_overdue_shots:
        violations.append(
            f"{len(overdue_accepted_unacked)} server-accepted shots have "
            f"no client feedback after {max_shot_feedback_ms:.0f} ms grace"
        )
    elif overdue_accepted_unacked:
        missing.append(
            f"{len(overdue_accepted_unacked)} server-accepted shots have "
            "no client feedback; below failure-count threshold"
        )
    if overdue_unclassified and not require_server:
        missing.append(
            f"{len(overdue_unclassified)} overdue shots lack both feedback "
            "and authoritative server classification; delivery cause unknown"
        )
    if len(overdue_received_unprocessed) > max_overdue_shots:
        violations.append(
            f"{len(overdue_received_unprocessed)} RPC-received shot "
            "attempts were queued but lack server simulation resolution"
        )
    elif overdue_received_unprocessed:
        missing.append(
            f"{len(overdue_received_unprocessed)} queued shots lack server "
            "simulation resolution; below failure-count threshold"
        )
    if len(server_unseen) > max_server_unseen_shots:
        violations.append(
            f"{len(server_unseen)} aged shot submit attempts have no "
            "matching server receipt in the supplied trace"
        )
    elif server_unseen:
        missing.append(
            f"{len(server_unseen)} aged shot submit attempts lack a "
            "matching server receipt; below failure-count threshold"
        )

    worst_render_offset = 0.0
    worst_horizontal_offset = 0.0
    worst_client_vertical_step = 0.0
    worst_client_vertical_step_gap = 0.0
    worst_view_vertical_step = 0.0
    worst_view_vertical_step_gap = 0.0
    worst_server_vertical_step = 0.0
    worst_server_vertical_step_gap = 0.0
    worst_model_relative_step = 0.0
    worst_model_relative_gap = 0.0
    worst_bottom_relative_step = 0.0
    worst_bottom_relative_gap = 0.0
    model_comparable_pairs = 0
    model_baseline_missing = 0
    model_height_samples = sum(
        model_number(event, "modelY") is not None for event in client_enemies
    )
    renderer_bounds_samples = sum(
        model_number(event, "bottomY") is not None for event in client_enemies
    )
    model_unavailable_samples = sum(
        event.get("modelReady") == "0" for event in client_enemies
    )
    if any("modelReady" in event or "modelY" in event
           for event in client_enemies) and not model_height_samples:
        missing.append("no finite loaded-model height sample; view root is not model data")
    if any("bottomY" in event for event in client_enemies) and not renderer_bounds_samples:
        missing.append("no finite loaded-model renderer-bound sample")
    client_groups: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    server_groups: dict[tuple[str, str], list[dict[str, str]]] = defaultdict(list)
    for event in client_enemies:
        client_groups[enemy_key(event)].append(event)
        offset = abs(number(event, "viewY") - number(event, "targetY"))
        worst_render_offset = max(worst_render_offset, offset)
        horizontal = math.hypot(
            number(event, "viewX") - number(event, "targetX"),
            number(event, "viewZ") - number(event, "targetZ"),
        )
        worst_horizontal_offset = max(worst_horizontal_offset, horizontal)
    for event in server_enemies:
        server_groups[enemy_key(event)].append(event)

    for samples in client_groups.values():
        samples.sort(key=lambda event: number(event, "monoMs"))
        step, gap = vertical_step(samples, "targetY", "targetJump")
        if step > worst_client_vertical_step:
            worst_client_vertical_step, worst_client_vertical_step_gap = step, gap
        step, gap = vertical_step(samples, "viewY", "viewJump")
        if step > worst_view_vertical_step:
            worst_view_vertical_step, worst_view_vertical_step_gap = step, gap
        for field in ("modelY", "bottomY"):
            step, gap, pairs, samples_with_field = relative_vertical_step(
                samples, field
            )
            model_comparable_pairs += pairs
            if samples_with_field and not pairs:
                model_baseline_missing += 1
            if field == "modelY" and step > worst_model_relative_step:
                worst_model_relative_step, worst_model_relative_gap = step, gap
            if field == "bottomY" and step > worst_bottom_relative_step:
                worst_bottom_relative_step, worst_bottom_relative_gap = step, gap
    if model_baseline_missing and model_comparable_pairs == 0:
        missing.append(
            f"{model_baseline_missing} model/bounds sequences lack a "
            "same-enemy, same-generation comparison baseline"
        )
    for samples in server_groups.values():
        samples.sort(key=lambda event: number(event, "monoMs"))
        step, gap = vertical_step(samples, "y", "reason")
        if step > worst_server_vertical_step:
            worst_server_vertical_step, worst_server_vertical_step_gap = step, gap
        for event in samples:
            if event.get("reason") == "vertical_jump":
                reported = abs(number(event, "jumpY"))
                if reported > worst_server_vertical_step:
                    worst_server_vertical_step = reported
                    worst_server_vertical_step_gap = 0.0
    stale_trajectories: list[dict[str, object]] = []
    for key in client_groups.keys() & server_groups.keys():
        observed = client_groups[key]
        authoritative = server_groups[key]
        if len(observed) < 2 or len(authoritative) < 2:
            continue
        required = ("targetX", "targetZ", "viewX", "viewZ")
        if any(any(field not in event for field in required) for event in observed):
            continue
        if any(any(field not in event for field in ("x", "z")) for event in authoritative):
            continue
        client_span = number(observed[-1], "monoMs") - number(observed[0], "monoMs")
        server_span = number(authoritative[-1], "monoMs") - number(authoritative[0], "monoMs")
        if min(client_span, server_span) < min_trajectory_span_ms:
            continue
        server_motion = horizontal_excursion(authoritative, "x", "z")
        target_motion = horizontal_excursion(observed, "targetX", "targetZ")
        view_motion = horizontal_excursion(observed, "viewX", "viewZ")
        if (server_motion >= min_server_motion_m
                and max(target_motion, view_motion) <= max_client_still_m):
            stale_trajectories.append({
                "id": key[0], "gen": key[1],
                "serverMotionM": round(server_motion, 3),
                "clientTargetMotionM": round(target_motion, 3),
                "clientViewMotionM": round(view_motion, 3),
                "serverSpanMs": round(server_span, 1),
                "clientSpanMs": round(client_span, 1),
            })
    if stale_trajectories:
        missing.append(
            f"{len(stale_trajectories)} enemy trajectories move on server "
            "but remain static on client; cross-machine time overlap "
            "is unverified"
        )
    if worst_render_offset > max_vertical_offset_m:
        violations.append(
            f"view/target vertical offset {worst_render_offset:.2f} m"
        )
    if worst_horizontal_offset > max_horizontal_offset_m:
        violations.append(
            f"view/target horizontal offset {worst_horizontal_offset:.2f} m"
        )
    if worst_client_vertical_step > max_vertical_step_m:
        violations.append(
            f"client target vertical step {worst_client_vertical_step:.2f} m "
            f"over {worst_client_vertical_step_gap:.1f} ms"
        )
    if worst_view_vertical_step > max_vertical_step_m:
        violations.append(
            f"client view vertical step {worst_view_vertical_step:.2f} m "
            f"over {worst_view_vertical_step_gap:.1f} ms"
        )
    if worst_server_vertical_step > max_vertical_step_m:
        violations.append(
            f"server authority vertical step {worst_server_vertical_step:.2f} m "
            f"over {worst_server_vertical_step_gap:.1f} ms"
        )
    if worst_model_relative_step > max_model_relative_step_m:
        violations.append(
            f"enemy model/view relative vertical step "
            f"{worst_model_relative_step:.2f} m over "
            f"{worst_model_relative_gap:.1f} ms"
        )
    if worst_bottom_relative_step > max_model_relative_step_m:
        violations.append(
            f"enemy renderer-bottom/view relative vertical step "
            f"{worst_bottom_relative_step:.2f} m over "
            f"{worst_bottom_relative_gap:.1f} ms"
        )

    verdict = "red" if violations else "insufficient" if missing else "green"
    return {
        "verdict": verdict,
        "counts": {
            "world": len(world),
            "worldApply": len(world_applies),
            "worldApplyBaseline": len(world_apply_baselines),
            "worldAppliedChanges": int(applied_world_changes),
            "clientEnemy": len(client_enemies),
            "serverEnemy": len(server_enemies),
            "shotSend": len(sends),
            "shotSubmitAttempts": len(sends),
            "newShotSubmit": len(submitted),
            "legacyShotSend": len(legacy_sends),
            "invalidLocalSubmits": len(invalid_submit_keys),
            "shotAck": len(acknowledgements),
            "matchedShots": matched_shots,
            "overdueUnackedShots": len(overdue_unacked),
            "overdueAcceptedUnackedShots": len(overdue_accepted_unacked),
            "overdueUnclassifiedShots": len(overdue_unclassified),
            "overdueReceivedUnprocessedShots": len(overdue_received_unprocessed),
            "inFlightShots": len(in_flight),
            "shotReceive": len(server_receives),
            "serverQueuedRejects": len(server_queue_rejected_keys),
            "submitAttemptsSeenAtRpcEntry": len(set(sends) & server_receive_keys),
            "serverShots": len(server_shots),
            "sendSeenByServer": len(set(sends) & server_shot_keys),
            "overdueUnseenByServer": len(server_unseen),
            "overdueSubmitAttemptsWithoutServerReceipt": len(server_unseen),
            "incompleteSnapshots": len(incomplete),
            "staleTrajectories": len(stale_trajectories),
            "serverRejectedShots": sum(
                event.get("accepted") == "0" for event in server_shots
            ),
            "serverRejectedSends": len(set(sends) & server_rejected_keys),
            "modelComparablePairs": model_comparable_pairs,
            "modelSequencesWithoutBaseline": model_baseline_missing,
            "modelHeightSamples": model_height_samples,
            "rendererBoundsSamples": renderer_bounds_samples,
            "modelUnavailableSamples": model_unavailable_samples,
            "modelReadySamples": sum(
                event.get("modelReady") == "1" for event in client_enemies
            ),
            "rtt": len(rtts),
        },
        "maxima": {
            "tickAgeMs": round(worst_tick_age, 1),
            "worldApplyGapMs": round(worst_world_apply_gap, 1),
            "worldApplyTickDelta": int(worst_world_apply_tick_delta),
            "shotFeedbackMs": round(worst_shot_feedback, 1),
            "p95RttMs": round(p95_rtt, 1),
            "renderVerticalOffsetM": round(worst_render_offset, 3),
            "renderHorizontalOffsetM": round(worst_horizontal_offset, 3),
            "clientVerticalStepM": round(worst_client_vertical_step, 3),
            "clientVerticalStepGapMs": round(worst_client_vertical_step_gap, 1),
            "viewVerticalStepM": round(worst_view_vertical_step, 3),
            "viewVerticalStepGapMs": round(worst_view_vertical_step_gap, 1),
            "serverVerticalStepM": round(worst_server_vertical_step, 3),
            "serverVerticalStepGapMs": round(worst_server_vertical_step_gap, 1),
            "modelRelativeStepM": round(worst_model_relative_step, 3),
            "modelRelativeStepGapMs": round(worst_model_relative_gap, 1),
            "bottomRelativeStepM": round(worst_bottom_relative_step, 3),
            "bottomRelativeStepGapMs": round(worst_bottom_relative_gap, 1),
            "longestIncompleteMs": round(longest_incomplete_ms, 1),
        },
        "violations": violations,
        "missing": missing,
        "staleTrajectoryDetails": stale_trajectories,
        "serverRejectReasons": dict(sorted(server_reject_reasons.items())),
        "invalidLocalSubmitReasons": dict(sorted(invalid_submit_reasons.items())),
        "note": (
            "Legacy enemy traces may lack snapshotTick: world tick is not an exact "
            "per-enemy send tick, and cross-machine UTC is not used as latency "
            "or instant position alignment. Stale trajectory flags are "
            "inconclusive without verified time overlap."
            " worldApply measures NGO main-thread state application, not "
            "UDP socket packet arrival. A server-rejected shot is not "
            "expected to produce shotAck. Missing feedback without server "
            "classification cannot establish UDP packet loss. Model and "
            "renderer-bottom checks detect within-instance changes only; "
            "a stable initial prefab offset needs independent calibration. "
            "Short-lived groups without comparison samples are counted but "
            "do not invalidate other comparable model sequences."
            " modelReady=0, absent fields and non-finite model/bounds values "
            "are not height samples or comparison pairs. Readiness flags "
            "and finite measurement counts are reported separately; legacy "
            "finite fields do not establish model-loading readiness."
            " shotSubmit/shotSend records a local submit attempt before "
            "the RPC; shotReceive records server RPC entry, and shot records "
            "later simulation resolution. An absent entry in the supplied "
            "trace does not by itself establish UDP packet loss."
        ),
    }


def consecutive_duration(
    samples: list[dict[str, str]], max_gap_ms: float
) -> float:
    if not samples:
        return 0.0
    ordered = sorted(samples, key=lambda event: number(event, "monoMs"))
    start = previous = number(ordered[0], "monoMs")
    longest = 0.0
    for event in ordered[1:]:
        current = number(event, "monoMs")
        if current - previous > max_gap_ms:
            start = current
        else:
            longest = max(longest, current - start)
        previous = current
    return longest


def horizontal_excursion(
    samples: list[dict[str, str]], x_field: str, z_field: str
) -> float:
    first_x = number(samples[0], x_field)
    first_z = number(samples[0], z_field)
    return max(
        math.hypot(number(event, x_field) - first_x,
                   number(event, z_field) - first_z)
        for event in samples
    )


def relative_vertical_step(
    samples: list[dict[str, str]], model_field: str
) -> tuple[float, float, int, int]:
    largest = 0.0
    largest_gap = 0.0
    pairs = 0
    samples_with_field = sum(
        model_number(event, model_field) is not None and "viewY" in event
        for event in samples
    )
    for previous, current in zip(samples, samples[1:]):
        previous_value = model_number(previous, model_field)
        current_value = model_number(current, model_field)
        if previous_value is None or current_value is None:
            continue
        if previous.get("alive") == "0" or current.get("alive") == "0":
            continue
        if (previous.get("archetype") is not None and
                current.get("archetype") is not None and
                previous["archetype"] != current["archetype"]):
            continue
        elapsed = number(current, "monoMs") - number(previous, "monoMs")
        if elapsed <= 0:
            continue
        pairs += 1
        previous_offset = previous_value - number(previous, "viewY")
        current_offset = current_value - number(current, "viewY")
        step = abs(current_offset - previous_offset)
        if step > largest:
            largest, largest_gap = step, elapsed
    return largest, largest_gap, pairs, samples_with_field


def vertical_step(
    samples: list[dict[str, str]], field: str, jump_field: str
) -> tuple[float, float]:
    largest = 0.0
    largest_gap = 0.0
    for previous, current in zip(samples, samples[1:]):
        elapsed = number(current, "monoMs") - number(previous, "monoMs")
        flagged = (
            current.get(jump_field) == "1"
            or current.get(jump_field) == "vertical_jump"
        )
        if elapsed > 0 and (elapsed <= 300 or flagged):
            step = abs(number(current, field) - number(previous, field))
            if step > largest:
                largest, largest_gap = step, elapsed
    return largest, largest_gap


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--client-log", required=True, type=Path)
    parser.add_argument("--server-log", type=Path)
    parser.add_argument("--max-shot-feedback-ms", type=float, default=750.0)
    parser.add_argument("--max-p95-rtt-ms", type=float, default=400.0)
    parser.add_argument("--max-tick-age-ms", type=float, default=500.0)
    parser.add_argument("--max-world-apply-gap-ms", type=float, default=500.0)
    parser.add_argument("--max-vertical-offset-m", type=float, default=2.0)
    parser.add_argument("--max-horizontal-offset-m", type=float, default=3.0)
    parser.add_argument("--max-vertical-step-m", type=float, default=2.0)
    parser.add_argument("--max-model-relative-step-m", type=float, default=1.5)
    parser.add_argument("--max-overdue-shots", type=int, default=2)
    parser.add_argument("--max-server-unseen-shots", type=int, default=2)
    parser.add_argument("--max-incomplete-ms", type=float, default=1000.0)
    parser.add_argument("--max-sample-gap-ms", type=float, default=500.0)
    parser.add_argument("--min-trajectory-span-ms", type=float, default=1500.0)
    parser.add_argument("--min-server-motion-m", type=float, default=3.0)
    parser.add_argument("--max-client-still-m", type=float, default=0.5)
    parser.add_argument("--min-enemy-samples", type=int, default=5)
    parser.add_argument("--min-world-samples", type=int, default=5)
    args = parser.parse_args()
    report = analyse(
        parse_events(args.client_log),
        parse_events(args.server_log) if args.server_log else [],
        max_shot_feedback_ms=args.max_shot_feedback_ms,
        max_p95_rtt_ms=args.max_p95_rtt_ms,
        max_tick_age_ms=args.max_tick_age_ms,
        max_world_apply_gap_ms=args.max_world_apply_gap_ms,
        max_vertical_offset_m=args.max_vertical_offset_m,
        max_horizontal_offset_m=args.max_horizontal_offset_m,
        max_vertical_step_m=args.max_vertical_step_m,
        max_model_relative_step_m=args.max_model_relative_step_m,
        max_overdue_shots=args.max_overdue_shots,
        max_server_unseen_shots=args.max_server_unseen_shots,
        max_incomplete_ms=args.max_incomplete_ms,
        max_sample_gap_ms=args.max_sample_gap_ms,
        min_trajectory_span_ms=args.min_trajectory_span_ms,
        min_server_motion_m=args.min_server_motion_m,
        max_client_still_m=args.max_client_still_m,
        min_enemy_samples=args.min_enemy_samples,
        min_world_samples=args.min_world_samples,
        require_server=args.server_log is not None,
    )
    print(json.dumps(report, ensure_ascii=False, indent=2))
    return {"green": 0, "red": 1, "insufficient": 2}[report["verdict"]]


if __name__ == "__main__":
    raise SystemExit(main())
