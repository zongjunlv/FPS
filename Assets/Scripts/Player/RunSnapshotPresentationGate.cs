using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps an opaque overlay alive across a snapshot scene reload so the fresh
/// scene cannot be presented before all restored systems have published state.
/// </summary>
public sealed class RunSnapshotPresentationGate : MonoBehaviour
{
    public const string ObjectName = "Run Snapshot Presentation Gate";

    private static RunSnapshotPresentationGate instance;
    private Coroutine releaseRoutine;

    public static bool IsVisible => instance != null &&
                                    instance.gameObject.activeInHierarchy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        instance = null;
    }

    public static void Show()
    {
        if (instance == null)
        {
            GameObject root = new GameObject(
                ObjectName,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster),
                typeof(RunSnapshotPresentationGate));
            instance = root.GetComponent<RunSnapshotPresentationGate>();

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32760;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            GameObject cover = new GameObject(
                "Opaque Cover",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform rect = cover.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Image image = cover.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;

            DontDestroyOnLoad(root);
        }

        if (instance.releaseRoutine != null)
        {
            instance.StopCoroutine(instance.releaseRoutine);
            instance.releaseRoutine = null;
        }

        instance.gameObject.SetActive(true);
        instance.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
    }

    public static void ReleaseWhenRestoredHudIsReady()
    {
        if (instance == null || instance.releaseRoutine != null)
        {
            return;
        }

        instance.releaseRoutine = instance.StartCoroutine(
            instance.ReleaseAfterHudRefresh());
    }

    public static void HideImmediately()
    {
        if (instance == null)
        {
            return;
        }

        RunSnapshotPresentationGate current = instance;
        instance = null;
        current.gameObject.SetActive(false);
        Destroy(current.gameObject);
    }

    private IEnumerator ReleaseAfterHudRefresh()
    {
        const int maximumWaitFrames = 600;
        for (int frame = 0; frame < maximumWaitFrames; frame++)
        {
            UnifiedGameHudBootstrap bootstrap =
                FindAnyObjectByType<UnifiedGameHudBootstrap>();
            if (bootstrap != null && bootstrap.IsInitialized)
            {
                Canvas.ForceUpdateCanvases();
                // WaitForEndOfFrame is not guaranteed to resume in a
                // headless test player. A normal frame is sufficient there;
                // rendered players still wait for the actual presented frame.
                if (Application.isBatchMode)
                    yield return null;
                else
                    yield return new WaitForEndOfFrame();
                HideImmediately();
                yield break;
            }

            yield return null;
        }

        Debug.LogError(
            "[RunSnapshotPresentationGate] HUD 未在限定时间内完成恢复，已解除读取遮罩以避免黑屏锁死。");
        HideImmediately();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
