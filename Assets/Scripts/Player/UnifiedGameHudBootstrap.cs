using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class UnifiedGameHudBootstrap : MonoBehaviour
{
    public UnifiedGameHud Hud { get; private set; }

    private IEnumerator Start()
    {
        yield return null;

        Hud = Object.FindAnyObjectByType<UnifiedGameHud>();

        if (Hud == null)
        {
            Hud = CreateHud();
        }

        Hud.Bind(gameObject);
        EnsureEventSystem();
    }

    private static UnifiedGameHud CreateHud()
    {
        GameObject canvasObject = new GameObject(
            "GameUICanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(UnifiedGameHud));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode =
            CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        return canvasObject.GetComponent<UnifiedGameHud>();
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        GameObject eventSystem = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystem.transform.SetAsLastSibling();
    }
}
