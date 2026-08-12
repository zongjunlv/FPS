using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerUpgradeController : MonoBehaviour
{
    [SerializeField] private int runSeed = 18018;
    [SerializeField] private List<UpgradeDefinition> definitions = new();

    private readonly RunUpgradeState state = new();
    private readonly List<UpgradeDefinition> runtimeDefinitions = new();
    private PlayerRunProgression progression;
    private PlayerRuntimeCombatStats combatStats;
    private GameplayLockCoordinator gameplayLocks;
    private UpgradeCandidateGenerator generator;
    private GameplayLockLease choiceLock;
    private UpgradeChoiceView view;
    private IReadOnlyList<UpgradeDefinition> currentCandidates =
        Array.Empty<UpgradeDefinition>();
    private int pendingChoices;
    private bool waitingForHud;

    public int RunSeed => runSeed;
    public int PendingChoiceCount => pendingChoices;
    public bool IsChoiceOpen => view != null && view.IsVisible;
    public int SelectedUpgradeCount => state.SelectionCount;
    public float WeaponDamageMultiplier => state.WeaponDamageMultiplier;
    public IReadOnlyList<string> SelectionHistory => state.SelectionHistory;
    public IReadOnlyList<UpgradeDefinition> CurrentCandidates =>
        currentCandidates;
    public UpgradeCandidateStatus LastCandidateStatus { get; private set; }

    private void Awake()
    {
        progression = GetComponent<PlayerRunProgression>();
        combatStats = GetComponent<PlayerRuntimeCombatStats>();
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
    }

    private void OnDisable()
    {
        if (progression != null)
        {
            progression.LevelsGained -= HandleLevelsGained;
        }

        CloseChoice();
    }

    private void OnDestroy()
    {
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
        if (count <= 0)
        {
            return;
        }

        pendingChoices += count;
        TryOpenNextChoice();
    }

    public bool TrySelect(int candidateIndex)
    {
        if (!IsChoiceOpen ||
            candidateIndex < 0 ||
            candidateIndex >= currentCandidates.Count)
        {
            return false;
        }

        UpgradeDefinition selected = currentCandidates[candidateIndex];

        if (!state.TryApply(selected))
        {
            return false;
        }

        combatStats?.SetWeaponDamageMultiplier(
            state.WeaponDamageMultiplier);
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

    public int GetUpgradeLevel(string stableId)
    {
        return state.GetLevel(stableId);
    }

    private void HandleLevelsGained(int count)
    {
        QueueUpgradeChoices(count);
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
                "HARDENED ROUNDS",
                "Weapon damage +25% per level.",
                UpgradeRarity.Common,
                3,
                0.25f,
                HudIconId.Ammo),
            CreateRuntimeDefinition(
                "damage_weakpoint_analysis",
                "WEAKPOINT ANALYSIS",
                "Weapon damage +20% per level.",
                UpgradeRarity.Rare,
                2,
                0.2f,
                HudIconId.Rifle),
            CreateRuntimeDefinition(
                "damage_overcharged_core",
                "OVERCHARGED CORE",
                "Weapon damage +35% per level.",
                UpgradeRarity.Epic,
                1,
                0.35f,
                HudIconId.Handgun)
        };
    }

    private UpgradeDefinition CreateRuntimeDefinition(
        string id,
        string title,
        string description,
        UpgradeRarity rarity,
        int maximumLevel,
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
            UpgradeEffectType.WeaponDamage,
            amount);
        runtimeDefinitions.Add(definition);
        return definition;
    }
}
