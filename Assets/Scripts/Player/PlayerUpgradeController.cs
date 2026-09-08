using System;
using System.Collections;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public sealed class PlayerUpgradeController : MonoBehaviour
{
    [SerializeField] private int runSeed = 18018;
    [SerializeField] private List<UpgradeDefinition> definitions = new();

    private readonly RunUpgradeState state = new();
    private PlayerRunProgression progression;
    private PlayerRuntimeCombatStats combatStats;
    private Health health;
    private float baseMaximumHealth;
    private float baseMaximumArmor;
    private bool survivalBaselineCaptured;
    private GameplayLockCoordinator gameplayLocks;
    private UpgradeCandidateGenerator generator;
    private GameplayLockLease choiceLock;
    private UpgradeChoiceView view;
    private IReadOnlyList<UpgradeDefinition> currentCandidates =
        Array.Empty<UpgradeDefinition>();
    private int pendingChoices;
    private bool waitingForHud;
    private bool runEnded;
    private GameplayEffectRuntime gameplayEffects;
    private bool gameplayEffectBaselineCaptured;

    public int RunSeed => runSeed;
    public int PendingChoiceCount => pendingChoices;
    public bool IsChoiceOpen => view != null && view.IsVisible;
    public int SelectedUpgradeCount => state.SelectionCount;
    public float WeaponDamageMultiplier => state.WeaponDamageMultiplier;
    public IReadOnlyList<string> SelectionHistory => state.SelectionHistory;
    public IReadOnlyList<UpgradeDefinition> CurrentCandidates =>
        currentCandidates;
    public IReadOnlyList<UpgradeDefinition> AvailableUpgrades => definitions;
    public UpgradeCandidateStatus LastCandidateStatus { get; private set; }
    public IReadOnlyList<GameplayEffectInstance> ActiveGameplayEffects =>
        gameplayEffects?.ActiveInstances ??
        Array.Empty<GameplayEffectInstance>();
    public int ActiveGameplayEffectCount => ActiveGameplayEffects.Count;

    private void Awake()
    {
        progression = GetComponent<PlayerRunProgression>();
        combatStats = GetComponent<PlayerRuntimeCombatStats>();
        health = GetComponent<Health>();
        gameplayEffects = new GameplayEffectRuntime(
            gameObject,
            "Player Upgrade Effects");
        gameplayLocks = GetComponent<GameplayLockCoordinator>();
        generator = new UpgradeCandidateGenerator(runSeed);
        EnsureDefinitions();
    }

    private void OnEnable()
    {
        if (progression != null)
        {
            progression.LevelsGained += HandleLevelsGained;
        }

        if (gameplayLocks != null)
        {
            gameplayLocks.ModalStateChanged += HandleModalStateChanged;
        }
    }

    private void OnDisable()
    {
        if (progression != null)
        {
            progression.LevelsGained -= HandleLevelsGained;
        }

        if (gameplayLocks != null)
        {
            gameplayLocks.ModalStateChanged -= HandleModalStateChanged;
        }

        CloseChoice();
    }

    private void OnDestroy()
    {
        ClearGameplayEffects();
        CloseChoice();

    }

    public void ConfigureRun(
        int seed,
        IEnumerable<UpgradeDefinition> configuredDefinitions)
    {
        if (SelectedUpgradeCount > 0 || pendingChoices > 0)
        {
            throw new InvalidOperationException(
                "A running upgrade session cannot be reconfigured.");
        }

        runSeed = seed;
        definitions = configuredDefinitions != null
            ? new List<UpgradeDefinition>(configuredDefinitions)
            : new List<UpgradeDefinition>();
        generator = new UpgradeCandidateGenerator(runSeed);
    }

    public void QueueUpgradeChoices(int count)
    {
        if (runEnded || count <= 0)
        {
            return;
        }

        pendingChoices += count;
        TryOpenNextChoice();
    }

    public bool TrySelect(int candidateIndex)
    {
        if (runEnded || !IsChoiceOpen ||
            (gameplayLocks != null &&
             !gameplayLocks.IsTopmost(GameplayLockReason.UpgradeChoice)) ||
            candidateIndex < 0 ||
            candidateIndex >= currentCandidates.Count)
        {
            return false;
        }

        UpgradeDefinition selected = currentCandidates[candidateIndex];

        if (!CanApplyRuntimeEffect(selected))
        {
            return false;
        }

        if (!state.TryApply(selected))
        {
            return false;
        }

        combatStats?.SetWeaponModifiers(state.WeaponModifiers);
        ApplyGameplayEffect(selected);
        ApplySurvivalEffect(selected);
        pendingChoices = Mathf.Max(0, pendingChoices - 1);

        if (pendingChoices > 0)
        {
            PresentNextChoice();
        }
        else
        {
            CloseChoice();
        }

        return true;
    }

    public void EndRun()
    {
        if (runEnded)
        {
            return;
        }

        runEnded = true;
        pendingChoices = 0;
        waitingForHud = false;
        StopAllCoroutines();
        CloseChoice();
        ClearGameplayEffects();
    }

    public bool RemoveGameplayEffect(long instanceId)
    {
        if (gameplayEffects == null || !gameplayEffects.Remove(instanceId))
        {
            return false;
        }

        RefreshGameplayEffectAttributes();
        return true;
    }

    public int GetUpgradeLevel(string stableId)
    {
        return state.GetLevel(stableId);
    }

    private void HandleLevelsGained(int count)
    {
        if (!runEnded)
        {
            QueueUpgradeChoices(count);
        }
    }

    private void TryOpenNextChoice()
    {
        if (pendingChoices <= 0 || IsChoiceOpen)
        {
            return;
        }

        if (!EnsureView())
        {
            if (!waitingForHud)
            {
                StartCoroutine(WaitForHud());
            }

            return;
        }

        choiceLock ??= gameplayLocks?.Acquire(
            GameplayLockReason.UpgradeChoice);
        PresentNextChoice();
        HandleModalStateChanged(gameplayLocks?.TopReason);
    }

    private void PresentNextChoice()
    {
        while (pendingChoices > 0)
        {
            UpgradeCandidateResult result = generator.Generate(
                definitions,
                state,
                3);
            LastCandidateStatus = result.Status;
            currentCandidates = result.Candidates;

            if (result.Status == UpgradeCandidateStatus.NoEligibleUpgrade)
            {
                pendingChoices--;
                continue;
            }

            view.Show(result.Candidates, state, TrySelect);
            return;
        }

        CloseChoice();
    }

    private bool EnsureView()
    {
        if (view != null)
        {
            return true;
        }

        UnifiedGameHud hud = FindAnyObjectByType<UnifiedGameHud>();

        if (hud == null || hud.ModalLayer == null)
        {
            return false;
        }

        view = hud.GetComponent<UpgradeChoiceView>();
        view ??= hud.gameObject.AddComponent<UpgradeChoiceView>();
        view.Initialize(hud.ModalLayer);
        return true;
    }

    private IEnumerator WaitForHud()
    {
        waitingForHud = true;

        for (int frame = 0; frame < 120 && pendingChoices > 0; frame++)
        {
            if (EnsureView())
            {
                break;
            }

            yield return null;
        }

        waitingForHud = false;
        TryOpenNextChoice();
    }

    private void CloseChoice()
    {
        view?.Hide();
        currentCandidates = Array.Empty<UpgradeDefinition>();
        choiceLock?.Dispose();
        choiceLock = null;
    }

    private void HandleModalStateChanged(GameplayLockReason? topReason)
    {
        view?.SetSuspended(
            topReason.HasValue &&
            topReason.Value != GameplayLockReason.UpgradeChoice);
    }

    private void EnsureDefinitions()
    {
        if (definitions != null && definitions.Count > 0)
        {
            return;
        }

        CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
        string error = string.Empty;
        if (catalog == null || !catalog.TryValidate(out error))
        {
            definitions = new List<UpgradeDefinition>();
            Debug.LogError(
                "Player upgrade content is unavailable: " +
                (catalog == null ? "default catalog is missing." : error),
                this);
            return;
        }

        definitions = new List<UpgradeDefinition>(catalog.Upgrades);
    }

    private bool CanApplyRuntimeEffect(UpgradeDefinition definition)
    {
        if (definition == null || health == null)
        {
            return definition != null;
        }

        return definition.EffectType switch
        {
            UpgradeEffectType.HealthRestore =>
                !health.IsDead && health.CurrentHealth < health.MaxHealth,
            UpgradeEffectType.ArmorRestore =>
                !health.IsDead && health.CurrentArmor < health.MaxArmor,
            _ => !health.IsDead
        };
    }

    private void ApplySurvivalEffect(UpgradeDefinition selected)
    {
        CaptureSurvivalBaseline();
        combatStats?.SetSurvivalModifiers(state.SurvivalModifiers);

        switch (selected.EffectType)
        {
            case UpgradeEffectType.MaximumArmor:
                health?.SetMaximumArmor(
                    baseMaximumArmor *
                    state.SurvivalModifiers.MaximumArmorMultiplier);
                break;
            case UpgradeEffectType.HealthRestore:
                health?.RestoreHealth(selected.EffectAmount);
                break;
            case UpgradeEffectType.ArmorRestore:
                health?.RestoreArmor(selected.EffectAmount);
                break;
        }
    }

    private void CaptureSurvivalBaseline()
    {
        if (survivalBaselineCaptured || health == null)
        {
            return;
        }

        baseMaximumArmor = health.MaxArmor;
        survivalBaselineCaptured = true;
    }

    private void ApplyGameplayEffect(UpgradeDefinition selected)
    {
        GameplayEffectDefinition definition = selected?.GameplayEffect;

        if (definition == null || gameplayEffects == null || health == null)
        {
            return;
        }

        CaptureGameplayEffectBaseline();
        gameplayEffects.Apply(
            definition,
            new GameplayEffectContext(
                selected.StableId,
                selected,
                gameObject));
        RefreshGameplayEffectAttributes();
    }

    private void CaptureGameplayEffectBaseline()
    {
        if (gameplayEffectBaselineCaptured || health == null)
        {
            return;
        }

        baseMaximumHealth = health.MaxHealth;
        gameplayEffectBaselineCaptured = true;
    }

    private void RefreshGameplayEffectAttributes()
    {
        if (!gameplayEffectBaselineCaptured ||
            gameplayEffects == null ||
            health == null)
        {
            return;
        }

        health.SetMaximumHealth(
            gameplayEffects.Evaluate(
                GameplayAttributeId.MaximumHealth,
                baseMaximumHealth));
    }

    private void ClearGameplayEffects()
    {
        if (gameplayEffects == null)
        {
            return;
        }

        gameplayEffects.Clear();

        if (gameplayEffectBaselineCaptured && health != null)
        {
            health.SetMaximumHealth(baseMaximumHealth);
        }

        gameplayEffectBaselineCaptured = false;
    }
}
