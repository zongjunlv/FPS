using FPS.GameplayEffects;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Health))]
public sealed class EnemyBurnEffectController : MonoBehaviour
{
    [SerializeField] private GameplayEffectDefinition burnDefinition;
    [SerializeField, Min(0.01f)] private float defaultTickDamage = 4f;
    [SerializeField, Min(0.05f)] private float defaultTickInterval = 1f;
    [SerializeField, Min(0.05f)] private float defaultDuration = 4f;
    [SerializeField, Min(1)] private int defaultMaximumStacks = 3;
    [SerializeField, Min(0.05f)] private float overheadClearance = 0.28f;
    [SerializeField] private GameplayEffectStackRefreshPolicy
        defaultRefreshPolicy =
            GameplayEffectStackRefreshPolicy.RefreshAllDurations;

    private Health health;
    private GameplayEffectRuntime runtime;
    private GameplayEffectDefinition generatedDefinition;
    private GameObject presentationRoot;
    private GameObject burnVisualRoot;
    private GameObject statusRoot;
    private ParticleSystem flameParticles;
    private TextMeshProUGUI statusLabel;
    private EnemyController enemy;
    private RectTransform healthFill;
    private Image healthFillImage;
    private Material particleMaterial;
    private bool overheadPresentationEnabled = true;
    private string affixStatusLabel;
    private Color affixStatusColor = Color.white;
    private string roleStatusLabel;
    private Color roleStatusColor = Color.white;
    private string supportStatusLabel;
    private Color supportStatusColor = Color.white;
    private int supportSourceCount;
    private bool wasBurningWhenKilled;
    private const float HealthFillWidth = 116f;
    private const float OverheadWorldScale = 0.0065f;
    private const string OverheadRootName = "Enemy Overhead Information";
    private const string BurnVisualRootName = "Burn Flame Particles";

    public bool IsBurning => StackCount > 0;
    public int StackCount => FindBurnInstance()?.StackCount ?? 0;
    public int TickCount { get; private set; }
    public bool PresentationVisible =>
        presentationRoot != null && presentationRoot.activeSelf;
    public bool HealthBarVisible => PresentationVisible;
    public bool StatusVisible => statusRoot != null && statusRoot.activeSelf;
    public float HealthFillNormalized => health != null && health.MaxHealth > 0f
        ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth)
        : 0f;
    public string StackText => StatusText;
    public string StatusText =>
        statusLabel != null ? statusLabel.text : string.Empty;
    public bool HasAffixStatus => !string.IsNullOrWhiteSpace(
        affixStatusLabel);
    public bool HasRoleStatus => !string.IsNullOrWhiteSpace(
        roleStatusLabel);
    public bool HasSupportStatus => supportSourceCount > 0 &&
        !string.IsNullOrWhiteSpace(supportStatusLabel);
    public float PresentationWorldScale =>
        presentationRoot != null
            ? presentationRoot.transform.localScale.x
            : 0f;
    public float OverheadLocalHeight =>
        presentationRoot != null
            ? presentationRoot.transform.localPosition.y
            : ResolveOverheadLocalHeight();
    public float OverheadClearance => overheadClearance;
    public GameplayEffectDefinition Definition => EnsureDefinition();
    public bool WasBurningWhenKilled => wasBurningWhenKilled;

    public bool TryCaptureGameplayEffectSnapshot(
        System.Func<Object, string> sourceEncoder,
        out GameplayEffectRuntimeSnapshot snapshot,
        out string error)
    {
        return runtime.TryCaptureSnapshot(
            sourceEncoder,
            out snapshot,
            out error);
    }

    public bool TryRestoreGameplayEffectSnapshot(
        GameplayEffectRuntimeSnapshot snapshot,
        System.Func<string, Object> sourceResolver,
        out string error)
    {
        bool restored = runtime.TryRestoreSnapshot(
            snapshot,
            stableId => string.Equals(
                stableId,
                Definition.StableId,
                System.StringComparison.Ordinal)
                ? Definition
                : null,
            sourceResolver,
            out error);
        if (restored)
        {
            SyncPresentation();
        }
        return restored;
    }

    private void Awake()
    {
        health = GetComponent<Health>();
        enemy = GetComponent<EnemyController>();
        runtime = new GameplayEffectRuntime(
            gameObject,
            "Enemy Status Effects");
        EnsureDefinition();
        EnsurePresentation();
        SyncPresentation();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Killed += HandleKilled;
            health.Died += HandleDeath;
            health.VitalsChanged += SyncPresentation;
        }

        SyncPresentation();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Killed -= HandleKilled;
            health.Died -= HandleDeath;
            health.VitalsChanged -= SyncPresentation;
        }

        // Disable may be part of the death/despawn sequence. Clear the active
        // runtime without erasing the death-time tag memory required by the
        // subsequent combat event. Pool reset calls ClearBurn explicitly.
        runtime?.Clear();
        TickCount = 0;
        SyncPresentation();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Killed -= HandleKilled;
            health.Died -= HandleDeath;
            health.VitalsChanged -= SyncPresentation;
        }

        ReleaseGeneratedDefinition();

        if (particleMaterial != null)
        {
            Destroy(particleMaterial);
        }
    }

    private void Update()
    {
        Advance(Time.deltaTime);
        FaceInfoToCamera();
    }

    public void ConfigureBurn(
        float tickDamage,
        float tickInterval,
        float duration,
        int maximumStacks,
        GameplayEffectStackRefreshPolicy refreshPolicy)
    {
        ClearBurn();
        ReleaseGeneratedDefinition();
        generatedDefinition =
            ScriptableObject.CreateInstance<GameplayEffectDefinition>();
        generatedDefinition.name = "Runtime Enemy Burn Effect";
        generatedDefinition.hideFlags = HideFlags.HideAndDontSave;
        generatedDefinition.ConfigureTimed(
            "status.burn",
            Mathf.Max(0.01f, tickDamage),
            Mathf.Max(0.05f, tickInterval),
            Mathf.Max(0.05f, duration),
            Mathf.Max(1, maximumStacks),
            refreshPolicy);
    }

    public bool ApplyBurn(GameObject source)
    {
        return ApplyStatus(EnsureDefinition(), source);
    }

    public bool ApplyStatus(
        GameplayEffectDefinition definition,
        GameObject source)
    {
        if (health == null || health.IsDead || source == null ||
            definition == null ||
            definition.DurationPolicy != GameplayEffectDurationPolicy.Timed ||
            !string.Equals(
                definition.StableId,
                "status.burn",
                System.StringComparison.Ordinal))
        {
            return false;
        }

        GameplayEffectApplicationResult result = runtime.ApplyTimed(
            definition,
            new GameplayEffectContext(
                source.GetEntityId().ToString(),
                source,
                gameObject));
        SyncPresentation();
        return result.Succeeded &&
               (result.StackAdded || result.DurationRefreshed);
    }

    public void Advance(float deltaTime)
    {
        if (runtime == null || deltaTime <= 0f || !IsBurning)
        {
            return;
        }

        runtime.AdvanceTimed(deltaTime, ApplyTickDamage);
        SyncPresentation();
    }

    public void ClearBurn()
    {
        wasBurningWhenKilled = false;
        runtime?.Clear();
        TickCount = 0;
        SyncPresentation();
    }

    public void SetOverheadPresentationEnabled(bool enabled)
    {
        overheadPresentationEnabled = enabled;

        if (enabled)
        {
            RefreshOverheadAnchor();
        }

        SyncPresentation();
    }

    public void SetAffixStatus(string label, Color color)
    {
        affixStatusLabel = string.IsNullOrWhiteSpace(label)
            ? "ELITE"
            : label.Trim();
        affixStatusColor = color;
        SyncPresentation();
    }

    public void ClearAffixStatus()
    {
        affixStatusLabel = string.Empty;
        SyncPresentation();
    }

    public void SetRoleStatus(string label, Color color)
    {
        roleStatusLabel = string.IsNullOrWhiteSpace(label)
            ? "RAIDER"
            : label.Trim();
        roleStatusColor = color;
        SyncPresentation();
    }

    public void ClearRoleStatus()
    {
        roleStatusLabel = string.Empty;
        SyncPresentation();
    }

    public void SetSupportStatus(
        int sourceCount,
        string label,
        Color color)
    {
        supportSourceCount = Mathf.Max(0, sourceCount);
        supportStatusLabel = string.IsNullOrWhiteSpace(label)
            ? "BOOST"
            : label.Trim();
        supportStatusColor = color;
        SyncPresentation();
    }

    public void ClearSupportStatus()
    {
        supportSourceCount = 0;
        supportStatusLabel = string.Empty;
        SyncPresentation();
    }

    private void ApplyTickDamage(GameplayEffectTick tick)
    {
        if (health == null || health.IsDead || tick.Definition == null)
        {
            return;
        }

        GameObject source = tick.Context.Source as GameObject;

        if (source == null && tick.Context.Source is Component component)
        {
            source = component.gameObject;
        }

        TickCount++;
        health.ApplyDamage(new DamageInfo(
            Mathf.Max(0f, tick.Definition.PeriodicMagnitude),
            transform.position,
            Vector3.up,
            source,
            DamageType.StatusEffect));
    }

    private void HandleDeath()
    {
        // Killed is the preferred capture point, but dynamically-added enemy
        // components can finish enabling later in the same frame. Preserve the
        // tag again at death so rule evaluation never depends on subscription
        // order between EnemyController and this presentation/runtime component.
        wasBurningWhenKilled |= IsBurning;
        runtime?.Clear();
        TickCount = 0;
        SyncPresentation();
        SetOverheadPresentationEnabled(false);
    }

    private void HandleKilled(DamageInfo damage)
    {
        wasBurningWhenKilled = IsBurning;
    }

    private GameplayEffectDefinition EnsureDefinition()
    {
        if (burnDefinition != null)
        {
            return burnDefinition;
        }

        if (generatedDefinition == null)
        {
            generatedDefinition =
                ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            generatedDefinition.name = "Runtime Enemy Burn Effect";
            generatedDefinition.hideFlags = HideFlags.HideAndDontSave;
            generatedDefinition.ConfigureTimed(
                "status.burn",
                defaultTickDamage,
                defaultTickInterval,
                defaultDuration,
                defaultMaximumStacks,
                defaultRefreshPolicy);
        }

        return generatedDefinition;
    }

    private GameplayEffectInstance FindBurnInstance()
    {
        if (runtime == null)
        {
            return null;
        }

        GameplayEffectDefinition definition = EnsureDefinition();

        for (int index = 0; index < runtime.ActiveInstances.Count; index++)
        {
            GameplayEffectInstance instance = runtime.ActiveInstances[index];

            if (instance.Definition.DurationPolicy ==
                    GameplayEffectDurationPolicy.Timed &&
                string.Equals(
                    instance.Definition.StableId,
                    definition.StableId,
                    System.StringComparison.Ordinal))
            {
                return instance;
            }
        }

        return null;
    }

    private void EnsurePresentation()
    {
        if (presentationRoot != null)
        {
            return;
        }

        // Runtime-created children are part of Unity's Instantiate clone.
        // Their private controller references are not, so an active scene
        // enemy used as a pool template would otherwise create a second copy.
        DetachInheritedRuntimeChild(OverheadRootName);
        DetachInheritedRuntimeChild(BurnVisualRootName);

        presentationRoot = new GameObject(
            OverheadRootName,
            typeof(RectTransform),
            typeof(Canvas));
        presentationRoot.transform.SetParent(transform, false);
        RefreshOverheadAnchor();
        presentationRoot.transform.localScale =
            Vector3.one * OverheadWorldScale;
        RectTransform infoRect =
            presentationRoot.GetComponent<RectTransform>();
        infoRect.sizeDelta = new Vector2(140f, 42f);
        Canvas canvas = presentationRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;

        RectTransform healthBackground = CreateImage(
            "Health Bar Background",
            infoRect,
            new Color(0.03f, 0.04f, 0.05f, 0.88f),
            new Vector2(0f, 9f),
            new Vector2(122f, 14f));
        healthFill = CreateImage(
            "Health Bar Fill",
            healthBackground,
            new Color(0.2f, 0.9f, 0.36f, 1f),
            new Vector2(-58f, 0f),
            new Vector2(HealthFillWidth, 8f));
        healthFill.pivot = new Vector2(0f, 0.5f);
        healthFillImage = healthFill.GetComponent<Image>();

        statusRoot = new GameObject(
            "Status Row",
            typeof(RectTransform));
        RectTransform statusRect = statusRoot.GetComponent<RectTransform>();
        statusRect.SetParent(infoRect, false);
        statusRect.anchoredPosition = new Vector2(0f, -9f);
        statusRect.sizeDelta = new Vector2(122f, 18f);
        statusLabel = statusRoot.AddComponent<TextMeshProUGUI>();
        statusLabel.alignment = TextAlignmentOptions.Center;
        statusLabel.fontSize = 14f;
        statusLabel.fontStyle = FontStyles.Bold;
        statusLabel.color = new Color(1f, 0.48f, 0.08f, 1f);
        statusLabel.text = string.Empty;

        burnVisualRoot = new GameObject(BurnVisualRootName);
        burnVisualRoot.transform.SetParent(transform, false);
        burnVisualRoot.transform.localPosition = Vector3.up * 1.15f;

        flameParticles = burnVisualRoot.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = flameParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 0.45f;
        main.startSpeed = 0.28f;
        main.startSize = 0.12f;
        main.startColor = new Color(1f, 0.28f, 0.02f, 0.9f);
        main.maxParticles = 24;
        ParticleSystem.ShapeModule shape = flameParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.42f;
        ParticleSystem.EmissionModule emission = flameParticles.emission;
        emission.rateOverTime = 5f;
        ParticleSystemRenderer renderer =
            flameParticles.GetComponent<ParticleSystemRenderer>();
        Shader shader = Shader.Find("Sprites/Default");

        if (shader != null)
        {
            particleMaterial = new Material(shader)
            {
                name = "Runtime Burn Particle Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            renderer.sharedMaterial = particleMaterial;
        }

    }

    public void RefreshOverheadAnchor()
    {
        if (presentationRoot == null)
        {
            return;
        }

        presentationRoot.transform.localPosition =
            Vector3.up * ResolveOverheadLocalHeight();
    }

    private float ResolveOverheadLocalHeight()
    {
        BoxCollider rootBounds = GetComponent<BoxCollider>();

        if (rootBounds != null)
        {
            float colliderTop = rootBounds.center.y +
                Mathf.Abs(rootBounds.size.y) * 0.5f;
            return Mathf.Max(0.5f, colliderTop + overheadClearance);
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        float top = 0f;

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer renderer = renderers[index];

            if (renderer == null ||
                (presentationRoot != null &&
                 renderer.transform.IsChildOf(presentationRoot.transform)) ||
                (burnVisualRoot != null &&
                 renderer.transform.IsChildOf(burnVisualRoot.transform)))
            {
                continue;
            }

            Vector3 worldTop = new Vector3(
                renderer.bounds.center.x,
                renderer.bounds.max.y,
                renderer.bounds.center.z);
            float localTop = transform.InverseTransformPoint(worldTop).y;
            top = found ? Mathf.Max(top, localTop) : localTop;
            found = true;
        }

        return Mathf.Max(0.5f, (found ? top : 1.2f) +
            overheadClearance);
    }

    private void DetachInheritedRuntimeChild(string childName)
    {
        Transform inherited = transform.Find(childName);

        if (inherited == null)
        {
            return;
        }

        inherited.gameObject.SetActive(false);
        inherited.SetParent(null, false);

        if (Application.isPlaying)
        {
            Destroy(inherited.gameObject);
        }
        else
        {
            DestroyImmediate(inherited.gameObject);
        }
    }

    private void SyncPresentation()
    {
        if (presentationRoot == null)
        {
            return;
        }

        int stacks = StackCount;
        bool showOverhead = overheadPresentationEnabled &&
                            isActiveAndEnabled &&
                            health != null &&
                            !health.IsDead;
        presentationRoot.SetActive(showOverhead);

        if (healthFill != null)
        {
            healthFill.sizeDelta = new Vector2(
                HealthFillWidth * HealthFillNormalized,
                healthFill.sizeDelta.y);
        }

        if (healthFillImage != null)
        {
            healthFillImage.color = HealthFillNormalized > 0.5f
                ? new Color(0.2f, 0.9f, 0.36f, 1f)
                : HealthFillNormalized > 0.25f
                    ? new Color(1f, 0.68f, 0.1f, 1f)
                    : new Color(1f, 0.2f, 0.16f, 1f);
        }

        bool hasAffix = HasAffixStatus;
        bool hasRole = HasRoleStatus;
        bool hasSupport = HasSupportStatus;
        string displayName = enemy != null
            ? enemy.DisplayName
            : string.Empty;
        bool hasDisplayName = !string.IsNullOrWhiteSpace(displayName);

        if (statusRoot != null)
        {
            statusRoot.SetActive(
                showOverhead &&
                (stacks > 0 || hasAffix || hasRole || hasSupport ||
                 hasDisplayName));
        }

        if (statusLabel != null)
        {
            string roleAndAffix = hasRole && hasAffix
                ? $"{roleStatusLabel} · {affixStatusLabel}"
                : hasRole
                    ? roleStatusLabel
                    : hasAffix
                        ? affixStatusLabel
                        : string.Empty;
            string supported = hasSupport
                ? supportSourceCount > 1
                    ? $"{supportStatusLabel} ×{supportSourceCount}"
                    : supportStatusLabel
                : string.Empty;
            string combined = !string.IsNullOrEmpty(roleAndAffix) &&
                              !string.IsNullOrEmpty(supported)
                ? $"{roleAndAffix} · {supported}"
                : !string.IsNullOrEmpty(roleAndAffix)
                    ? roleAndAffix
                    : supported;
            combined = hasDisplayName && !string.IsNullOrEmpty(combined)
                ? $"{displayName} · {combined}"
                : hasDisplayName
                    ? displayName
                    : combined;
            statusLabel.text = stacks > 0 &&
                               !string.IsNullOrEmpty(combined)
                ? $"{combined} · BURN ×{stacks}"
                : !string.IsNullOrEmpty(combined)
                    ? combined
                    : stacks > 0
                        ? $"BURN ×{stacks}"
                        : string.Empty;
            statusLabel.color = hasAffix
                ? affixStatusColor
                : hasRole
                    ? roleStatusColor
                    : hasSupport
                        ? supportStatusColor
                        : hasDisplayName
                            ? Color.white
                            : new Color(1f, 0.48f, 0.08f, 1f);
        }

        if (burnVisualRoot != null)
        {
            burnVisualRoot.SetActive(showOverhead && stacks > 0);
        }

        if (flameParticles != null && stacks > 0)
        {
            ParticleSystem.EmissionModule emission = flameParticles.emission;
            emission.rateOverTime = 4f + stacks * 3f;

            if (!flameParticles.isPlaying)
            {
                flameParticles.Play();
            }
        }
    }

    private void FaceInfoToCamera()
    {
        if (!PresentationVisible || Camera.main == null)
        {
            return;
        }

        Transform infoTransform = presentationRoot.transform;
        Vector3 direction = infoTransform.position -
                            Camera.main.transform.position;

        if (direction.sqrMagnitude > 0.0001f)
        {
            infoTransform.rotation = Quaternion.LookRotation(direction);
        }
    }

    private static RectTransform CreateImage(
        string objectName,
        RectTransform parent,
        Color color,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        var imageObject = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(Image));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        imageObject.GetComponent<Image>().color = color;
        return rect;
    }

    private void ReleaseGeneratedDefinition()
    {
        if (generatedDefinition == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(generatedDefinition);
        }
        else
        {
            DestroyImmediate(generatedDefinition);
        }

        generatedDefinition = null;
    }
}
