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
    private readonly List<UpgradeDefinition> runtimeDefinitions = new();
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

        for (int index = 0; index < runtimeDefinitions.Count; index++)
        {
            if (runtimeDefinitions[index] != null)
            {
                Destroy(runtimeDefinitions[index]);
            }
        }
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

        definitions = new List<UpgradeDefinition>
        {
            CreateRuntimeDefinition(
                "damage_hardened_rounds",
                "强化弹头",
                "每层使武器伤害提高 25%。",
                UpgradeRarity.Common,
                3,
                UpgradeEffectType.WeaponDamage,
                0.25f,
                HudIconId.Ammo),
            CreateRuntimeDefinition(
                "damage_weakpoint_analysis",
                "弱点分析",
                "每层使武器伤害提高 20%。",
                UpgradeRarity.Rare,
                2,
                UpgradeEffectType.WeaponDamage,
                0.2f,
                HudIconId.Rifle),
            CreateRuntimeDefinition(
                "damage_overcharged_core",
                "过载核心",
                "使武器伤害提高 35%。",
                UpgradeRarity.Epic,
                1,
                UpgradeEffectType.WeaponDamage,
                0.35f,
                HudIconId.Handgun),
            CreateRuntimeDefinition(
                "fire_rate_rapid_cycling",
                "快速枪机",
                "每层使武器射速提高 15%。",
                UpgradeRarity.Common,
                3,
                UpgradeEffectType.WeaponFireRate,
                0.15f,
                HudIconId.Rifle),
            CreateRuntimeDefinition(
                "magazine_extended_capacity",
                "扩容弹匣",
                "每层使弹匣容量提高 20%。",
                UpgradeRarity.Rare,
                3,
                UpgradeEffectType.WeaponMagazineCapacity,
                0.2f,
                HudIconId.Ammo),
            CreateRuntimeDefinition(
                "reload_quick_hands",
                "快速换弹",
                "每层使换弹速度提高 20%。",
                UpgradeRarity.Common,
                3,
                UpgradeEffectType.WeaponReloadSpeed,
                0.2f,
                HudIconId.Handgun),
            CreateRuntimeDefinition(
                "recoil_dampening",
                "后坐力抑制",
                "每层使后坐力控制提高 18%。",
                UpgradeRarity.Rare,
                3,
                UpgradeEffectType.WeaponRecoilControl,
                0.18f,
                HudIconId.Rifle),
            CreateRuntimeDefinition(
                "accuracy_tight_grouping",
                "精准射击",
                "每层使射击精准度提高 20%。",
                UpgradeRarity.Epic,
                2,
                UpgradeEffectType.WeaponAccuracy,
                0.2f,
                HudIconId.Ammo),
            CreateRuntimeDefinition(
                "survival_vitality_reinforcement",
                "生命强化",
                "每层使最大生命值提高 20%。",
                UpgradeRarity.Common,
                3,
                UpgradeEffectType.MaximumHealth,
                0.2f,
                HudIconId.Health),
            CreateRuntimeDefinition(
                "survival_reinforced_plating",
                "强化护甲",
                "每层使最大护甲值提高 20%。",
                UpgradeRarity.Rare,
                3,
                UpgradeEffectType.MaximumArmor,
                0.2f,
                HudIconId.Armor),
            CreateRuntimeDefinition(
                "survival_emergency_treatment",
                "紧急治疗",
                "立即恢复 30 点生命值。",
                UpgradeRarity.Common,
                5,
                UpgradeEffectType.HealthRestore,
                30f,
                HudIconId.Health),
            CreateRuntimeDefinition(
                "survival_field_armor_repair",
                "战地护甲修复",
                "立即恢复 30 点护甲值。",
                UpgradeRarity.Common,
                5,
                UpgradeEffectType.ArmorRestore,
                30f,
                HudIconId.Armor),
            CreateRuntimeDefinition(
                "survival_mobility_training",
                "机动训练",
                "每层使移动速度提高 10%。",
                UpgradeRarity.Rare,
                3,
                UpgradeEffectType.MovementSpeed,
                0.1f,
                HudIconId.Health)
        };
    }

    private UpgradeDefinition CreateRuntimeDefinition(
        string id,
        string title,
        string description,
        UpgradeRarity rarity,
        int maximumLevel,
        UpgradeEffectType effectType,
        float amount,
        HudIconId iconId)
    {
        UpgradeDefinition definition =
            ScriptableObject.CreateInstance<UpgradeDefinition>();
        definition.name = title;
        definition.Configure(
            id,
            title,
            description,
            new HudIconCatalog().Get(iconId),
            rarity,
            maximumLevel,
            effectType,
            amount);
        runtimeDefinitions.Add(definition);
        return definition;
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
