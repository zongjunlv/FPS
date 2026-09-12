#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Read-only live diagnostics for AI, waves, perception and enemy pools.</summary>
public sealed class CombatRuntimeDiagnosticsPanel : MonoBehaviour
{
    private const float RefreshInterval = 0.25f;
    private readonly RuntimeDiagnosticsRefreshGate refreshGate =
        new(RefreshInterval);
    private WaveDirector director;
    private RuntimeCombatDiagnosticsSnapshot snapshot;
    private Vector2 scroll;

    public bool IsVisible => refreshGate.Visible;
    public int CaptureCount { get; private set; }

    public void Configure(WaveDirector waveDirector)
    {
        director = waveDirector;
    }

    public void SetVisible(bool visible)
    {
        refreshGate.SetVisible(visible, Time.unscaledTime);
        if (!visible)
        {
            snapshot = null;
        }
    }

    private void Update()
    {
        if (Keyboard.current?.f7Key.wasPressedThisFrame == true)
        {
            SetVisible(!IsVisible);
        }

        if (!refreshGate.TryConsume(Time.unscaledTime) || director == null)
        {
            return;
        }

        snapshot = director.CaptureCombatDiagnostics();
        CaptureCount++;
    }

    private void OnDisable()
    {
        SetVisible(false);
    }

    private void OnGUI()
    {
        if (!IsVisible)
        {
            return;
        }

        float width = Mathf.Min(Screen.width - 32f, 1040f);
        float height = Mathf.Min(Screen.height - 32f, 660f);
        GUILayout.BeginArea(
            new Rect((Screen.width - width) * 0.5f, 16f, width, height),
            GUI.skin.window);
        GUILayout.Label("战斗运行时诊断 · F7 关闭", GUI.skin.box);
        if (snapshot == null)
        {
            GUILayout.Label("正在采集诊断快照……");
            GUILayout.EndArea();
            return;
        }

        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.BeginHorizontal();
        DrawAiColumn(width * 0.43f);
        DrawWaveColumn(width * 0.27f);
        DrawPoolColumn(width * 0.27f);
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawAiColumn(float width)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
        GUILayout.Label("AI 分布");
        DrawCounts("状态", snapshot.AwarenessStates);
        DrawCounts("职责", snapshot.Roles);
        DrawCounts("LOD", snapshot.LodTiers);
        DrawUtilityDecisions();
        GUILayout.Space(8f);
        GUILayout.Label("感知预算");
        PerceptionRuntimeDiagnostics value = snapshot.Perception;
        if (value == null)
        {
            GUILayout.Label("感知调度器未运行");
        }
        else
        {
            GUILayout.Label($"本帧检查 {value.ChecksLastFrame} / 预算 {value.BudgetPerFrame}");
            GUILayout.Label($"注册 {value.RegisteredCount} · 等待 {value.QueueLength} · 批次 {value.PendingBatchCount}");
            GUILayout.Label($"延迟 最大 {value.MaximumLatencyFrames} 帧 · 平均 {value.AverageLatencyFrames:F1} 帧");
        }
        GUILayout.EndVertical();
    }

    private void DrawUtilityDecisions()
    {
        GUILayout.Space(8f);
        GUILayout.Label("Utility AI 决策", GUI.skin.box);

        if (snapshot.UtilityDecisions.Count == 0)
        {
            GUILayout.Label("当前没有启用 Utility AI 的敌人");
            return;
        }

        int count = Mathf.Min(4, snapshot.UtilityDecisions.Count);

        for (int index = 0; index < count; index++)
        {
            EnemyUtilityRuntimeDiagnostics decision =
                snapshot.UtilityDecisions[index];
            EnemyUtilityWorldFacts facts = decision.Facts;
            GUILayout.Label(
                $"#{decision.SpawnId:000} {decision.Role} → " +
                decision.SelectedAction);
            GUILayout.Label("原因：" + decision.Reason);
            GUILayout.Label(
                $"距离 {facts.TargetDistance:F1}m · 生命 " +
                $"{facts.HealthRatio * 100f:F0}% · " +
                $"视线 {(facts.HasLineOfSight ? "有" : "无")} · " +
                $"掩体 {(facts.TargetInCover ? "是" : "否")}");
            GUILayout.Label(
                $"友军 R{facts.FriendlyRaiderCount}/" +
                $"P{facts.FriendlySuppressorCount}/" +
                $"S{facts.FriendlySupportCount} · " +
                $"支援 {(facts.HasSupportCoverage ? "覆盖" : "未覆盖")}");

            for (int candidateIndex = 0;
                 candidateIndex < decision.Candidates.Count;
                 candidateIndex++)
            {
                EnemyUtilityCandidateRuntimeDiagnostics candidate =
                    decision.Candidates[candidateIndex];
                string cooldown = candidate.CooldownRemaining > 0.01f
                    ? $" · 冷却 {candidate.CooldownRemaining:F1}s"
                    : string.Empty;
                GUILayout.Label(
                    $"  {candidate.DisplayName}: {candidate.Score:F2} " +
                    $"[{StatusLabel(candidate)}]{cooldown}");
            }

            GUILayout.Space(4f);
        }

        if (snapshot.UtilityDecisions.Count > count)
        {
            GUILayout.Label(
                $"另有 {snapshot.UtilityDecisions.Count - count} 个决策体");
        }
    }

    private static string StatusLabel(
        EnemyUtilityCandidateRuntimeDiagnostics candidate)
    {
        if (candidate.Eligible)
        {
            return "可选";
        }

        return candidate.Status switch
        {
            "unreachable" => "不可达",
            "cooldown" => "冷却中",
            "below-threshold" => "低于门槛",
            _ => "不可选"
        };
    }

    private void DrawWaveColumn(float width)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
        GUILayout.Label("波次与生成队列");
        WaveRuntimeDiagnostics wave = snapshot.Wave;
        if (wave == null)
        {
            GUILayout.Label("波次系统未运行");
        }
        else
        {
            GUILayout.Label($"波次 {wave.CurrentWave}/{wave.TotalWaves} · {wave.Phase}");
            GUILayout.Label($"威胁预算 {wave.ThreatBudget} · 已选威胁 {wave.ResolvedThreat}");
            DrawCounts("候选阵容", wave.Candidates);
            DrawCounts("待生成队列", wave.SpawnQueue);
        }
        GUILayout.EndVertical();
    }

    private void DrawPoolColumn(float width)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
        GUILayout.Label("敌人对象池");
        if (snapshot.Pools.Count == 0)
        {
            GUILayout.Label("对象池未运行");
        }
        for (int index = 0; index < snapshot.Pools.Count; index++)
        {
            EnemyPoolRuntimeDiagnostics pool = snapshot.Pools[index];
            GUILayout.Label(pool.Template, GUI.skin.box);
            GUILayout.Label($"容量 {pool.Capacity} · 活动 {pool.Active} · 空闲 {pool.Available}");
            GUILayout.Label($"复用 {pool.Reuse} · 扩容 {pool.Expansion}");
        }
        GUILayout.EndVertical();
    }

    private static void DrawCounts(
        string title,
        IReadOnlyList<RuntimeDiagnosticCount> counts)
    {
        GUILayout.Label(title, GUI.skin.box);
        if (counts == null || counts.Count == 0)
        {
            GUILayout.Label("—");
            return;
        }
        for (int index = 0; index < counts.Count; index++)
        {
            GUILayout.Label($"{counts[index].Label}: {counts[index].Count}");
        }
    }
}
#endif
