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
    public GameplayEffectDefinition Definition => EnsureDefinition();

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
            health.Died += HandleDeath;
            health.VitalsChanged += SyncPresentation;
        }

        SyncPresentation();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDeath;
            health.VitalsChanged -= SyncPresentation;
        }

        ClearBurn();
    }

    private void OnDestroy()
    {
        if (health != null)
        {
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
        if (health == null || health.IsDead || source == null)
        {
            return false;
        }

        GameplayEffectDefinition definition = EnsureDefinition();
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
        runtime?.Clear();
        TickCount = 0;
        SyncPresentation();
    }

    public void SetOverheadPresentationEnabled(bool enabled)
    {
        overheadPresentationEnabled = enabled;
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
        ClearBurn();
        SetOverheadPresentationEnabled(false);
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
        presentationRoot.transform.localPosition = Vector3.up * 1.85f;
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
