#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FPS.Determinism;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Development-only paged overlay for the active deterministic run. Toggle with F9.</summary>
public sealed class RunReplayDebugTimeline : MonoBehaviour
{
    private const int PageSize = 50;
    private RunDeterminismRecorder recorder;
    private RunReplayController replay;
    private ReplayTimeline timeline;
    private ReplayTimelinePage page;
    private ReplayTimelineItem selected;
    private ReplayTimelineDetails details;
    private ReplayTimelineEventKind filter = ReplayTimelineEventKind.All;
    private Vector2 eventScroll;
    private Vector2 detailScroll;
    private int pageIndex;
    private int lastEventCount = -1;
    private ReplayDivergence lastDivergence;
    private bool visible;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private float previousTimeScale;

    public bool IsVisible => visible;

    public void Configure(RunDeterminismRecorder runRecorder, RunReplayController replayController)
    {
        recorder = runRecorder;
        replay = replayController;
    }

    private void Update()
    {
        if (Keyboard.current?.f9Key.wasPressedThisFrame == true) SetVisible(!visible);
        if (!visible) return;
        RunRecord record = replay?.LoadedRecord ?? recorder?.Record;
        if (record == null) return;
        if (record.Events.Count != lastEventCount || replay?.Divergence != lastDivergence)
            Rebuild();
    }

    private void OnDisable()
    {
        if (visible) SetVisible(false);
    }

    private void SetVisible(bool value)
    {
        if (visible == value) return;
        visible = value;
        if (visible)
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousTimeScale = Time.timeScale;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
            Rebuild();
        }
        else
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            Time.timeScale = previousTimeScale;
        }
    }

    private void Rebuild()
    {
        RunRecord record = replay?.LoadedRecord ?? recorder?.Record;
        if (record == null) return;
        lastEventCount = record.Events.Count;
        lastDivergence = replay?.Divergence;
        timeline = new ReplayTimeline(record, lastDivergence);
        RefreshPage();
    }

    private void RefreshPage()
    {
        if (timeline == null) return;
        page = timeline.Query(ReplayTimelineFilter.Only(filter), pageIndex, PageSize);
        pageIndex = page.PageIndex;
    }

    private void OnGUI()
    {
        if (!visible) return;
        float width = Mathf.Min(Screen.width - 40f, 1100f);
        float height = Mathf.Min(Screen.height - 40f, 700f);
        GUILayout.BeginArea(new Rect((Screen.width - width) * 0.5f, 20f, width, height), GUI.skin.window);
        GUILayout.BeginHorizontal();
        GUILayout.Label("REPLAY 调试时间轴 · F9 关闭", GUI.skin.box);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("首个偏差", GUILayout.Width(90))) JumpToDivergence();
        GUILayout.EndHorizontal();
        if (timeline == null || page == null)
        {
            GUILayout.Label("当前没有可用战局记录。");
            GUILayout.EndArea();
            return;
        }

        DrawFilters();
        GUILayout.BeginHorizontal();
        DrawEvents(width * 0.5f, height - 100f);
        DrawDetails(width * 0.47f, height - 100f);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private void DrawFilters()
    {
        GUILayout.BeginHorizontal();
        DrawFilter("输入", ReplayTimelineEventKind.Input);
        DrawFilter("射击", ReplayTimelineEventKind.Shot);
        DrawFilter("击杀", ReplayTimelineEventKind.EnemyKilled);
        DrawFilter("升级", ReplayTimelineEventKind.Upgrade);
        DrawFilter("掉落", ReplayTimelineEventKind.Loot);
        DrawFilter("波次", ReplayTimelineEventKind.Wave);
        DrawFilter("出生", ReplayTimelineEventKind.EnemySpawn);
        DrawFilter("精英", ReplayTimelineEventKind.Elite);
        DrawFilter("AI决策", ReplayTimelineEventKind.AiDecision);
        DrawFilter("导演", ReplayTimelineEventKind.CombatDirector);
        DrawFilter("偏差", ReplayTimelineEventKind.ChecksumDivergence);
        GUILayout.EndHorizontal();
    }

    private void DrawFilter(string label, ReplayTimelineEventKind kind)
    {
        bool oldValue = (filter & kind) != 0;
        bool newValue = GUILayout.Toggle(oldValue, label, GUI.skin.button);
        if (oldValue == newValue) return;
        filter = newValue ? filter | kind : filter & ~kind;
        pageIndex = 0;
        selected = null;
        details = null;
        RefreshPage();
    }

    private void DrawEvents(float width, float height)
    {
        GUILayout.BeginVertical(GUILayout.Width(width));
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Seed {timeline.RunSeed} · {page.TotalItems} 项");
        GUI.enabled = pageIndex > 0;
        if (GUILayout.Button("◀", GUILayout.Width(32))) { pageIndex--; RefreshPage(); }
        GUI.enabled = page.TotalPages > 0 && pageIndex + 1 < page.TotalPages;
        if (GUILayout.Button("▶", GUILayout.Width(32))) { pageIndex++; RefreshPage(); }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        eventScroll = GUILayout.BeginScrollView(eventScroll, GUILayout.Height(height));
        foreach (ReplayTimelineItem item in page.Items)
        {
            string entity = string.IsNullOrEmpty(item.EntityId) ? string.Empty : $" [{item.EntityId}]";
            if (GUILayout.Button($"T{item.Tick}  {item.Title}{entity}"))
            {
                selected = item;
                details = timeline.Describe(item);
                detailScroll = Vector2.zero;
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawDetails(float width, float height)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width));
        GUILayout.Label("事件详情");
        detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(height));
        if (details == null) GUILayout.Label("选择事件查看载荷和前后状态摘要。");
        else
        {
            GUILayout.Label($"Tick {details.Item.Tick} · {details.Item.Title}");
            GUILayout.Label("实体：" + (string.IsNullOrEmpty(details.EntityId) ? "—" : details.EntityId));
            foreach (var pair in details.Payload) GUILayout.Label($"{pair.Key}: {pair.Value}");
            GUILayout.Space(8);
            GUILayout.Label($"前快照：{StateLabel(details.Before)}");
            GUILayout.Label($"后快照：{StateLabel(details.After)}");
            foreach (string change in details.ChangedFields) GUILayout.Label(change);
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void JumpToDivergence()
    {
        if (timeline == null) return;
        filter |= ReplayTimelineEventKind.ChecksumDivergence;
        ReplayTimelineLocation location = timeline.FindFirstDivergence(
            ReplayTimelineFilter.Only(filter), PageSize);
        if (!location.Found) return;
        pageIndex = location.PageIndex;
        RefreshPage();
        selected = location.Item;
        details = timeline.Describe(selected);
    }

    private static string StateLabel(ReplayTimelineStateSummary state) =>
        state.Exists ? $"Tick {state.Tick}（{state.Fields.Count} 字段）" : "无";
}
#endif
