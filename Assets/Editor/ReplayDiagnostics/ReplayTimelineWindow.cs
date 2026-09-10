using System;
using System.Collections.Generic;
using System.IO;
using FPS.Determinism;
using UnityEditor;
using UnityEngine;

public sealed class ReplayTimelineWindow : EditorWindow
{
    private const int PageSize = 100;
    private static readonly ReplayTimelineEventKind[] FilterKinds =
    {
        ReplayTimelineEventKind.Input,
        ReplayTimelineEventKind.Shot,
        ReplayTimelineEventKind.EnemyKilled,
        ReplayTimelineEventKind.Upgrade,
        ReplayTimelineEventKind.Loot,
        ReplayTimelineEventKind.Wave,
        ReplayTimelineEventKind.EnemySpawn,
        ReplayTimelineEventKind.Elite,
        ReplayTimelineEventKind.ChecksumDivergence
    };

    private ReplayTimeline timeline;
    private ReplayTimelineEventKind visibleKinds = ReplayTimelineEventKind.All;
    private ReplayTimelinePage page;
    private ReplayTimelineItem selected;
    private ReplayTimelineDetails details;
    private Vector2 eventScroll;
    private Vector2 detailScroll;
    private int pageIndex;
    private string sourceLabel = "尚未加载记录";
    private string message = "可加载 JSON 文件；运行游戏时也可读取当前战局。";
    private MessageType messageType = MessageType.Info;

    [MenuItem("FPS/Replay/调试时间轴")]
    public static void Open()
    {
        var window = GetWindow<ReplayTimelineWindow>("Replay 时间轴");
        window.minSize = new Vector2(920, 520);
        window.Show();
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, messageType);
        if (timeline == null) return;

        DrawFilters();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.53f)))
                DrawTimeline();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                DrawDetails();
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("加载 JSON", EditorStyles.toolbarButton, GUILayout.Width(80)))
                LoadFile();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || RunDeterminismRecorder.Active?.Record == null))
            {
                if (GUILayout.Button("当前战局", EditorStyles.toolbarButton, GUILayout.Width(80)))
                    LoadCurrentRecord();
            }
            GUILayout.Space(8);
            GUILayout.Label(sourceLabel, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("跳到首个偏差", EditorStyles.toolbarButton, GUILayout.Width(100)))
                JumpToDivergence();
        }
    }

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("筛选", EditorStyles.boldLabel, GUILayout.Width(38));
            foreach (ReplayTimelineEventKind kind in FilterKinds)
            {
                bool enabled = (visibleKinds & kind) != 0;
                bool changed = GUILayout.Toggle(enabled, KindLabel(kind), EditorStyles.miniButton);
                if (changed != enabled)
                {
                    visibleKinds = changed ? visibleKinds | kind : visibleKinds & ~kind;
                    pageIndex = 0;
                    selected = null;
                    details = null;
                    RefreshPage();
                }
            }
        }
    }

    private void DrawTimeline()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(
                $"Seed {timeline.RunSeed}  ·  Tick 0–{timeline.LastTick}  ·  {page.TotalItems} 项",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(pageIndex <= 0))
                if (GUILayout.Button("◀", GUILayout.Width(28))) SetPage(pageIndex - 1);
            GUILayout.Label(page.TotalPages == 0 ? "0 / 0" : $"{pageIndex + 1} / {page.TotalPages}",
                GUILayout.Width(60));
            using (new EditorGUI.DisabledScope(page.TotalPages == 0 || pageIndex + 1 >= page.TotalPages))
                if (GUILayout.Button("▶", GUILayout.Width(28))) SetPage(pageIndex + 1);
        }

        eventScroll = EditorGUILayout.BeginScrollView(eventScroll);
        foreach (ReplayTimelineItem item in page.Items)
        {
            GUIStyle style = selected == item ? EditorStyles.selectionRect : EditorStyles.label;
            string entity = string.IsNullOrEmpty(item.EntityId) ? string.Empty : $"  [{item.EntityId}]";
            Color old = GUI.color;
            if (item.Kind == ReplayTimelineEventKind.ChecksumDivergence) GUI.color = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button($"T{item.Tick,7}  {item.Title,-12}{entity}", style, GUILayout.Height(21)))
                Select(item);
            GUI.color = old;
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawDetails()
    {
        EditorGUILayout.LabelField("事件详情", EditorStyles.boldLabel);
        if (details == null)
        {
            EditorGUILayout.LabelField("选择左侧事件查看实体、载荷和前后状态。", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
        EditorGUILayout.LabelField($"Tick {details.Item.Tick} · {details.Item.Title}");
        EditorGUILayout.LabelField("相关实体", string.IsNullOrEmpty(details.EntityId) ? "—" : details.EntityId);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("载荷", EditorStyles.boldLabel);
        foreach (KeyValuePair<string, string> pair in details.Payload)
            EditorGUILayout.SelectableLabel($"{pair.Key}: {pair.Value}", GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("状态摘要", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("前快照", CheckpointLabel(details.Before));
        EditorGUILayout.LabelField("后快照", CheckpointLabel(details.After));
        if (details.ChangedFields.Count == 0)
            EditorGUILayout.LabelField("两个相邻快照间无字段变化，或缺少相邻快照。", EditorStyles.wordWrappedMiniLabel);
        else
            foreach (string change in details.ChangedFields)
                EditorGUILayout.SelectableLabel(change, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        EditorGUILayout.EndScrollView();
    }

    private void LoadFile()
    {
        string path = EditorUtility.OpenFilePanel("打开 RunRecord", string.Empty, "json");
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            ReplayTimelineLoadResult result = ReplayTimeline.LoadJson(File.ReadAllText(path));
            if (!result.Success)
            {
                timeline = null;
                sourceLabel = Path.GetFileName(path);
                SetMessage(result.Message, MessageType.Error);
                return;
            }
            SetTimeline(result.Timeline, Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            timeline = null;
            SetMessage("读取记录失败：" + exception.Message, MessageType.Error);
        }
    }

    private void LoadRecord(RunRecord record, ReplayDivergence divergence, string label)
    {
        try { SetTimeline(new ReplayTimeline(record, divergence), label); }
        catch (Exception exception) { SetMessage("读取当前战局失败：" + exception.Message, MessageType.Error); }
    }

    private void LoadCurrentRecord()
    {
        RunReplayController controller = FindRuntimeController();
        RunRecord record = controller?.LoadedRecord ?? RunDeterminismRecorder.Active?.Record;
        LoadRecord(record, controller?.Divergence,
            controller?.LoadedRecord != null ? "当前 Replay" : "当前战局");
    }

    private void SetTimeline(ReplayTimeline value, string label)
    {
        timeline = value;
        sourceLabel = label;
        visibleKinds = ReplayTimelineEventKind.All;
        pageIndex = 0;
        selected = null;
        details = null;
        RefreshPage();
        SetMessage("记录已加载。列表每页最多创建 100 行，不会一次实例化全部事件。", MessageType.Info);
    }

    private void RefreshPage()
    {
        if (timeline == null) return;
        page = timeline.Query(ReplayTimelineFilter.Only(visibleKinds), pageIndex, PageSize);
        pageIndex = page.PageIndex;
    }

    private void SetPage(int value)
    {
        pageIndex = Mathf.Max(0, value);
        selected = null;
        details = null;
        eventScroll = Vector2.zero;
        RefreshPage();
    }

    private void Select(ReplayTimelineItem item)
    {
        selected = item;
        details = timeline.Describe(item);
        detailScroll = Vector2.zero;
    }

    private void JumpToDivergence()
    {
        if (timeline == null) return;
        visibleKinds |= ReplayTimelineEventKind.ChecksumDivergence;
        ReplayTimelineLocation location = timeline.FindFirstDivergence(
            ReplayTimelineFilter.Only(visibleKinds), PageSize);
        if (!location.Found)
        {
            SetMessage("当前记录或回放会话没有 Checksum 偏差。", MessageType.Info);
            return;
        }
        SetPage(location.PageIndex);
        Select(location.Item);
        SetMessage($"已定位到 Tick {location.Item.Tick} 的首个 Checksum 偏差。", MessageType.Warning);
    }

    private static RunReplayController FindRuntimeController() =>
        UnityEngine.Object.FindAnyObjectByType<RunReplayController>();

    private void SetMessage(string value, MessageType type)
    {
        message = value;
        messageType = type;
    }

    private static string CheckpointLabel(ReplayTimelineStateSummary summary) =>
        summary.Exists ? $"Tick {summary.Tick}（{summary.Fields.Count} 字段）" : "无";

    private static string KindLabel(ReplayTimelineEventKind kind)
    {
        switch (kind)
        {
            case ReplayTimelineEventKind.Input: return "输入";
            case ReplayTimelineEventKind.Shot: return "射击";
            case ReplayTimelineEventKind.EnemyKilled: return "击杀";
            case ReplayTimelineEventKind.Upgrade: return "升级";
            case ReplayTimelineEventKind.Loot: return "掉落";
            case ReplayTimelineEventKind.Wave: return "波次";
            case ReplayTimelineEventKind.EnemySpawn: return "出生";
            case ReplayTimelineEventKind.Elite: return "精英";
            case ReplayTimelineEventKind.ChecksumDivergence: return "偏差";
            default: return kind.ToString();
        }
    }
}
