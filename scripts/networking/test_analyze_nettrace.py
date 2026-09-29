#!/usr/bin/env python3
"""Unit tests for the opt-in combat trace gate."""

import unittest

from analyze_nettrace import analyse


def baseline():
    client = [
        {
            "kind": "world",
            "tickAgeMs": "20",
            "connected": "1",
            "rttMs": "50",
            "monoMs": str(1000 + index * 200),
        }
        for index in range(5)
    ]
    client.extend(
        {
            "kind": "enemy",
            "id": "7",
            "gen": "1",
            "monoMs": str(1000 + index * 200),
            "targetX": "0.0",
            "targetY": "1.0",
            "targetZ": "0.0",
            "viewX": "0.0",
            "viewY": "1.0",
            "viewZ": "0.0",
        }
        for index in range(5)
    )
    client.extend(
        [
            {
                "kind": "shotSend",
                "player": "1",
                "seq": "9",
                "monoMs": "1100",
            },
            {
                "kind": "shotAck",
                "player": "1",
                "seq": "9",
                "monoMs": "1200",
            },
        ]
    )
    server = [
        {
            "kind": "enemy",
            "id": "7",
            "gen": "1",
            "monoMs": str(500 + index * 200),
            "x": "0.0",
            "y": "1.0",
            "z": "0.0",
        }
        for index in range(5)
    ]
    server.append(
        {
            "kind": "shot",
            "player": "1",
            "cmdSeq": "9",
            "accepted": "1",
        }
    )
    return client, server


class NettraceGateTests(unittest.TestCase):
    def test_normal_trace_passes(self):
        client, server = baseline()
        report = analyse(client, server, require_server=True)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(1, report["counts"]["sendSeenByServer"])
        self.assertEqual(0, report["counts"]["worldApply"])

    def test_normal_world_application_trace_passes_and_reports_count(self):
        client, server = baseline()
        client.extend([
            {"kind": "worldApply", "arrivalGapMs": "80",
             "maxGapMs": "120", "tickDelta": "1", "maxTickDelta": "2",
             "appliedChanges": "6", "monoMs": "1200"},
            {"kind": "worldApply", "arrivalGapMs": "95",
             "maxGapMs": "100", "tickDelta": "1", "maxTickDelta": "1",
             "appliedChanges": "4", "monoMs": "1600"},
        ])
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(2, report["counts"]["worldApply"])
        self.assertEqual(10, report["counts"]["worldAppliedChanges"])
        self.assertEqual(120, report["maxima"]["worldApplyGapMs"])

    def test_new_trace_baseline_without_any_applied_world_is_inconclusive(self):
        client, server = baseline()
        client.append({"kind": "worldApplyBaseline", "monoMs": "900",
                       "tick": "0"})
        report = analyse(client, server)
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(1, report["counts"]["worldApplyBaseline"])

    def test_stalled_world_application_is_red_even_with_healthy_rtt(self):
        client, server = baseline()
        client.append({"kind": "worldApply", "arrivalGapMs": "90",
                       "maxGapMs": "820", "tickDelta": "1",
                       "maxTickDelta": "7", "appliedChanges": "3",
                       "monoMs": "1700"})
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(820, report["maxima"]["worldApplyGapMs"])
        self.assertEqual(7, report["maxima"]["worldApplyTickDelta"])

    def test_late_feedback_and_vertical_error_go_red(self):
        client, server = baseline()
        client[0]["tickAgeMs"] = "900"
        client[-1]["monoMs"] = "2200"
        enemy = next(event for event in client if event["kind"] == "enemy")
        enemy["viewY"] = "4.0"
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertGreaterEqual(len(report["violations"]), 3)

    def test_high_udp_rtt_goes_red(self):
        client, server = baseline()
        for event in client[:5]:
            event["rttMs"] = "650"
        self.assertEqual("red", analyse(client, server)["verdict"])

    def test_visual_horizontal_drift_goes_red(self):
        client, server = baseline()
        enemy = next(event for event in client if event["kind"] == "enemy")
        enemy["viewX"] = "4.0"
        self.assertEqual("red", analyse(client, server)["verdict"])

    def test_no_runtime_trace_is_not_false_green(self):
        self.assertEqual("insufficient", analyse([], [])["verdict"])

    def test_requested_server_log_must_contain_samples(self):
        client, _ = baseline()
        self.assertEqual(
            "insufficient", analyse(client, [], require_server=True)["verdict"]
        )

    def test_spawn_generation_isolated_for_vertical_steps(self):
        client, server = baseline()
        client[6]["gen"] = "2"
        client[6]["targetY"] = "5.0"
        client[6]["viewY"] = "5.0"
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])

    def test_many_old_unacknowledged_shots_are_red_not_false_green(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        client.extend({"kind": "shotSend", "player": "1", "seq": str(seq),
                       "monoMs": "1200"} for seq in range(100, 200))
        report = analyse(client, server, require_server=True)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(100, report["counts"]["overdueUnackedShots"])

    def test_unclassified_old_submit_attempts_without_server_log_are_inconclusive(self):
        client, _ = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        client.extend({"kind": "shotSend", "player": "1", "seq": str(seq),
                       "monoMs": "1200"} for seq in range(100, 200))
        report = analyse(client, [])
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(100, report["counts"]["overdueUnclassifiedShots"])
        self.assertFalse(any("UDP loss" in item for item in report["violations"]))

    def test_server_rejected_shots_expect_no_client_ack(self):
        client, server = baseline()
        client[:] = [event for event in client if event.get("kind") != "shotAck"]
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        server[-1]["accepted"] = "0"
        server[-1]["reject"] = "fire_rate"
        report = analyse(client, server, require_server=True)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(0, report["counts"]["overdueUnackedShots"])
        self.assertEqual(1, report["counts"]["serverRejectedShots"])
        self.assertEqual(1, report["counts"]["serverRejectedSends"])
        self.assertEqual({"fire_rate": 1}, report["serverRejectReasons"])

    def test_mixed_rejected_shots_do_not_inflate_missing_feedback(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        for seq in range(100, 110):
            client.append({"kind": "shotSend", "player": "1",
                           "seq": str(seq), "monoMs": "1200"})
            server.append({"kind": "shot", "player": "1",
                           "cmdSeq": str(seq), "accepted": "0",
                           "reject": "no_ammo"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(0, report["counts"]["overdueUnackedShots"])
        self.assertEqual(10, report["serverRejectReasons"]["no_ammo"])

    def test_new_submit_trace_overrides_legacy_send_for_feedback_timing(self):
        client, server = baseline()
        client.append({"kind": "shotSubmit", "player": "1", "seq": "9",
                       "monoMs": "1150", "connected": "1",
                       "listening": "1", "spawned": "1"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(50, report["maxima"]["shotFeedbackMs"])
        self.assertEqual(1, report["counts"]["shotSubmitAttempts"])
        self.assertEqual(1, report["counts"]["legacyShotSend"])

    def test_disconnected_submit_is_local_unknown_not_network_loss(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        client.append({"kind": "shotSubmit", "player": "1", "seq": "100",
                       "monoMs": "1200", "connected": "0",
                       "listening": "1", "spawned": "1"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(1, report["counts"]["invalidLocalSubmits"])
        self.assertEqual(0, report["counts"]["overdueUnseenByServer"])
        self.assertEqual({"connected": 1}, report["invalidLocalSubmitReasons"])

    def test_rpc_identity_rejection_requires_no_ack_or_shot_resolution(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        client.append({"kind": "shotSubmit", "player": "1", "seq": "100",
                       "monoMs": "1200", "connected": "1",
                       "listening": "1", "spawned": "1"})
        server.append({"kind": "shotReceive", "player": "1", "seq": "100",
                       "queued": "0"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(1, report["counts"]["serverQueuedRejects"])
        self.assertEqual(1, report["counts"]["submitAttemptsSeenAtRpcEntry"])
        self.assertEqual(0, report["counts"]["overdueUnackedShots"])

    def test_received_queued_but_unresolved_shots_are_server_stage_failure(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        for seq in range(100, 104):
            client.append({"kind": "shotSubmit", "player": "1",
                           "seq": str(seq), "monoMs": "1200",
                           "connected": "1", "listening": "1", "spawned": "1"})
            server.append({"kind": "shotReceive", "player": "1",
                           "seq": str(seq), "queued": "1"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(4, report["counts"]["overdueReceivedUnprocessedShots"])
        self.assertEqual(0, report["counts"]["overdueUnseenByServer"])

    def test_recent_in_flight_shots_get_tail_grace(self):
        client, server = baseline()
        client.extend({"kind": "shotSend", "player": "1", "seq": str(seq),
                       "monoMs": "1780"} for seq in range(100, 200))
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(100, report["counts"]["inFlightShots"])

    def test_aged_sends_missing_from_server_log_are_red(self):
        client, server = baseline()
        client.append({"kind": "world", "monoMs": "3000", "tickAgeMs": "20",
                       "connected": "1", "rttMs": "50"})
        for seq in range(100, 104):
            client.append({"kind": "shotSend", "player": "1",
                           "seq": str(seq), "monoMs": "1200"})
            client.append({"kind": "shotAck", "player": "1",
                           "seq": str(seq), "monoMs": "1300"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(4, report["counts"]["overdueUnseenByServer"])

    def test_persistent_incomplete_snapshots_are_red(self):
        client, server = baseline()
        client.extend({"kind": "incomplete", "monoMs": str(ms),
                       "actualTargets": "1", "expectedTargets": "4"}
                      for ms in range(1000, 2401, 200))
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(1400, report["maxima"]["longestIncompleteMs"])

    def test_scattered_incomplete_snapshots_do_not_imply_long_stall(self):
        client, server = baseline()
        client.extend({"kind": "incomplete", "monoMs": str(ms)}
                      for ms in (1000, 1200, 2200, 2400))
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(200, report["maxima"]["longestIncompleteMs"])

    def test_flagged_vertical_jump_after_two_second_gap_is_red(self):
        client, server = baseline()
        client.append({"kind": "enemy", "id": "7", "gen": "1",
                       "monoMs": "3800", "targetX": "0", "targetY": "8",
                       "targetZ": "0", "viewX": "0", "viewY": "8",
                       "viewZ": "0", "targetJump": "1", "viewJump": "1"})
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(2000, report["maxima"]["clientVerticalStepGapMs"])
        self.assertEqual(7, report["maxima"]["viewVerticalStepM"])

    def test_server_reported_jump_is_red_even_after_sparse_sample(self):
        client, server = baseline()
        server.append({"kind": "enemy", "id": "7", "gen": "1",
                       "monoMs": "3300", "x": "0", "y": "8", "z": "0",
                       "jumpY": "7", "reason": "vertical_jump"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(7, report["maxima"]["serverVerticalStepM"])

    def test_server_motion_with_static_client_is_not_green(self):
        client, server = baseline()
        for index in range(5, 12):
            client.append({"kind": "enemy", "id": "7", "gen": "1",
                           "monoMs": str(1000 + index * 200),
                           "targetX": "0", "targetY": "1", "targetZ": "0",
                           "viewX": "0", "viewY": "1", "viewZ": "0"})
            server.append({"kind": "enemy", "id": "7", "gen": "1",
                           "monoMs": str(500 + index * 200),
                           "x": str(index * 0.5), "y": "1", "z": "0"})
        report = analyse(client, server, require_server=True)
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(1, report["counts"]["staleTrajectories"])
        self.assertIn("time overlap", report["missing"][-1])

    def test_prefab_child_model_rises_while_target_and_view_remain_normal(self):
        client, server = baseline()
        enemies = [event for event in client if event["kind"] == "enemy"]
        for event in enemies:
            event.update({"modelY": "1.0", "bottomY": "0.2",
                          "alive": "1", "archetype": "spider"})
        enemies[-1]["modelY"] = "4.0"
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(3, report["maxima"]["modelRelativeStepM"])
        self.assertEqual(0, report["maxima"]["clientVerticalStepM"])
        self.assertEqual(0, report["maxima"]["renderVerticalOffsetM"])

    def test_renderer_bottom_rise_is_relative_to_own_view(self):
        client, server = baseline()
        enemies = [event for event in client if event["kind"] == "enemy"]
        for event in enemies:
            event.update({"modelY": "1.0", "bottomY": "0.2",
                          "alive": "1", "archetype": "spider"})
        enemies[-1]["bottomY"] = "3.2"
        report = analyse(client, server)
        self.assertEqual("red", report["verdict"])
        self.assertEqual(3, report["maxima"]["bottomRelativeStepM"])

    def test_single_model_sample_has_no_comparison_baseline(self):
        client, server = baseline()
        enemy = next(event for event in client if event["kind"] == "enemy")
        enemy.update({"modelY": "4", "bottomY": "3"})
        report = analyse(client, server)
        self.assertEqual("insufficient", report["verdict"])

    def test_unloaded_nan_models_are_not_height_samples_or_false_green(self):
        client, server = baseline()
        for event in client:
            if event.get("kind") == "enemy":
                event.update({"modelReady": "0", "modelY": "NaN",
                              "bottomY": "NaN", "alive": "1",
                              "archetype": "spider"})
        report = analyse(client, server)
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(0, report["counts"]["modelHeightSamples"])
        self.assertEqual(0, report["counts"]["rendererBoundsSamples"])
        self.assertEqual(5, report["counts"]["modelUnavailableSamples"])
        self.assertEqual(0, report["counts"]["modelReadySamples"])
        self.assertEqual(0, report["counts"]["modelComparablePairs"])
        self.assertEqual(0, report["maxima"]["modelRelativeStepM"])
        self.assertEqual(0, report["maxima"]["bottomRelativeStepM"])

    def test_model_unavailable_breaks_comparison_even_with_finite_fallback_values(self):
        client, server = baseline()
        enemies = [event for event in client if event.get("kind") == "enemy"]
        for event in enemies:
            event.update({"modelReady": "1", "modelY": "1.0",
                          "bottomY": "0.2", "alive": "1",
                          "archetype": "spider"})
        enemies[1].update({"modelReady": "0", "modelY": "100", "bottomY": "99"})
        for event in enemies[2:]:
            event.update({"modelY": "4.0", "bottomY": "3.2"})
        enemies[-1].update({"modelReady": "0", "modelY": "NaN", "bottomY": "NaN"})
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(3, report["counts"]["modelHeightSamples"])
        self.assertEqual(3, report["counts"]["rendererBoundsSamples"])
        self.assertEqual(2, report["counts"]["modelUnavailableSamples"])
        self.assertEqual(3, report["counts"]["modelReadySamples"])
        self.assertEqual(2, report["counts"]["modelComparablePairs"])
        self.assertEqual(0, report["maxima"]["modelRelativeStepM"])
        self.assertEqual(0, report["maxima"]["bottomRelativeStepM"])

    def test_loaded_flag_with_nonfinite_metrics_is_insufficient_not_model_motion(self):
        client, server = baseline()
        for event in client:
            if event.get("kind") == "enemy":
                event.update({"modelReady": "1", "modelY": "NaN",
                              "bottomY": "Infinity", "alive": "1"})
        report = analyse(client, server)
        self.assertEqual("insufficient", report["verdict"])
        self.assertEqual(0, report["counts"]["modelUnavailableSamples"])
        self.assertEqual(5, report["counts"]["modelReadySamples"])
        self.assertEqual(0, report["counts"]["modelHeightSamples"])
        self.assertEqual(0, report["counts"]["rendererBoundsSamples"])
        self.assertEqual(0, report["counts"]["modelComparablePairs"])

    def test_short_lived_enemy_without_model_baseline_does_not_mask_valid_series(self):
        client, server = baseline()
        for event in client:
            if event.get("kind") == "enemy":
                event.update({"modelY": "1.0", "bottomY": "0.2",
                              "alive": "1", "archetype": "spider"})
        client.append({"kind": "enemy", "id": "8", "gen": "1",
                       "monoMs": "1400", "targetX": "0", "targetY": "1",
                       "targetZ": "0", "viewX": "0", "viewY": "1",
                       "viewZ": "0", "modelY": "1", "bottomY": "0.2",
                       "alive": "1", "archetype": "spider"})
        report = analyse(client, server)
        self.assertEqual("green", report["verdict"])
        self.assertEqual(8, report["counts"]["modelComparablePairs"])
        self.assertEqual(2, report["counts"]["modelSequencesWithoutBaseline"])


if __name__ == "__main__":
    unittest.main()
