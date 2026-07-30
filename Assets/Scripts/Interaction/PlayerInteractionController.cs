using UnityEngine;

[RequireComponent(
    typeof(PlayerInputReader),
    typeof(PlayerController),
    typeof(Health))]
public sealed class PlayerInteractionController : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float interactionRange = 3f;
    [SerializeField] private LayerMask interactionMask = ~0;

    private readonly RaycastHit[] interactionHits = new RaycastHit[12];
    private PlayerInputReader input;
    private PlayerController player;
    private Health health;
    private Camera interactionCamera;
    private IInteractable focusedInteraction;
    private IInteractable activeInteraction;
    private GUIStyle promptStyle;
    private GUIStyle progressStyle;
    private PlayerHudVisualProfile profile;

    public bool HasFocusedTarget => focusedInteraction != null;
    public bool IsInteracting => activeInteraction != null;
    public InteractionView FocusedView =>
        focusedInteraction != null
            ? focusedInteraction.View
            : default;

    private void Awake()
    {
        input = GetComponent<PlayerInputReader>();
        player = GetComponent<PlayerController>();
        health = GetComponent<Health>();
        interactionCamera =
            player != null ? player.AimCamera : Camera.main;
        profile = Resources.Load<PlayerHudVisualProfile>(
            "PlayerHudVisualProfile");
    }

    private void Start()
    {
        if (interactionCamera == null)
        {
            interactionCamera = Camera.main;
        }

        if (health != null)
        {
            health.Damaged += HandleDamaged;
        }
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
        }
    }

    private void Update()
    {
        if (player == null ||
            !player.GameplayInputEnabled ||
            player.IsPaused ||
            Time.timeScale <= 0f)
        {
            CancelActiveInteraction(
                InteractionCancelReason.GameplayDisabled);
            focusedInteraction = null;
            return;
        }

        IInteractable scannedInteraction = null;

        if (interactionCamera != null)
        {
            Ray ray = interactionCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f));
            TryFindInteractable(
                ray,
                out scannedInteraction);
        }

        if (activeInteraction != null &&
            !ReferenceEquals(
                activeInteraction,
                scannedInteraction))
        {
            CancelActiveInteraction(
                scannedInteraction == null
                    ? InteractionCancelReason.OutOfRange
                    : InteractionCancelReason.LostFocus);
        }

        focusedInteraction = scannedInteraction;

        if (focusedInteraction == null)
        {
            return;
        }

        if (activeInteraction == null &&
            input.InteractPressed &&
            focusedInteraction.View.IsAvailable &&
            focusedInteraction.TryBegin(gameObject))
        {
            activeInteraction = focusedInteraction;
        }

        if (activeInteraction == null)
        {
            return;
        }

        if (input.InteractReleased || !input.InteractHeld)
        {
            CancelActiveInteraction(
                InteractionCancelReason.Released);
            return;
        }

        if (activeInteraction.Advance(
                gameObject,
                Time.deltaTime))
        {
            activeInteraction = null;
        }
    }

    public void ConfigureRange(float configuredRange)
    {
        interactionRange = Mathf.Max(0.5f, configuredRange);
    }

    public bool TryFindInteractable(
        Ray ray,
        out IInteractable interaction)
    {
        interaction = null;
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            interactionHits,
            interactionRange,
            interactionMask,
            QueryTriggerInteraction.Ignore);
        RaycastHit? nearestHit = null;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit hit = interactionHits[index];

            if (hit.transform == transform ||
                hit.transform.IsChildOf(transform))
            {
                continue;
            }

            if (!nearestHit.HasValue ||
                hit.distance < nearestHit.Value.distance)
            {
                nearestHit = hit;
            }
        }

        if (!nearestHit.HasValue)
        {
            return false;
        }

        MonoBehaviour[] candidates =
            nearestHit.Value.collider
                .GetComponentsInParent<MonoBehaviour>(true);

        foreach (MonoBehaviour candidate in candidates)
        {
            if (candidate is IInteractable interactable &&
                interactable.View.IsAvailable)
            {
                interaction = interactable;
                return true;
            }
        }

        return false;
    }

    public bool CancelActiveInteraction(
        InteractionCancelReason reason)
    {
        if (activeInteraction == null)
        {
            return false;
        }

        IInteractable interaction = activeInteraction;
        activeInteraction = null;
        return interaction.Cancel(gameObject, reason);
    }

    private void HandleDamaged(DamageInfo damage)
    {
        CancelActiveInteraction(
            InteractionCancelReason.Damaged);
    }

    private void OnGUI()
    {
        if (focusedInteraction == null ||
            !focusedInteraction.View.IsAvailable)
        {
            return;
        }

        EnsureStyles();
        InteractionView view = focusedInteraction.View;
        float scale = Mathf.Clamp(
            Screen.height / 1080f,
            0.75f,
            1.25f);
        float width = 300f * scale;
        float height = 70f * scale;
        Rect panel = new Rect(
            (Screen.width - width) * 0.5f,
            Screen.height * 0.5f + 68f * scale,
            width,
            height);
        Color previous = GUI.color;
        GUI.color = profile != null
            ? profile.PanelColor
            : new Color(0.02f, 0.03f, 0.04f, 0.85f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(
            new Rect(
                panel.x,
                panel.y + 6f * scale,
                panel.width,
                26f * scale),
            view.Prompt,
            promptStyle);

        if (activeInteraction != null)
        {
            Rect progressBackground = new Rect(
                panel.x + 24f * scale,
                panel.y + 42f * scale,
                panel.width - 48f * scale,
                10f * scale);
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(
                progressBackground,
                Texture2D.whiteTexture);
            GUI.color = new Color(0.1f, 0.75f, 1f, 1f);
            GUI.DrawTexture(
                new Rect(
                    progressBackground.x,
                    progressBackground.y,
                    progressBackground.width *
                        view.ProgressNormalized,
                    progressBackground.height),
                Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(
                    panel.x,
                    panel.y + 31f * scale,
                    panel.width,
                    24f * scale),
                $"接入中 {view.ProgressNormalized:P0}",
                progressStyle);
        }

        GUI.color = previous;
    }

    private void EnsureStyles()
    {
        if (promptStyle != null)
        {
            return;
        }

        promptStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(
                18f * Mathf.Clamp(
                    Screen.height / 1080f,
                    0.75f,
                    1.25f)),
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
        progressStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(
                12f * Mathf.Clamp(
                    Screen.height / 1080f,
                    0.75f,
                    1.25f)),
            normal =
            {
                textColor = new Color(0.25f, 0.85f, 1f)
            }
        };

        if (profile != null && profile.Font != null)
        {
            promptStyle.font = profile.Font;
            progressStyle.font = profile.Font;
        }
    }
}
