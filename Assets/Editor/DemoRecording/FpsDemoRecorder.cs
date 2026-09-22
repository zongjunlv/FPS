#if UNITY_EDITOR

using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace FPS.EditorTools.DemoRecording
{
    /// <summary>
    /// Records the Game view and game audio on the same fixed-rate timeline.
    /// Raw captures are written below the project Recordings folder, which is git-ignored.
    /// </summary>
    [InitializeOnLoad]
    public static class FpsDemoRecorder
    {
        private const string StartMenu = "FPS/演示录制/后台录制/开始";
        private const string StopMenu = "FPS/演示录制/后台录制/停止";
        private const string SmokeTestMenu = "FPS/演示录制/后台录制/运行 3 秒自检";
        private const string AutomatedShowcaseMenu =
            "FPS/演示录制/后台录制/录制完整自动演示";
        private const string ArmedSessionKey = "FPS.DemoRecording.Armed";
        private const string SmokeTestSessionKey = "FPS.DemoRecording.SmokeTest";
        private const string AutomatedSessionKey =
            "FPS.DemoRecording.AutomatedShowcase";
        private const double StartDelaySeconds = 1d;
        private const double SmokeTestDurationSeconds = 3d;

        private static RecorderController recorderController;
        private static RecorderControllerSettings controllerSettings;
        private static MovieRecorderSettings movieSettings;
        private static string currentOutputPath;
        private static double earliestStartTime;
        private static double smokeTestStopTime = double.PositiveInfinity;
        private static bool previousRunInBackground;
        private static bool runInBackgroundCaptured;

        static FpsDemoRecorder()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update -= UpdateBackgroundSession;
            EditorApplication.update += UpdateBackgroundSession;
            AssemblyReloadEvents.beforeAssemblyReload += StopRecordingInternal;
        }

        public static bool IsRecording =>
            recorderController != null && recorderController.IsRecording();

        public static string CurrentOutputPath => currentOutputPath ?? string.Empty;

        public static bool IsAutomatedShowcase =>
            SessionState.GetBool(AutomatedSessionKey, false);

        /// <summary>
        /// Arms a recording without focusing the Editor. When called outside Play Mode,
        /// recording starts automatically after the active scene has entered Play Mode.
        /// </summary>
        [MenuItem(StartMenu)]
        public static void StartBackgroundRecording()
        {
            if (IsRecording || SessionState.GetBool(ArmedSessionKey, false))
            {
                Debug.LogWarning($"[FPS Demo Recorder] 后台录制已经启动或正在准备：{CurrentOutputPath}");
                return;
            }

            SessionState.SetBool(ArmedSessionKey, true);
            earliestStartTime = EditorApplication.timeSinceStartup + StartDelaySeconds;

            if (EditorApplication.isPlaying)
            {
                EnableBackgroundRuntime();
                return;
            }

            Debug.Log("[FPS Demo Recorder] 已准备后台录制，正在进入 Play Mode；不会切换编辑器焦点。");
            EditorApplication.EnterPlaymode();
        }

        [MenuItem(StartMenu, true)]
        private static bool ValidateStartBackgroundRecording()
        {
            return !IsRecording && !SessionState.GetBool(ArmedSessionKey, false);
        }

        [MenuItem(SmokeTestMenu)]
        public static void RunBackgroundSmokeTest()
        {
            if (IsRecording || SessionState.GetBool(ArmedSessionKey, false))
            {
                Debug.LogWarning("[FPS Demo Recorder] 已有后台录制任务，不能同时运行自检。");
                return;
            }

            SessionState.SetBool(SmokeTestSessionKey, true);
            StartBackgroundRecording();
        }

        [MenuItem(SmokeTestMenu, true)]
        private static bool ValidateBackgroundSmokeTest()
        {
            return !IsRecording && !SessionState.GetBool(ArmedSessionKey, false);
        }

        [MenuItem(AutomatedShowcaseMenu)]
        public static void RecordAutomatedShowcase()
        {
            if (IsRecording || SessionState.GetBool(ArmedSessionKey, false))
            {
                Debug.LogWarning("[FPS Demo Recorder] 已有后台录制任务，不能重复启动自动演示。");
                return;
            }

            SessionState.SetBool(AutomatedSessionKey, true);
            StartBackgroundRecording();
        }

        [MenuItem(AutomatedShowcaseMenu, true)]
        private static bool ValidateAutomatedShowcase()
        {
            return !IsRecording && !SessionState.GetBool(ArmedSessionKey, false);
        }

        private static void BeginRecording()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[FPS Demo Recorder] Play Mode 尚未就绪，后台录制没有启动。");
                CancelPendingSession();
                return;
            }

            EnableBackgroundRuntime();
            EnsureRuntimeAudioListener();

            var outputDirectory = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Recordings", "FPSDemoRaw"));
            Directory.CreateDirectory(outputDirectory);

            currentOutputPath = Path.Combine(
                outputDirectory,
                $"fps-demo-{DateTime.Now:yyyyMMdd-HHmmss}");

            controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 60f;
            controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            controllerSettings.CapFrameRate = true;
            controllerSettings.ExitPlayMode = false;

            movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movieSettings.name = "FPS Demo Movie Recorder";
            movieSettings.Enabled = true;
            movieSettings.CaptureAudio = true;
            movieSettings.CaptureAlpha = false;
            movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
            };
            movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080
            };
            movieSettings.OutputFile = currentOutputPath;

            controllerSettings.AddRecorderSettings(movieSettings);
            recorderController = new RecorderController(controllerSettings);

            RecorderOptions.VerboseMode = false;
            recorderController.PrepareRecording();
            if (!recorderController.StartRecording())
            {
                Debug.LogError("[FPS Demo Recorder] Recorder 启动失败，请检查 Console。");
                CancelPendingSession();
                ReleaseRecorderObjects();
                return;
            }

            SessionState.SetBool(ArmedSessionKey, false);
            if (SessionState.GetBool(SmokeTestSessionKey, false))
            {
                smokeTestStopTime = EditorApplication.timeSinceStartup + SmokeTestDurationSeconds;
            }

            if (IsAutomatedShowcase)
            {
                GameObject directorObject = new GameObject(
                    "FPS Automated Demo Director");
                FpsDemoAutoplayDirector director =
                    directorObject.AddComponent<FpsDemoAutoplayDirector>();
                director.Completed += FinishAutomatedShowcase;
            }

            Debug.Log(
                $"[FPS Demo Recorder] 后台录制已开始：1920×1080 / 60 FPS / 同步游戏音频。" +
                $"Unity 可保持在后台：{currentOutputPath}.mp4");
        }

        private static void EnsureRuntimeAudioListener()
        {
            AudioListener[] listeners =
                UnityEngine.Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            foreach (AudioListener listener in listeners)
            {
                if (listener.enabled && listener.gameObject.activeInHierarchy)
                {
                    return;
                }
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            AudioListener cameraListener = camera.GetComponent<AudioListener>();
            if (cameraListener == null)
            {
                cameraListener = camera.gameObject.AddComponent<AudioListener>();
            }

            cameraListener.enabled = true;
        }

        [MenuItem(StopMenu)]
        public static void StopRecording()
        {
            if (!IsRecording)
            {
                if (SessionState.GetBool(ArmedSessionKey, false))
                {
                    CancelPendingSession();
                    Debug.Log("[FPS Demo Recorder] 已取消等待启动的后台录制。");
                    return;
                }

                Debug.LogWarning("[FPS Demo Recorder] 当前没有正在录制的后台片段。");
                return;
            }

            var completedPath = currentOutputPath + ".mp4";
            StopRecordingInternal();
            Debug.Log($"[FPS Demo Recorder] 录制完成：{completedPath}");
        }

        [MenuItem(StopMenu, true)]
        private static bool ValidateStopRecording()
        {
            return IsRecording || SessionState.GetBool(ArmedSessionKey, false);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode &&
                SessionState.GetBool(ArmedSessionKey, false))
            {
                earliestStartTime = EditorApplication.timeSinceStartup + StartDelaySeconds;
                EnableBackgroundRuntime();
            }

            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopRecordingInternal();
                CancelPendingSession();
            }
        }

        private static void UpdateBackgroundSession()
        {
            if (SessionState.GetBool(ArmedSessionKey, false) &&
                EditorApplication.isPlaying &&
                !EditorApplication.isPaused &&
                EditorApplication.timeSinceStartup >= earliestStartTime &&
                Camera.main != null)
            {
                BeginRecording();
            }

            if (IsRecording &&
                SessionState.GetBool(SmokeTestSessionKey, false) &&
                EditorApplication.timeSinceStartup >= smokeTestStopTime)
            {
                string completedPath = currentOutputPath + ".mp4";
                StopRecordingInternal();
                SessionState.SetBool(SmokeTestSessionKey, false);
                Debug.Log($"[FPS Demo Recorder] 3 秒后台录制自检完成：{completedPath}");
                EditorApplication.ExitPlaymode();
            }
        }

        private static void EnableBackgroundRuntime()
        {
            if (!runInBackgroundCaptured)
            {
                previousRunInBackground = Application.runInBackground;
                runInBackgroundCaptured = true;
            }

            Application.runInBackground = true;
        }

        private static void CancelPendingSession()
        {
            SessionState.SetBool(ArmedSessionKey, false);
            SessionState.SetBool(SmokeTestSessionKey, false);
            SessionState.SetBool(AutomatedSessionKey, false);
            smokeTestStopTime = double.PositiveInfinity;
        }

        internal static void FinishAutomatedShowcase(
            bool succeeded,
            string details)
        {
            string completedPath = string.IsNullOrEmpty(currentOutputPath)
                ? string.Empty
                : currentOutputPath + ".mp4";
            SessionState.SetBool(AutomatedSessionKey, false);
            StopRecordingInternal();

            if (succeeded)
            {
                Debug.Log(
                    $"[FPS Demo Recorder] 完整后台自动演示录制完成：{completedPath}");
            }
            else
            {
                Debug.LogError(
                    $"[FPS Demo Recorder] 自动演示提前终止：{details}");
            }

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlaying)
                {
                    EditorApplication.ExitPlaymode();
                }
            };
        }

        private static void StopRecordingInternal()
        {
            if (recorderController != null && recorderController.IsRecording())
            {
                recorderController.StopRecording();
            }

            ReleaseRecorderObjects();
            smokeTestStopTime = double.PositiveInfinity;

            if (runInBackgroundCaptured && EditorApplication.isPlaying)
            {
                Application.runInBackground = previousRunInBackground;
            }

            runInBackgroundCaptured = false;
        }

        private static void ReleaseRecorderObjects()
        {
            recorderController = null;

            if (movieSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(movieSettings);
                movieSettings = null;
            }

            if (controllerSettings != null)
            {
                UnityEngine.Object.DestroyImmediate(controllerSettings);
                controllerSettings = null;
            }
        }
    }
}

#endif
