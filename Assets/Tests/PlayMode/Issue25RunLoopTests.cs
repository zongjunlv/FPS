using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue25RunLoopTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [Test]
        public void ResultSnapshotCopiesValuesAndUpgradeHistory()
        {
            Type outcomeType = RuntimeType("MissionFlowState");
            Type summaryType = RuntimeType("MissionRunSummary");
            object summary = Activator.CreateInstance(summaryType, new object[]
            {
                Enum.Parse(outcomeType, "Victory"),
                3, 3, 18, 42, 30, 95.5f, 4, 37.5f, 5,
                new List<string> { "武器伤害 ×2", "移动速度 ×1" }
            });

            Assert.That(Get<bool>(summaryType, summary, "IsValid"), Is.True);
            Assert.That(Get<int>(summaryType, summary, "CompletedWaves"), Is.EqualTo(3));
            Assert.That(Get<int>(summaryType, summary, "Kills"), Is.EqualTo(18));
            Assert.That(Get<float>(summaryType, summary, "Accuracy"),
                Is.EqualTo(30f / 42f).Within(0.0001f));
            Assert.That(Get<float>(summaryType, summary, "DamageTakenAmount"),
                Is.EqualTo(37.5f));
            Assert.That(Get<int>(summaryType, summary, "FinalLevel"), Is.EqualTo(5));
            Assert.That(((IReadOnlyList<string>)summaryType
                .GetProperty("SelectedUpgrades").GetValue(summary)).Count,
                Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator PlayerDeathStopsAllRunProgressAndShowsCompleteResult()
        {
            yield return LoadRuntime();
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component mission = player.GetComponent(RuntimeType("CityNewMissionController"));
            Component director = Find(RuntimeType("WaveDirector"));
            Component rewards = player.GetComponent(RuntimeType("PlayerLootRewardController"));
            Component progression = player.GetComponent(RuntimeType("PlayerRunProgression"));
            Component legacyFailure = player.GetComponent(
                RuntimeType("PlayerFailureFlowController"));
            Assert.That(legacyFailure, Is.Not.Null);
            Assert.That(Get<bool>(legacyFailure.GetType(), legacyFailure,
                "ExternalPresentationEnabled"), Is.True,
                "CityNew 必须在死亡前关闭旧失败结算展示。");

            yield return KillOneEnemyAsPlayer(director, player);
            yield return ResolveAllChoices(player);
            int experienceBefore = CurrentExperience(progression);
            int rewardsBefore = Get<int>(rewards.GetType(), rewards, "SpawnedStackCount");

            ApplyLethalDamage(player.GetComponent(RuntimeType("Health")), null);
            yield return null;
            Assert.That(Get<object>(mission.GetType(), mission, "State").ToString(),
                Is.EqualTo("Defeat"));
            Assert.That(Get<bool>(rewards.GetType(), rewards, "AcceptingRewards"), Is.False);
            Assert.That(Get<bool>(legacyFailure.GetType(), legacyFailure,
                "ExternalPresentationEnabled"), Is.True);
            Assert.That(Get<int>(rewards.GetType(), rewards, "PendingRewardCount"), Is.Zero);
            Assert.That(Get<bool>(director.GetType(), director, "IsRunning"), Is.False);
            Assert.That(ActiveEnemyCount(director), Is.Zero);
            AssertSingleOutcomeLock(player, "Defeat");
            AssertCompleteResult(mission, false);

            yield return new WaitForSecondsRealtime(0.8f);
            Assert.That(CurrentExperience(progression), Is.EqualTo(experienceBefore));
            Assert.That(Get<int>(rewards.GetType(), rewards, "SpawnedStackCount"),
                Is.EqualTo(rewardsBefore));
        }

        [UnityTest]
        public IEnumerator OutcomePresentationWaitsForHudInsteadOfFallingBackToLegacyGui()
        {
            yield return LoadRuntime();
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component mission = player.GetComponent(RuntimeType("CityNewMissionController"));
            Component oldHud = Find(RuntimeType("UnifiedGameHud"));
            UnityEngine.Object.DestroyImmediate(oldHud.gameObject);

            ApplyLethalDamage(player.GetComponent(RuntimeType("Health")), null);
            yield return null;
            Assert.That(mission.GetType().GetProperty("OutcomeView").GetValue(mission),
                Is.Null, "HUD 缺失时不得错误复用已经销毁的展示。 ");

            GameObject canvasObject = new GameObject(
                "Late Outcome HUD", typeof(RectTransform), typeof(Canvas));
            canvasObject.AddComponent(RuntimeType("UnifiedGameHud"));
            yield return null;
            yield return null;

            Component view = (Component)mission.GetType()
                .GetProperty("OutcomeView").GetValue(mission);
            Assert.That(view, Is.Not.Null,
                "结算必须等待统一 HUD 就绪，不能退回旧 IMGUI。 ");
            Assert.That(Get<bool>(view.GetType(), view, "IsVisible"), Is.True);
        }

        [UnityTest]
        public IEnumerator DefeatRestartRestoresFreshRunBaseline()
        {
            yield return LoadRuntime();
            GameObject oldPlayer = GameObject.FindGameObjectWithTag("Player");
            Component oldMission = oldPlayer.GetComponent(RuntimeType("CityNewMissionController"));
            ApplyLethalDamage(oldPlayer.GetComponent(RuntimeType("Health")), null);
            yield return null;

            Assert.That(oldMission.GetType().GetMethod("RestartLevel")
                .Invoke(oldMission, null), Is.EqualTo(true));
            yield return WaitForNewRuntime(oldPlayer);
            AssertFreshRunBaseline(GameObject.FindGameObjectWithTag("Player"));
        }

        [UnityTest]
        public IEnumerator VictoryResultAndRestartCompleteTheWholeLoop()
        {
            yield return LoadRuntime();
            GameObject oldPlayer = GameObject.FindGameObjectWithTag("Player");
            Component mission = oldPlayer.GetComponent(RuntimeType("CityNewMissionController"));
            Component director = Find(RuntimeType("WaveDirector"));
            yield return CompleteAllWaves(director, oldPlayer, 55f);
            Assert.That(Get<object>(mission.GetType(), mission, "State").ToString(),
                Is.EqualTo("ActivateTerminal"));
            CompleteTerminal(mission, oldPlayer);

            string preExtractionState = Get<object>(
                mission.GetType(), mission, "State").ToString();
            Assert.That(preExtractionState, Is.EqualTo("ExtractionAvailable"),
                "波次完成后任务状态异常。当前状态：" + preExtractionState);
            Assert.That(mission.GetType().GetMethod("TryEnterExtraction")
                .Invoke(mission, new object[] { oldPlayer }), Is.EqualTo(true));
            yield return null;
            Assert.That(Get<object>(mission.GetType(), mission, "State").ToString(),
                Is.EqualTo("Victory"));
            AssertSingleOutcomeLock(oldPlayer, "Victory");
            AssertCompleteResult(mission, true);
            object summary = mission.GetType().GetProperty("OutcomeSummary").GetValue(mission);
            Type summaryType = summary.GetType();
            Assert.That(Get<int>(summaryType, summary, "CompletedWaves"), Is.EqualTo(3));
            Assert.That(Get<int>(summaryType, summary, "TotalWaves"), Is.EqualTo(3));
            Assert.That(Get<int>(summaryType, summary, "Kills"), Is.EqualTo(18));
            Assert.That(((IReadOnlyList<string>)summaryType
                .GetProperty("SelectedUpgrades").GetValue(summary)).Count,
                Is.GreaterThan(0));
            Component outcomeView = (Component)mission.GetType()
                .GetProperty("OutcomeView").GetValue(mission);
            Assert.That(Get<string>(outcomeView.GetType(), outcomeView, "UpgradeText"),
                Does.Not.Contain("未选择"));

            Assert.That(mission.GetType().GetMethod("RestartLevel")
                .Invoke(mission, null), Is.EqualTo(true));
            Assert.That(mission.GetType().GetMethod("RestartLevel")
                .Invoke(mission, null), Is.EqualTo(false),
                "同一帧重复重开必须被幂等门闩拒绝。");
            yield return WaitForNewRuntime(oldPlayer);
            AssertFreshRunBaseline(GameObject.FindGameObjectWithTag("Player"));
        }

        private static IEnumerator LoadRuntime()
        {
            yield return SceneManager.LoadSceneAsync(CityNewScene, LoadSceneMode.Single);
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                Component director = Find(RuntimeType("WaveDirector"));
                if (player != null && director != null &&
                    player.GetComponent(RuntimeType("CityNewMissionController")) != null &&
                    player.GetComponent(RuntimeType("PlayerLootRewardController")) != null &&
                    Find(RuntimeType("UnifiedGameHud")) != null &&
                    ActiveEnemyCount(director) > 0)
                {
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("CityNew 局内系统未在期限内完成初始化。");
        }

        private static IEnumerator WaitForNewRuntime(GameObject oldPlayer)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                GameObject player = GameObject.FindGameObjectWithTag("Player");
                Component director = Find(RuntimeType("WaveDirector"));
                if (player != null && player != oldPlayer &&
                    director != null && ActiveEnemyCount(director) > 0)
                {
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("重开后没有创建全新的局内运行时。");
        }

        private static IEnumerator CompleteAllWaves(
            Component director, GameObject player, float timeout)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!Get<bool>(director.GetType(), director, "IsCompleted") &&
                   Time.realtimeSinceStartup < deadline)
            {
                DisableEnemyThreats(director);
                foreach (Component enemy in ActiveEnemies(director))
                {
                    Component health = enemy.GetComponent(RuntimeType("Health"));
                    if (!Get<bool>(health.GetType(), health, "IsDead"))
                    {
                        ApplyLethalDamage(health, player);
                    }
                }
                yield return null;
                yield return ResolveAllChoices(player);
                yield return null;
            }
            Assert.That(Get<bool>(director.GetType(), director, "IsCompleted"), Is.True,
                "三波敌人未在期限内全部结算。");
        }

        private static IEnumerator KillOneEnemyAsPlayer(Component director, GameObject player)
        {
            List<Component> enemies = ActiveEnemies(director);
            Assert.That(enemies.Count, Is.GreaterThan(0));
            ApplyLethalDamage(enemies[0].GetComponent(RuntimeType("Health")), player);
            yield return null;
        }

        private static IEnumerator ResolveAllChoices(GameObject player)
        {
            Component upgrades = player.GetComponent(RuntimeType("PlayerUpgradeController"));
            int guard = 0;
            while (upgrades != null &&
                   (Get<bool>(upgrades.GetType(), upgrades, "IsChoiceOpen") ||
                    Get<int>(upgrades.GetType(), upgrades, "PendingChoiceCount") > 0) &&
                   guard++ < 20)
            {
                if (Get<bool>(upgrades.GetType(), upgrades, "IsChoiceOpen"))
                {
                    object candidates = upgrades.GetType()
                        .GetProperty("CurrentCandidates").GetValue(upgrades);
                    int candidateCount = 0;
                    foreach (object _ in (IEnumerable)candidates)
                    {
                        candidateCount++;
                    }
                    bool selected = false;
                    for (int index = 0; index < candidateCount && !selected; index++)
                    {
                        selected = (bool)upgrades.GetType().GetMethod("TrySelect")
                            .Invoke(upgrades, new object[] { index });
                    }
                }
                yield return null;
            }
        }

        private static void CompleteTerminal(Component mission, GameObject player)
        {
            Component terminal = (Component)mission.GetType()
                .GetProperty("Terminal").GetValue(mission);
            Type terminalType = terminal.GetType();
            terminalType.GetMethod("Configure").Invoke(terminal, new object[]
            {
                0.01f,
                Enum.Parse(RuntimeType("TerminalInterruptionProgressMode"), "Reset"),
                Enum.Parse(RuntimeType("TerminalCompletionMode"), "Silent"),
                32f, 1f
            });
            terminalType.GetMethod("TryBegin").Invoke(terminal, new object[] { player });
            terminalType.GetMethod("Advance").Invoke(terminal, new object[] { player, 0.05f });
        }

        private static void AssertCompleteResult(Component mission, bool victory)
        {
            object summary = mission.GetType().GetProperty("OutcomeSummary").GetValue(mission);
            Type summaryType = summary.GetType();
            Assert.That(Get<bool>(summaryType, summary, "IsValid"), Is.True);
            Component view = (Component)mission.GetType().GetProperty("OutcomeView").GetValue(mission);
            Assert.That(view, Is.Not.Null);
            Assert.That(Get<bool>(view.GetType(), view, "IsVisible"), Is.True);
            string text = Get<string>(view.GetType(), view, "SummaryText");
            Assert.That(text, Does.Contain("完成波次"));
            Assert.That(text, Does.Contain("击杀数量"));
            Assert.That(text, Does.Contain("开火数量"));
            Assert.That(text, Does.Contain("命中率"));
            Assert.That(text, Does.Contain("本局用时"));
            Assert.That(text, Does.Contain("承受伤害"));
            Assert.That(text, Does.Contain("最终等级"));
            Assert.That(Get<string>(view.GetType(), view, "TitleText"),
                Does.Contain(victory ? "完成" : "失败"));
        }

        private static void AssertSingleOutcomeLock(GameObject player, string reason)
        {
            Component coordinator = player.GetComponent(RuntimeType("GameplayLockCoordinator"));
            object state = coordinator.GetType().GetProperty("State").GetValue(coordinator);
            Type stateType = state.GetType();
            Assert.That(Get<int>(stateType, state, "ActiveLockCount"), Is.EqualTo(1));
            Assert.That(Get<object>(stateType, state, "TopReason").ToString(),
                Does.Contain(reason));
            Assert.That(Time.timeScale, Is.Zero);
        }

        private static void AssertFreshRunBaseline(GameObject player)
        {
            Assert.That(player, Is.Not.Null);
            Component mission = player.GetComponent(RuntimeType("CityNewMissionController"));
            Component progression = player.GetComponent(RuntimeType("PlayerRunProgression"));
            Component upgrades = player.GetComponent(RuntimeType("PlayerUpgradeController"));
            Component inventory = player.GetComponent(RuntimeType("PlayerInventoryController"));
            Component coordinator = player.GetComponent(RuntimeType("GameplayLockCoordinator"));
            Assert.That(Get<object>(mission.GetType(), mission, "State").ToString(),
                Is.EqualTo("EliminateTargets"));
            Assert.That(Get<bool>(mission.GetType(), mission, "IsRestarting"), Is.False);
            object summary = mission.GetType().GetProperty("OutcomeSummary").GetValue(mission);
            Assert.That(Get<bool>(summary.GetType(), summary, "IsValid"), Is.False);
            Assert.That(CurrentExperience(progression), Is.Zero);
            Assert.That(Get<int>(upgrades.GetType(), upgrades, "SelectedUpgradeCount"), Is.Zero);
            object state = coordinator.GetType().GetProperty("State").GetValue(coordinator);
            Assert.That(Get<int>(state.GetType(), state, "ActiveLockCount"), Is.Zero);
            object inventoryState = inventory.GetType().GetProperty("Inventory").GetValue(inventory);
            Assert.That((int)inventoryState.GetType().GetProperty("OccupiedSlotCount")
                .GetValue(inventoryState), Is.Zero);
            object quickSlots = inventory.GetType().GetProperty("QuickSlots").GetValue(inventory);
            Assert.That(quickSlots.GetType().GetMethod("GetBoundId")
                .Invoke(quickSlots, new object[] { 0 }), Is.EqualTo("medical_kit"));
            Assert.That(quickSlots.GetType().GetMethod("GetBoundId")
                .Invoke(quickSlots, new object[] { 1 }), Is.EqualTo("armor_pack"));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        private static int CurrentExperience(Component progression)
        {
            object snapshot = progression.GetType().GetProperty("CurrentProgress")
                .GetValue(progression);
            return Get<int>(snapshot.GetType(), snapshot, "CurrentExperience");
        }

        private static void ApplyLethalDamage(Component health, GameObject source)
        {
            object damage = Activator.CreateInstance(RuntimeType("DamageInfo"), new object[]
            {
                100000f, health.transform.position, Vector3.forward, source
            });
            health.GetType().GetMethod("ApplyDamage").Invoke(health, new[] { damage });
        }

        private static void DisableEnemyThreats(Component director)
        {
            foreach (Component enemy in ActiveEnemies(director))
            {
                Behaviour combat = enemy.GetComponent("EnemyCombatController") as Behaviour;
                if (combat != null) combat.enabled = false;
            }
        }

        private static int ActiveEnemyCount(Component director) => ActiveEnemies(director).Count;

        private static List<Component> ActiveEnemies(Component director)
        {
            object dictionary = director.GetType().GetProperty("ActiveEnemies").GetValue(director);
            var result = new List<Component>();
            foreach (object pair in (IEnumerable)dictionary)
            {
                object handle = pair.GetType().GetProperty("Value").GetValue(pair);
                Component controller = (Component)handle.GetType()
                    .GetProperty("Controller").GetValue(handle);
                if (controller != null) result.Add(controller);
            }
            return result;
        }

        private static Component Find(Type type) =>
            (Component)UnityEngine.Object.FindAnyObjectByType(type);

        private static Type RuntimeType(string name) =>
            RuntimeTypeResolver.GetType(name) ??
            throw new InvalidOperationException("Missing runtime type: " + name);

        private static T Get<T>(Type type, object instance, string property) =>
            (T)type.GetProperty(property).GetValue(instance);
    }
}
