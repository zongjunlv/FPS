using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public sealed class PlayerCombatCompositionRoot : MonoBehaviour
{
    [Header("Required Scene Dependencies")]
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerController player;
    [SerializeField] private PlayerRecoilController recoil;
    [SerializeField] private PlayerAnimatorController animator;
    [SerializeField] private PlayerCombatController combat;
    [SerializeField] private WeaponLoadoutController loadout;
    [SerializeField] private ShotTracerPool tracerPool;

    [Header("Shared Runtime Assets")]
    [SerializeField] private CombatSoundEventChannel combatSoundEvents;
    [SerializeField] private CombatFeedbackAudioProfile feedbackAudio;
    [SerializeField] private PlayerHudVisualProfile hudVisualProfile;

    public bool IsInitialized { get; private set; }
    public bool HasConfigurationError { get; private set; }
    public string ConfigurationError { get; private set; }
    public PlayerRuntimeCombatStats RuntimeStats { get; private set; }
    public Health PlayerHealth { get; private set; }
    public PlayerCombatFeedbackController CombatFeedback { get; private set; }
    public UnifiedGameHudBootstrap HudBootstrap { get; private set; }

    private AmmoHudPresenter ammoHud;
    private PlayerCrosshairPresenter crosshair;
    private PlayerVitalsHudPresenter vitalsHud;
    private AudioSource damageAudio;
    private bool prepared;

    private void Awake()
    {
        ResolveSceneReferences();

        if (!ValidateRequiredDependencies())
        {
            enabled = false;
            return;
        }

        CreateRuntimeDependencies();
        ConfigureCombatSlice();
        prepared = true;
    }

    private void Start()
    {
        TryInitialize();
    }

    public bool TryInitialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        if (!prepared)
        {
            return false;
        }

        EnsureLegacyGameplayExtensions();
        PlayerHealth.Initialize(100f, 100f);
        vitalsHud.Configure(hudVisualProfile);
        vitalsHud.Bind(PlayerHealth);
        player.SetRuntimeStats(RuntimeStats);

        if (!combat.Initialize() || !CombatFeedback.Initialize())
        {
            Fail("A configured combat service rejected initialization.");
            return false;
        }

        IsInitialized = true;
        return true;
    }

    private void ResolveSceneReferences()
    {
        input ??= GetComponent<PlayerInputReader>();
        player ??= GetComponent<PlayerController>();
        recoil ??= GetComponent<PlayerRecoilController>();
        animator ??= GetComponent<PlayerAnimatorController>();
        combat ??= GetComponent<PlayerCombatController>();
        loadout ??= GetComponent<WeaponLoadoutController>();
        tracerPool ??= GetComponent<ShotTracerPool>();
    }

    private bool ValidateRequiredDependencies()
    {
        var missing = new List<string>();
        AddMissing(input, nameof(PlayerInputReader), missing);
        AddMissing(player, nameof(PlayerController), missing);
        AddMissing(recoil, nameof(PlayerRecoilController), missing);
        AddMissing(animator, nameof(PlayerAnimatorController), missing);
        AddMissing(combat, nameof(PlayerCombatController), missing);
        AddMissing(loadout, nameof(WeaponLoadoutController), missing);
        AddMissing(tracerPool, nameof(ShotTracerPool), missing);
        AddMissing(combatSoundEvents, nameof(CombatSoundEventChannel), missing);
        AddMissing(feedbackAudio, nameof(CombatFeedbackAudioProfile), missing);
        AddMissing(hudVisualProfile, nameof(PlayerHudVisualProfile), missing);

        if (missing.Count == 0)
        {
            return true;
        }

        Fail($"Missing required dependencies: {string.Join(", ", missing)}.");
        return false;
    }

    private void CreateRuntimeDependencies()
    {
        RuntimeStats = GetOrAdd<PlayerRuntimeCombatStats>();
        PlayerHealth = GetOrAdd<Health>();
        crosshair = GetOrAdd<PlayerCrosshairPresenter>();
        ammoHud = GetOrAdd<AmmoHudPresenter>();
        vitalsHud = GetOrAdd<PlayerVitalsHudPresenter>();
        CombatFeedback = GetOrAdd<PlayerCombatFeedbackController>();
        HudBootstrap = GetOrAdd<UnifiedGameHudBootstrap>();

        damageAudio = GetOrAdd<AudioSource>();
        damageAudio.playOnAwake = false;
        damageAudio.spatialBlend = 0f;
    }

    private void ConfigureCombatSlice()
    {
        combat.Configure(
            input,
            recoil,
            player,
            animator,
            loadout,
            ammoHud,
            tracerPool,
            RuntimeStats,
            combatSoundEvents);
        CombatFeedback.Configure(
            combat,
            player,
            input,
            crosshair,
            PlayerHealth,
            vitalsHud,
            damageAudio,
            feedbackAudio);
        HudBootstrap.Configure(gameObject, null);
    }

    private void EnsureLegacyGameplayExtensions()
    {
        GetOrAdd<PlayerInteractionController>();
        GetOrAdd<PlayerWorldPickupController>();
        GetOrAdd<CityNewTerminalMissionBootstrap>();
        GetOrAdd<PlayerRunProgression>();
        GetOrAdd<PlayerUpgradeController>();
        GetOrAdd<WorldItemFactory>();
        GetOrAdd<PlayerInventoryController>();
        GetOrAdd<PlayerLootRewardController>();
        GetOrAdd<CityNewInventoryBootstrap>();
        GetOrAdd<PlayerFailureFlowController>();
    }

    private T GetOrAdd<T>() where T : Component
    {
        return TryGetComponent(out T component)
            ? component
            : gameObject.AddComponent<T>();
    }

    private static void AddMissing(
        Object dependency,
        string dependencyName,
        ICollection<string> missing)
    {
        if (dependency == null)
        {
            missing.Add(dependencyName);
        }
    }

    private void Fail(string detail)
    {
        HasConfigurationError = true;
        ConfigurationError =
            $"[{nameof(PlayerCombatCompositionRoot)}] '{name}': {detail} " +
            "Initialization stopped safely.";
        Debug.LogError(ConfigurationError, this);
        enabled = false;
    }
}
