using System;
using System.Collections.Generic;
using FPS.GameplayEffects;
using UnityEngine;

public interface ICombatTriggerSink
{
    void PublishHit(
        GameObject target,
        DamageResult result,
        Vector3 hitPoint,
        DamageType damageType);
}

[DisallowMultipleComponent]
public sealed class PlayerCombatBuildController : MonoBehaviour,
    ICombatTriggerSink
{
    private const float LowHealthThreshold = 0.35f;
    private const string PlayerTag = "entity.player";
    private const string EnemyTag = "entity.enemy";
    private const string AliveTag = "enemy.alive";
    private const string BurningTag = "status.burning";

    [SerializeField] private List<CombatBuildDefinition> availableBuilds =
        new();

    private readonly List<EnemyController> neighborBuffer = new(16);
    private PlayerCombatController combat;
    private PlayerCombatEventRouter eventRouter;
    private Health health;
    private CombatRuleEngine engine;
    private long nextEventId;
    private long fallbackTick;
    private float observedArmor;
    private bool lowHealthActive;
    private bool subscribed;
    private GameObject currentEventTarget;
    private int? configuredRunSeed;

    public bool IsInitialized { get; private set; }
    public int TriggerCount { get; private set; }
    public int LastSpreadCount { get; private set; }
    public string LastTriggeredRuleId { get; private set; } = string.Empty;
    public IReadOnlyCollection<string> InstalledBuildIds =>
        engine?.InstalledBuildIds ?? Array.Empty<string>();
    public int ActiveRuleCount => engine?.ActiveRuleCount ?? 0;

    public void Configure(
        PlayerCombatController combatController,
        PlayerCombatEventRouter combatEvents,
        Health playerHealth,
        IEnumerable<CombatBuildDefinition> builds = null,
        int? runSeed = null)
    {
        if (IsInitialized)
        {
            throw new InvalidOperationException(
                "A running combat build controller cannot be reconfigured.");
        }

        combat = combatController;
        eventRouter = combatEvents;
        health = playerHealth;
        if (builds != null)
        {
            availableBuilds = new List<CombatBuildDefinition>(builds);
        }
        else
        {
            EnsureBuildCatalog();
        }

        configuredRunSeed = runSeed;
    }

    public bool Initialize()
    {
        if (IsInitialized)
        {
            return true;
        }

        health ??= GetComponent<Health>();
        eventRouter ??= GetComponent<PlayerCombatEventRouter>();
        combat ??= GetComponent<PlayerCombatController>();
        EnsureBuildCatalog();
        engine ??= CreateEngineForRun(ResolveRunSeed());

        if (health == null || eventRouter == null)
        {
            Debug.LogError(
                $"[{nameof(PlayerCombatBuildController)}] '{name}' " +
                "requires player health and combat events.",
                this);
            enabled = false;
            return false;
        }

        InstallStarterBuilds();

        BindWeapons();
        observedArmor = health.CurrentArmor;
        lowHealthActive = IsLowHealth();
        Subscribe();
        IsInitialized = true;
        return true;
    }

    public void ResetForRun(int runSeed)
    {
        EnsureBuildCatalog();
        configuredRunSeed = runSeed;
        engine = CreateEngineForRun(runSeed);
        nextEventId = 0;
        fallbackTick = 0;
        TriggerCount = 0;
        LastSpreadCount = 0;
        LastTriggeredRuleId = string.Empty;
        observedArmor = health != null ? health.CurrentArmor : 0f;
        lowHealthActive = IsLowHealth();
        InstallStarterBuilds();
    }

    public bool InstallBuild(CombatBuildDefinition build)
    {
        return engine != null && engine.Install(build);
    }

    public void PublishHit(
        GameObject target,
        DamageResult result,
        Vector3 hitPoint,
        DamageType damageType)
    {
        if (!IsInitialized || target == null || !result.WasApplied)
        {
            return;
        }

        Health targetHealth = target.GetComponent<Health>();
        float normalized = targetHealth != null && targetHealth.MaxHealth > 0f
            ? targetHealth.CurrentHealth / targetHealth.MaxHealth
            : result.WasKilled ? 0f : 1f;
        Process(
            CombatTriggerType.Hit,
            target,
            CollectTags(target, includeDeathMemory: false),
            normalized,
            result.AppliedAmount,
            (float)damageType);
    }

    public CombatRuleRuntimeSnapshot CaptureSnapshot()
    {
        if (engine == null)
        {
            throw new InvalidOperationException(
                "Combat build runtime is not initialized.");
        }

        return engine.CaptureSnapshot(nextEventId);
    }

    public bool CanRestoreSnapshot(
        CombatRuleRuntimeSnapshot snapshot,
        out string error)
    {
        EnsureBuildCatalog();
        var validation = new CombatRuleEngine(
            GetComponent<PlayerUpgradeController>()?.RunSeed ?? 18018,
            availableBuilds);
        return validation.TryRestore(snapshot, out error);
    }

    public bool TryRestoreSnapshot(
        CombatRuleRuntimeSnapshot snapshot,
        out string error)
    {
        if (engine == null)
        {
            error = "构筑运行时尚未初始化。";
            return false;
        }

        if (!engine.TryRestore(snapshot, out error))
        {
            return false;
        }

        nextEventId = snapshot.NextEventId;
        return true;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindWeapons();
    }

    private void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        eventRouter.Events.Published += HandleCombatEvent;
        health.DamageApplied += HandleDamageApplied;
        health.VitalsChanged += HandleVitalsChanged;
        if (combat != null)
        {
            combat.EquippedWeaponChanged += HandleWeaponChanged;
        }
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (eventRouter != null)
        {
            eventRouter.Events.Published -= HandleCombatEvent;
        }
        if (health != null)
        {
            health.DamageApplied -= HandleDamageApplied;
            health.VitalsChanged -= HandleVitalsChanged;
        }
        if (combat != null)
        {
            combat.EquippedWeaponChanged -= HandleWeaponChanged;
        }
        subscribed = false;
    }

    private void BindWeapons()
    {
        if (combat == null)
        {
            return;
        }

        for (int index = 0; index < combat.WeaponCount; index++)
        {
            WeaponController weapon = combat.GetWeapon(index);
            if (weapon == null)
            {
                continue;
            }
            weapon.SetCombatTriggerSink(this);
            weapon.ReloadCompleted += HandleReloadCompleted;
        }
    }

    private void UnbindWeapons()
    {
        if (combat == null)
        {
            return;
        }

        for (int index = 0; index < combat.WeaponCount; index++)
        {
            WeaponController weapon = combat.GetWeapon(index);
            if (weapon == null)
            {
                continue;
            }
            weapon.ReloadCompleted -= HandleReloadCompleted;
            weapon.SetCombatTriggerSink(null);
        }
    }

    private void HandleWeaponChanged(WeaponController weapon)
    {
        // All loadout weapons are bound once. This hook intentionally remains
        // generic so future runtime-added weapons have a single integration seam.
        if (weapon == null)
        {
            return;
        }
        weapon.SetCombatTriggerSink(this);
        weapon.ReloadCompleted -= HandleReloadCompleted;
        weapon.ReloadCompleted += HandleReloadCompleted;
    }

    private void HandleReloadCompleted()
    {
        Process(
            CombatTriggerType.ReloadCompleted,
            gameObject,
            new[] { PlayerTag },
            HealthNormalized(),
            0f,
            0f);
    }

    private void HandleCombatEvent(GameplayEffectEventContext context)
    {
        if (context.EventType != GameplayEffectEventType.EnemyKilled)
        {
            return;
        }

        GameObject target = context.Target switch
        {
            GameObject value => value,
            Component value => value.gameObject,
            _ => null
        };
        if (target == null)
        {
            return;
        }

        Process(
            CombatTriggerType.Kill,
            target,
            CollectTags(target, includeDeathMemory: true),
            0f,
            0f,
            0f);
    }

    private void HandleDamageApplied(DamageResult result)
    {
        if (!result.WasApplied)
        {
            return;
        }

        Process(
            CombatTriggerType.DamageTaken,
            gameObject,
            new[] { PlayerTag },
            HealthNormalized(),
            result.AppliedAmount,
            observedArmor);

        if (observedArmor > 0f && health.CurrentArmor <= 0f)
        {
            Process(
                CombatTriggerType.ArmorBroken,
                gameObject,
                new[] { PlayerTag, "state.armor_broken" },
                HealthNormalized(),
                result.AppliedAmount,
                0f);
        }

        observedArmor = health.CurrentArmor;
    }

    private void HandleVitalsChanged()
    {
        bool nextLowHealth = IsLowHealth();
        if (nextLowHealth && !lowHealthActive)
        {
            Process(
                CombatTriggerType.LowHealthEntered,
                gameObject,
                new[] { PlayerTag, "state.low_health" },
                HealthNormalized(),
                health.CurrentHealth,
                health.MaxHealth);
        }

        lowHealthActive = nextLowHealth;
        observedArmor = health.CurrentArmor;
    }

    private void Process(
        CombatTriggerType trigger,
        GameObject target,
        IReadOnlyList<string> targetTags,
        float targetHealthNormalized,
        float primaryValue,
        float secondaryValue)
    {
        currentEventTarget = target;
        var context = new CombatTriggerContext(
            ++nextEventId,
            ResolveTick(),
            trigger,
            gameObject.GetEntityId().ToString(),
            target != null ? target.GetEntityId().ToString() : string.Empty,
            new[] { PlayerTag },
            targetTags,
            targetHealthNormalized,
            primaryValue,
            secondaryValue);
        IReadOnlyList<CombatRuleExecution> executions =
            engine.Process(context);
        LastSpreadCount = 0;

        for (int index = 0; index < executions.Count; index++)
        {
            Execute(executions[index]);
        }

        currentEventTarget = null;
    }

    private void Execute(CombatRuleExecution execution)
    {
        CombatRuleEffectDefinition effect = execution.Effect;
        if (effect == null)
        {
            return;
        }

        int applied = effect.EffectKind switch
        {
            CombatRuleEffectKind.ApplyStatus => ApplyStatus(
                ResolveDirectTarget(effect.Target),
                effect.GameplayEffect),
            CombatRuleEffectKind.SpreadStatus => SpreadStatus(effect),
            CombatRuleEffectKind.ShowHudMessage => ShowHudMessage(effect),
            _ => 0
        };

        if (applied <= 0 &&
            effect.EffectKind != CombatRuleEffectKind.ShowHudMessage)
        {
            return;
        }

        TriggerCount++;
        LastTriggeredRuleId = execution.RuleId;
    }

    private GameObject ResolveDirectTarget(CombatRuleTarget target)
    {
        return target == CombatRuleTarget.EventSource
            ? gameObject
            : currentEventTarget;
    }

    private int ApplyStatus(
        GameObject target,
        GameplayEffectDefinition definition)
    {
        if (target == null || definition == null)
        {
            return 0;
        }

        EnemyBurnEffectController burn =
            target.GetComponent<EnemyBurnEffectController>();
        return burn != null && burn.ApplyStatus(definition, gameObject)
            ? 1
            : 0;
    }

    private int SpreadStatus(CombatRuleEffectDefinition effect)
    {
        EnemyController origin =
            currentEventTarget != null
                ? currentEventTarget.GetComponent<EnemyController>()
                : null;
        if (origin == null || effect.GameplayEffect == null)
        {
            return 0;
        }

        neighborBuffer.Clear();
        EnemySpatialIndexService index = EnemySpatialIndexService.Instance;
        if (index != null)
        {
            index.CollectAliveNeighbors(
                origin,
                effect.Radius,
                "enemy",
                neighborBuffer,
                effect.MaximumTargets);
        }
        else
        {
            EnemyController[] candidates = FindObjectsByType<EnemyController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.InstanceID);
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Length &&
                 neighborBuffer.Count < effect.MaximumTargets;
                 candidateIndex++)
            {
                if (EnemyNeighborQueryUtility.IsInRange(
                        origin,
                        candidates[candidateIndex],
                        effect.Radius))
                {
                    neighborBuffer.Add(candidates[candidateIndex]);
                }
            }
        }

        int applied = 0;
        for (int indexValue = 0;
             indexValue < neighborBuffer.Count;
             indexValue++)
        {
            EnemyBurnEffectController burn = neighborBuffer[indexValue]
                .GetComponent<EnemyBurnEffectController>();
            if (burn != null &&
                burn.ApplyStatus(effect.GameplayEffect, gameObject))
            {
                applied++;
            }
        }

        LastSpreadCount = applied;
        return applied;
    }

    private int ShowHudMessage(CombatRuleEffectDefinition effect)
    {
        if (LastSpreadCount <= 0 || string.IsNullOrWhiteSpace(effect.HudMessage))
        {
            return 0;
        }

        UnifiedGameHud hud = FindAnyObjectByType<UnifiedGameHud>();
        if (hud == null)
        {
            return 0;
        }

        hud.ShowRewardCue(
            effect.HudMessage.Replace(
                "{count}",
                LastSpreadCount.ToString()),
            true);
        return 1;
    }

    private long ResolveTick()
    {
        long tick = WaveDirector.Active?.Simulation?.Tick ?? ++fallbackTick;
        fallbackTick = Math.Max(fallbackTick, tick);
        return tick;
    }

    private IReadOnlyList<string> CollectTags(
        GameObject target,
        bool includeDeathMemory)
    {
        var tags = new List<string>(6);
        if (target == gameObject)
        {
            tags.Add(PlayerTag);
            return tags;
        }

        EnemyController enemy = target.GetComponent<EnemyController>();
        EnemyBurnEffectController burn =
            target.GetComponent<EnemyBurnEffectController>();
        if (enemy != null || burn != null)
        {
            tags.Add(EnemyTag);
            Health enemyHealth = target.GetComponent<Health>();
            if (enemyHealth != null && !enemyHealth.IsDead)
            {
                tags.Add(AliveTag);
            }
            WaveEnemyLifecycle lifecycle =
                target.GetComponent<WaveEnemyLifecycle>();
            if (lifecycle != null &&
                !string.IsNullOrWhiteSpace(lifecycle.EnemyTypeId))
            {
                tags.Add("enemy.type." + lifecycle.EnemyTypeId);
            }
            if (burn != null &&
                (burn.IsBurning ||
                 includeDeathMemory && burn.WasBurningWhenKilled))
            {
                tags.Add(BurningTag);
            }
        }

        return tags;
    }

    private float HealthNormalized() => health != null && health.MaxHealth > 0f
        ? Mathf.Clamp01(health.CurrentHealth / health.MaxHealth)
        : 0f;

    private bool IsLowHealth() => health != null && !health.IsDead &&
        health.CurrentHealth > 0f &&
        HealthNormalized() < LowHealthThreshold;

    private void EnsureBuildCatalog()
    {
        if (availableBuilds != null && availableBuilds.Count > 0)
        {
            return;
        }

        CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
        availableBuilds = catalog != null
            ? new List<CombatBuildDefinition>(catalog.CombatBuilds)
            : new List<CombatBuildDefinition>();
    }

    private int ResolveRunSeed() => configuredRunSeed ??
        GetComponent<PlayerUpgradeController>()?.RunSeed ?? 18018;

    private CombatRuleEngine CreateEngineForRun(int runSeed) =>
        new(runSeed, availableBuilds);

    private void InstallStarterBuilds()
    {
        if (engine == null)
        {
            return;
        }
        for (int index = 0; index < availableBuilds.Count; index++)
        {
            CombatBuildDefinition build = availableBuilds[index];
            if (build != null && build.InstallOnRunStart)
            {
                engine.Install(build);
            }
        }
    }
}
