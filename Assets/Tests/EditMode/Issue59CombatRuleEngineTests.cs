using System;
using System.Collections.Generic;
using System.Linq;
using FPS.GameplayEffects;
using FPS.SaveGame;
using NUnit.Framework;
using UnityEngine;

namespace FPS.Tests.Architecture
{
    public sealed class Issue59CombatRuleEngineTests
    {
        [Test]
        public void BurningKillProducesOrderedChainOnceAndHonorsCooldown()
        {
            GameplayEffectDefinition burn = CreateBurn();
            CombatRuleEffectDefinition hud = Effect(
                "effect.hud.burn_spread",
                CombatRuleEffectKind.ShowHudMessage,
                CombatRuleTarget.EventSource,
                null,
                string.Empty);
            CombatRuleEffectDefinition spread = Effect(
                "effect.status.burn_spread",
                CombatRuleEffectKind.SpreadStatus,
                CombatRuleTarget.NearbyEnemies,
                burn,
                "status.burning",
                hud);
            CombatRuleDefinition rule = Rule(
                "rule.kill.burning_spread",
                CombatTriggerType.Kill,
                5,
                spread);
            CombatBuildDefinition build = Build(rule);
            var engine = new CombatRuleEngine(18018, new[] { build });

            try
            {
                Assert.That(engine.Install(build), Is.True);
                CombatTriggerContext context = Context(1, 10);
                var result = engine.Process(context);

                Assert.That(result.Select(value => value.Effect.StableId),
                    Is.EqualTo(new[]
                    {
                        "effect.status.burn_spread",
                        "effect.hud.burn_spread"
                    }));
                Assert.That(engine.Process(context), Is.Empty,
                    "同一事件不能重复结算。" );
                Assert.That(engine.Process(Context(2, 12)), Is.Empty,
                    "规则冷却期间不能再次结算。" );
                Assert.That(engine.Process(Context(3, 15)), Has.Count.EqualTo(2));
            }
            finally
            {
                Destroy(build, rule, spread, hud, burn);
            }
        }

        [Test]
        public void SnapshotRestoreKeepsBuildCooldownAndDedupeWindow()
        {
            GameplayEffectDefinition burn = CreateBurn();
            CombatRuleEffectDefinition spread = Effect(
                "effect.status.burn_spread",
                CombatRuleEffectKind.SpreadStatus,
                CombatRuleTarget.NearbyEnemies,
                burn,
                "status.burning");
            CombatRuleDefinition rule = Rule(
                "rule.kill.burning_spread",
                CombatTriggerType.Kill,
                30,
                spread);
            CombatBuildDefinition build = Build(rule);
            var original = new CombatRuleEngine(18018, new[] { build });
            var restored = new CombatRuleEngine(18018, new[] { build });

            try
            {
                original.Install(build);
                Assert.That(original.Process(Context(7, 100)), Has.Count.EqualTo(1));
                CombatRuleRuntimeSnapshot snapshot = original.CaptureSnapshot(7);
                Assert.That(restored.TryRestore(snapshot, out string error),
                    Is.True, error);
                Assert.That(restored.Process(Context(7, 100)), Is.Empty);
                Assert.That(restored.Process(Context(8, 129)), Is.Empty);
                Assert.That(restored.Process(Context(9, 130)), Has.Count.EqualTo(1));
            }
            finally
            {
                Destroy(build, rule, spread, burn);
            }
        }

        [Test]
        public void StandardTriggerContextsAreRepresentable()
        {
            var expected = new[]
            {
                CombatTriggerType.Hit,
                CombatTriggerType.Kill,
                CombatTriggerType.ReloadCompleted,
                CombatTriggerType.DamageTaken,
                CombatTriggerType.ArmorBroken,
                CombatTriggerType.LowHealthEntered
            };

            Assert.That(Enum.GetValues(typeof(CombatTriggerType)),
                Is.EquivalentTo(expected));
        }

        [Test]
        public void ValidatorFindsInvalidTagMissingEffectCycleAndBadTarget()
        {
            CombatRuleEffectDefinition a = Effect(
                "effect.a",
                CombatRuleEffectKind.SpreadStatus,
                CombatRuleTarget.EventTarget,
                null,
                "Bad Tag");
            CombatRuleEffectDefinition b = Effect(
                "effect.b",
                CombatRuleEffectKind.ShowHudMessage,
                CombatRuleTarget.EventSource,
                null,
                string.Empty,
                a);
            a.Configure(
                "effect.a",
                CombatRuleEffectKind.SpreadStatus,
                CombatRuleTarget.EventTarget,
                null,
                "Bad Tag",
                6f,
                4,
                string.Empty,
                b);
            CombatRuleDefinition rule = Rule(
                "rule.invalid",
                CombatTriggerType.Kill,
                0,
                a);
            CombatBuildDefinition build = Build(rule);

            try
            {
                var issues = CombatRuleContentValidator.Validate(
                    new[] { build });
                Assert.That(issues.Select(value => value.Code), Does.Contain(
                    CombatRuleValidationIssueCode.InvalidTag));
                Assert.That(issues.Select(value => value.Code), Does.Contain(
                    CombatRuleValidationIssueCode.MissingEffect));
                Assert.That(issues.Select(value => value.Code), Does.Contain(
                    CombatRuleValidationIssueCode.CycleDependency));
                Assert.That(issues.Select(value => value.Code), Does.Contain(
                    CombatRuleValidationIssueCode.IncompatibleTarget));
            }
            finally
            {
                Destroy(build, rule, b, a);
            }
        }

        [Test]
        public void CombatBuildStateRoundTripsAndParticipatesInChecksum()
        {
            RunSnapshot snapshot = ValidSave();
            snapshot.CombatBuild = new CombatBuildSnapshot
            {
                NextEventId = 12,
                InstalledBuildIds = new List<string>
                {
                    "build.ember_chain"
                },
                Cooldowns = new List<CombatRuleCooldownSaveSnapshot>
                {
                    new()
                    {
                        RuleId = "rule.kill.burning_spread",
                        ReadyTick = 90
                    }
                },
                ProcessedEventIds = new List<long> { 10, 12 }
            };

            string json = RunSnapshotCodec.Serialize(snapshot);
            Assert.That(RunSnapshotCodec.TryDeserialize(
                json,
                out RunSnapshot loaded,
                out string error), Is.True, error);
            Assert.That(loaded.CombatBuild.InstalledBuildIds,
                Is.EqualTo(snapshot.CombatBuild.InstalledBuildIds));
            Assert.That(loaded.CombatBuild.Cooldowns[0].ReadyTick,
                Is.EqualTo(90));
            Assert.That(loaded.CombatBuild.ProcessedEventIds,
                Is.EqualTo(new long[] { 10, 12 }));

            string checksum = SnapshotChecksum.Compute(loaded);
            loaded.CombatBuild.Cooldowns[0].ReadyTick++;
            Assert.That(SnapshotChecksum.Compute(loaded),
                Is.Not.EqualTo(checksum));
        }

        private static CombatTriggerContext Context(long eventId, long tick) =>
            new(
                eventId,
                tick,
                CombatTriggerType.Kill,
                "player",
                "enemy:7",
                new[] { "entity.player" },
                new[] { "entity.enemy", "status.burning" },
                0f);

        private static RunSnapshot ValidSave()
        {
            return new RunSnapshot
            {
                Seed = 18018,
                Health = 100f,
                Armor = 0f,
                CurrentWeaponId = "rifle",
                Weapons = new List<WeaponAmmoSnapshot>
                {
                    new()
                    {
                        WeaponId = "rifle",
                        Magazine = 30,
                        Reserve = 90
                    }
                }
            };
        }

        private static GameplayEffectDefinition CreateBurn()
        {
            var burn = ScriptableObject.CreateInstance<GameplayEffectDefinition>();
            burn.ConfigureTimed(
                "status.burn",
                4f,
                1f,
                4f,
                3,
                GameplayEffectStackRefreshPolicy.RefreshAllDurations);
            return burn;
        }

        private static CombatRuleEffectDefinition Effect(
            string id,
            CombatRuleEffectKind kind,
            CombatRuleTarget target,
            GameplayEffectDefinition effect,
            string tag,
            params CombatRuleEffectDefinition[] followUps)
        {
            var value = ScriptableObject.CreateInstance<
                CombatRuleEffectDefinition>();
            value.Configure(
                id,
                kind,
                target,
                effect,
                tag,
                7f,
                4,
                "燃烧扩散",
                followUps);
            return value;
        }

        private static CombatRuleDefinition Rule(
            string id,
            CombatTriggerType trigger,
            int cooldown,
            params CombatRuleEffectDefinition[] effects)
        {
            var value = ScriptableObject.CreateInstance<CombatRuleDefinition>();
            value.Configure(
                id,
                trigger,
                new[] { "entity.player" },
                new[] { "entity.enemy", "status.burning" },
                Array.Empty<string>(),
                0f,
                1f,
                cooldown,
                10000,
                effects);
            return value;
        }

        private static CombatBuildDefinition Build(
            params CombatRuleDefinition[] rules)
        {
            var build = ScriptableObject.CreateInstance<CombatBuildDefinition>();
            build.Configure(
                "build.ember_chain",
                "余烬连锁",
                true,
                rules);
            return build;
        }

        private static void Destroy(params UnityEngine.Object[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(values[index]);
                }
            }
        }
    }
}
