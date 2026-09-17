using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FPS.Networking.Acceptance
{
    [DisallowMultipleComponent]
    internal sealed class Issue100OffscreenRecorder : MonoBehaviour
    {
        private const int Width = 1280;
        private const int Height = 720;
        private const int FramesPerSecond = 30;
        private readonly ConcurrentQueue<byte[]> completedFrames = new();
        private Issue100AcceptanceRuntime runtime;
        private RenderTexture renderTexture;
        private Camera captureCamera;
        private FileStream output;
        private Text subtitle;
        private Font subtitleFont;
        private double nextFrameAt;
        private int pendingReadbacks;
        private bool stopping;

        private void Start()
        {
            runtime = GetComponent<Issue100AcceptanceRuntime>();
            if (runtime?.Options == null || !runtime.Options.RecordVideo)
            {
                enabled = false;
                return;
            }
            try
            {
                output = new FileStream(runtime.Options.VideoPipePath,
                    FileMode.Open, FileAccess.Write, FileShare.ReadWrite,
                    1024 * 1024, FileOptions.SequentialScan);
                renderTexture = new RenderTexture(Width, Height, 24,
                    RenderTextureFormat.ARGB32)
                {
                    name = "Issue100 Offscreen Capture",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                renderTexture.Create();
                runtime.Evidence.Passed("video.capture-started",
                    detail: $"{Width}x{Height}@{FramesPerSecond};audio=disabled");
            }
            catch (Exception exception)
            {
                runtime.Fail("video.capture-started",
                    "离屏录像管道无法打开：" + exception.Message);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            DrainFrames();
            if (stopping || output == null || renderTexture == null) return;
            if (captureCamera == null)
            {
                captureCamera = Camera.main ?? FindObjectsByType<Camera>(
                        FindObjectsInactive.Exclude)
                    .FirstOrDefault(value => value.enabled);
                if (captureCamera == null) return;
                BuildSubtitle(captureCamera);
            }

            UpdateSubtitle();
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < nextFrameAt || Volatile.Read(ref pendingReadbacks) >= 2)
                return;
            nextFrameAt = now + 1d / FramesPerSecond;
            RenderTexture previous = captureCamera.targetTexture;
            captureCamera.targetTexture = renderTexture;
            captureCamera.Render();
            captureCamera.targetTexture = previous;
            Interlocked.Increment(ref pendingReadbacks);
            AsyncGPUReadback.Request(renderTexture, 0, TextureFormat.BGRA32,
                HandleReadback);
        }

        private void HandleReadback(AsyncGPUReadbackRequest request)
        {
            try
            {
                if (!stopping && !request.hasError)
                {
                    NativeArray<byte> data = request.GetData<byte>();
                    completedFrames.Enqueue(data.ToArray());
                }
            }
            finally
            {
                Interlocked.Decrement(ref pendingReadbacks);
            }
        }

        private void DrainFrames()
        {
            if (output == null) return;
            try
            {
                while (completedFrames.TryDequeue(out byte[] frame))
                    output.Write(frame, 0, frame.Length);
            }
            catch (IOException exception)
            {
                runtime?.Fail("video.capture",
                    "FFmpeg 录像管道中断：" + exception.Message);
                stopping = true;
            }
        }

        private void BuildSubtitle(Camera camera)
        {
            var canvasObject = new GameObject("Issue100 Video Subtitles");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.1f,
                0.5f);
            canvas.sortingOrder = 32000;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(Width, Height);

            var backgroundObject = new GameObject("Subtitle Background");
            backgroundObject.transform.SetParent(canvasObject.transform, false);
            RectTransform backgroundRect = backgroundObject.AddComponent<
                RectTransform>();
            // The first-person weapon is rendered by a later camera and can
            // cover camera-space UI. Keep captions inside the left safe area
            // so every explanation remains readable in the encoded frame.
            backgroundRect.anchorMin = new Vector2(0.03f, 0.025f);
            backgroundRect.anchorMax = new Vector2(0.48f, 0.145f);
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            Image background = backgroundObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.86f);

            var textObject = new GameObject("Subtitle");
            textObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24f, 8f);
            textRect.offsetMax = new Vector2(-24f, -8f);
            subtitle = textObject.AddComponent<Text>();
            subtitle.alignment = TextAnchor.MiddleCenter;
            subtitle.fontSize = 24;
            subtitle.color = Color.white;
            subtitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            subtitle.verticalOverflow = VerticalWrapMode.Truncate;
            subtitleFont = Font.CreateDynamicFontFromOSFont(new[]
            {
                "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC",
                "Arial Unicode MS", "Arial"
            }, 32);
            subtitle.font = subtitleFont;
        }

        private void UpdateSubtitle()
        {
            if (subtitle == null || runtime?.Evidence == null) return;
            subtitle.text = Caption(runtime.Evidence.CurrentStep);
        }

        private static string Caption(string step) => step switch
        {
            "account.register" => "账号注册：创建独立的多人身份",
            "account.login" => "账号登录：校验身份并签发短期凭证",
            "room.create" => "创建战局：启动独立权威服务器",
            "room.join" => "加入战局：两个客户端连接同一战局",
            "character.select" => "角色选择：同步玩家外观",
            "lobby.ready" => "准备完成：等待双方角色和快照就绪",
            "scene.load" => "加载 CityNew 战斗场景",
            "movement.walk" => "移动验证：服务器权威同步 WASD 输入",
            "movement.sprint" => "冲刺验证：同步速度与姿态",
            "movement.crouch" => "下蹲验证：同步角色姿态",
            "movement.jump" => "跳跃验证：服务器判定落地状态",
            "combat.aim" => "瞄准验证：同步 ADS 状态",
            "combat.fire" => "射击验证：命中反馈、延迟与预测校正",
            "combat.reload" => "换弹验证：弹药状态由服务器确认",
            "wave.complete" => "合作清敌：完成敌人波次",
            "drop.spawn" => "权威掉落：敌人死亡后生成战利品",
            "inventory.pickup" => "背包验证：拾取并同步物品",
            "inventory.use" => "消耗品验证：服务器确认使用结果",
            "upgrade.select" => "肉鸽升级：选择并应用成长效果",
            "mission.terminal" => "任务推进：合作激活终端",
            "reconnect.ready-to-disconnect" => "网络异常：客户端 B 主动断线",
            "reconnect.restore" => "断线重连：恢复原角色与完整战局状态",
            "mission.extraction" => "任务收尾：进入撤离区域",
            "match.settlement" => "战局结算：服务器确认胜利结果",
            _ => "双人合作 FPS：真实专服与两个独立客户端"
        };

        private void OnApplicationQuit() => StopCapture();

        private void OnDestroy() => StopCapture();

        private void StopCapture()
        {
            if (stopping) return;
            stopping = true;
            DrainFrames();
            output?.Flush();
            output?.Dispose();
            output = null;
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
            if (subtitleFont != null)
                Destroy(subtitleFont);
        }
    }
}
