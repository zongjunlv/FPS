#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.GameplayEffects
{
    [DefaultExecutionOrder(10000)]
    public sealed class GameplayEffectRuntimeInspector : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private static readonly Color HeaderColor =
            new(0.18f, 0.82f, 0.78f, 1f);
        private readonly List<GameplayEffectDebugTargetSnapshot> targets =
            new();
        private Vector2 targetScroll;
        private Vector2 detailScroll;
        private Rect windowRect;
        private float nextRefreshTime;
        private GUIStyle headerStyle;
        private GUIStyle subheaderStyle;
        private GUIStyle mutedStyle;
        private GUIStyle stepStyle;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool cursorStateCaptured;

        public static GameplayEffectRuntimeInspector Instance { get; private set; }
        public bool IsVisible { get; private set; }
        public GameObject SelectedTarget { get; private set; }
        public int ActiveTargetCount => targets.Count;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            windowRect = new Rect(24f, 24f, 1060f, 680f);
            RefreshTargets();
        }

        private void OnDestroy()
        {
            RestoreCursor();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextRefreshTime)
            {
                RefreshTargets();
            }
        }

        private void OnGUI()
        {
            if (!IsVisible)
            {
                return;
            }

            EnsureStyles();
            windowRect.width = Mathf.Min(1060f, Screen.width - 32f);
            windowRect.height = Mathf.Min(680f, Screen.height - 32f);
            windowRect = GUI.Window(
                GetEntityId().GetHashCode(),
                windowRect,
                DrawWindow,
                "GAMEPLAY EFFECT 运行时检查器   [F8 关闭]");
        }

        public void SetVisible(bool visible)
        {
            if (IsVisible == visible)
            {
                return;
            }

            IsVisible = visible;

            if (visible)
            {
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                cursorStateCaptured = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RefreshTargets();
            }
            else
            {
                RestoreCursor();
            }
        }

        public bool SelectTarget(GameObject target)
        {
            if (target == null || !target.activeInHierarchy)
            {
                SelectedTarget = null;
                return false;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].Target == target)
                {
                    SelectedTarget = target;
                    return true;
                }
            }

            SelectedTarget = null;
            return false;
        }

        public void RefreshTargets()
        {
            nextRefreshTime = Time.unscaledTime + RefreshInterval;
            IReadOnlyList<GameplayEffectDebugTargetSnapshot> snapshot =
                GameplayEffectDebugRegistry.CaptureActiveTargets();
            targets.Clear();
            targets.AddRange(snapshot);

            if (SelectedTarget == null)
            {
                return;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].Target == SelectedTarget)
                {
                    return;
                }
            }

            SelectedTarget = null;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            DrawTargetList();
            DrawTargetDetails();
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 24f));
        }

        private void DrawTargetList()
        {
            GUILayout.BeginVertical(GUILayout.Width(240f));
            GUILayout.Label($"活动目标  {targets.Count}", headerStyle);
            targetScroll = GUILayout.BeginScrollView(targetScroll);

            for (int index = 0; index < targets.Count; index++)
            {
                GameplayEffectDebugTargetSnapshot target = targets[index];
                bool selected = target.Target == SelectedTarget;
                string label = selected
                    ? $"> {target.DisplayName}"
                    : target.DisplayName;

                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    SelectedTarget = target.Target;
                    detailScroll = Vector2.zero;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.Label("池化回收或销毁后会自动失焦", mutedStyle);
            GUILayout.EndVertical();
        }

        private void DrawTargetDetails()
        {
            GUILayout.BeginVertical();

            if (SelectedTarget == null)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("请选择玩家或一个活动敌人", headerStyle);
                GUILayout.Label(
                    "目标离开对象池租约后不会自动跳到其他对象。",
                    mutedStyle);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                return;
            }

            GameplayEffectDebugTargetSnapshot selected = FindSelected();

            if (selected == null)
            {
                SelectedTarget = null;
                GUILayout.EndVertical();
                return;
            }

            GUILayout.Label(selected.DisplayName, headerStyle);
            GUILayout.Label(
                $"Entity ID: {selected.Target.GetEntityId()}   " +
                $"Runtime 通道: {selected.Runtimes.Count}",
                mutedStyle);
            detailScroll = GUILayout.BeginScrollView(detailScroll);

            for (int index = 0; index < selected.Runtimes.Count; index++)
            {
                DrawRuntime(selected.Runtimes[index]);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawRuntime(GameplayEffectRuntime runtime)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(
                $"{runtime.DebugChannel}  ·  " +
                $"实例 {runtime.ActiveInstances.Count}",
                subheaderStyle);

            if (runtime.ActiveInstances.Count == 0)
            {
                GUILayout.Label("当前无活动 Effect", mutedStyle);
            }

            for (int index = 0;
                 index < runtime.ActiveInstances.Count;
                 index++)
            {
                DrawInstance(runtime.ActiveInstances[index]);
            }

            IReadOnlyList<GameplayAttributeEvaluationTrace> traces =
                runtime.CaptureAttributeTraces();

            for (int index = 0; index < traces.Count; index++)
            {
                DrawTrace(traces[index]);
            }

            GUILayout.EndVertical();
        }

        private void DrawInstance(GameplayEffectInstance instance)
        {
            GameplayEffectDefinition definition = instance.Definition;
            GUILayout.Label(
                $"[实例 {instance.InstanceId}]  " +
                $"{definition?.StableId ?? "<missing>"}",
                stepStyle);
            GUILayout.Label(
                $"来源: {instance.Context.SourceId} / " +
                $"{FormatObject(instance.Context.Source)}    " +
                $"层数: {instance.EffectiveStackCount}    " +
                $"剩余: {FormatDuration(instance)}",
                mutedStyle);
            IReadOnlyList<string> tags = definition?.GameplayTags;
            GUILayout.Label(
                tags != null && tags.Count > 0
                    ? $"Gameplay Tag: {string.Join("  |  ", tags)}"
                    : "Gameplay Tag: <none>",
                mutedStyle);
        }

        private void DrawTrace(GameplayAttributeEvaluationTrace trace)
        {
            string baseText = trace.HasKnownBaseValue
                ? trace.BaseValue.ToString("0.###")
                : "待首次求值";
            GUILayout.Space(5f);
            GUILayout.Label(
                $"属性 {trace.Attribute}  ·  基础 {baseText}  →  " +
                $"最终 {trace.FinalValue:0.###}",
                stepStyle);

            for (int index = 0; index < trace.Steps.Count; index++)
            {
                GameplayModifierEvaluationStep step = trace.Steps[index];
                string operation = step.Operation switch
                {
                    GameplayModifierOperation.Add => "加算",
                    GameplayModifierOperation.Multiply => "倍率池",
                    GameplayModifierOperation.Override => "覆盖",
                    _ => step.Operation.ToString()
                };
                string state = step.Applied ? string.Empty : " [未采用]";
                GUILayout.Label(
                    $"  {step.Sequence:00}. {operation} " +
                    $"{step.Magnitude:+0.###;-0.###;0}  " +
                    $"{step.InputValue:0.###} → {step.OutputValue:0.###}  " +
                    $"[{step.EffectId} / {step.SourceId}]" + state,
                    stepStyle);
            }
        }

        private GameplayEffectDebugTargetSnapshot FindSelected()
        {
            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].Target == SelectedTarget)
                {
                    return targets[index];
                }
            }

            return null;
        }

        private void EnsureStyles()
        {
            if (headerStyle != null)
            {
                return;
            }

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = HeaderColor }
            };
            subheaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.68f, 0.72f, 0.76f) }
            };
            stepStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.9f, 0.94f, 0.96f) }
            };
        }

        private void RestoreCursor()
        {
            if (!cursorStateCaptured)
            {
                return;
            }

            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            cursorStateCaptured = false;
        }

        private static string FormatDuration(GameplayEffectInstance instance)
        {
            if (instance.Definition == null)
            {
                return "0.00s";
            }

            return instance.Definition.DurationPolicy switch
            {
                GameplayEffectDurationPolicy.Persistent => "∞",
                GameplayEffectDurationPolicy.Instant => "即时",
                _ => $"{instance.RemainingDuration:0.00}s"
            };
        }

        private static string FormatObject(UnityEngine.Object value)
        {
            return value != null
                ? $"{value.name} ({value.GetType().Name})"
                : "<none>";
        }
    }

}
#endif
