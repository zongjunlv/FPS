using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FPS.Core.GameModes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace FPS.Tests.PlayMode
{
    public sealed class CoopEconomyCompositionIsolationTests
    {
        private readonly List<GameObject> created = new();
        private readonly HashSet<UnifiedGameHud> existingHuds = new();
        private readonly HashSet<EventSystem> existingEventSystems = new();
        private readonly HashSet<WorldItemPickup> existingPickups = new();

        [SetUp]
        public void SetUp()
        {
            GameModeContext.ResetForTests();
            existingHuds.Clear();
            existingEventSystems.Clear();
            existingPickups.Clear();
            foreach (UnifiedGameHud hud in Object.FindObjectsByType<UnifiedGameHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                existingHuds.Add(hud);
            foreach (EventSystem system in Object.FindObjectsByType<EventSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                existingEventSystems.Add(system);
            foreach (WorldItemPickup pickup in Object.FindObjectsByType<WorldItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                existingPickups.Add(pickup);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject value in created)
                if (value != null) Object.Destroy(value);
            created.Clear();
            foreach (UnifiedGameHud hud in Object.FindObjectsByType<UnifiedGameHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!existingHuds.Contains(hud)) Object.Destroy(hud.gameObject);
            foreach (EventSystem system in Object.FindObjectsByType<EventSystem>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!existingEventSystems.Contains(system)) Object.Destroy(system.gameObject);
            // Initial supplies are independent world roots. Clean up even a
            // failed lifecycle assertion so one red test cannot block the next rig.
            foreach (WorldItemPickup pickup in Object.FindObjectsByType<WorldItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!existingPickups.Contains(pickup)) Object.Destroy(pickup.gameObject);
            GameModeContext.ResetForTests();
            Time.timeScale = 1f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CoopCompositionNeverInstallsLocalEconomyButKeepsCombatHud()
        {
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.Combat.IsInitialized, Is.True);
            Assert.That(rig.CompositionRoot.HudBootstrap.Hud, Is.Not.Null);
            Assert.That(rig.CompositionRoot.HudBootstrap.Hud.IsBound, Is.True);
            Assert.That(rig.CompositionRoot.PlayerHealth, Is.Not.Null);
            Assert.That(rig.Loadout.WeaponCount, Is.EqualTo(2));
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Null,
                "联机角色不应安装抢占TAB/快捷物品输入的本地库存控制器。");
            Assert.That(rig.GetComponent<PlayerUpgradeController>(), Is.Null);
            Assert.That(rig.GetComponent<PlayerWorldPickupController>(), Is.Null);
            Assert.That(rig.GetComponent<PlayerRunProgression>(), Is.Null);
            Assert.That(rig.GetComponent<PlayerLootRewardController>(), Is.Null);
            Assert.That(rig.GetComponent<PlayerFailureFlowController>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator SoloCompositionStillInstallsLocalEconomy()
        {
            GameModeContext.BeginTransition(GameModeId.SoloBattle, GameModeStage.Battle);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.GetComponent<PlayerInventoryController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerUpgradeController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerWorldPickupController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerRunProgression>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerLootRewardController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerFailureFlowController>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator TutorialKeepsItsLocalEconomyAndFailureFlow()
        {
            GameModeContext.BeginTransition(GameModeId.Tutorial, GameModeStage.Tutorial);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.GetComponent<PlayerInventoryController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerRunProgression>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerFailureFlowController>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator LeavingCoopUsesRequestedSoloRouteForNewComposition()
        {
            Assert.That(GameModeContext.TryActivate(GameModeId.Coop,
                GameModeStage.CoopBattle, out string routeError), Is.True, routeError);
            GameModeContext.BeginTransition(GameModeId.SoloBattle, GameModeStage.Battle);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            Assert.That(rig.CompositionRoot.IsInitialized, Is.True);
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Not.Null,
                "新场景尚未 Activate 时必须使用请求中的 Solo 路由，不可沿用旧 Coop 上下文。");
            Assert.That(rig.GetComponent<PlayerInventoryController>().enabled, Is.True);
            Assert.That(rig.GetComponent<PlayerFailureFlowController>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator RequestedCoopPreventsLateSoloBootstrapStart()
        {
            ActivateSoloBattle();
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Not.Null);

            // Reproduce the same-frame transition: the old scene is still
            // active Solo, but its destination is already authoritative Coop.
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            Assert.That(GameModeContext.IsActive(GameModeId.SoloBattle,
                GameModeStage.Battle), Is.True);
            CityNewInventoryBootstrap bootstrap =
                rig.gameObject.AddComponent<CityNewInventoryBootstrap>();
            yield return Settle();

            Assert.That(bootstrap.Pickups == null || bootstrap.Pickups.Length == 0,
                Is.True, "请求中的联机战斗不能由迟到的单机 Start 生成初始物资/碰撞体。");
            Assert.That(Object.FindObjectsByType<WorldItemPickup>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(pickup => !existingPickups.Contains(pickup)), Is.Zero);
        }

        [UnityTest]
        public IEnumerator CoopRetirementDestroysAlreadySpawnedSoloPickups()
        {
            ActivateSoloBattle();
            PlayerGameplayRig rig = CreateRig();
            CityNewInventoryBootstrap bootstrap =
                rig.gameObject.AddComponent<CityNewInventoryBootstrap>();
            yield return Settle();
            WorldItemPickup[] supplies = RequireFourActiveSupplies(bootstrap);
            var serverPackage = new GameObject("Authoritative Package Visual");
            created.Add(serverPackage);
            serverPackage.AddComponent<ReplicatedWorldItemVisual>()
                .Build(ItemEffectType.RestoreHealth);

            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            CoopSceneContentIsolation.DisableLegacyPlayerEconomy(rig.gameObject);

            Assert.That(bootstrap == null || !bootstrap.enabled, Is.True,
                "退役经济逻辑必须包含拥有初始物资的 Bootstrap，而不只是库存 Controller。");
            foreach (WorldItemPickup supply in supplies)
                Assert.That(supply == null || !supply.gameObject.activeInHierarchy,
                    Is.True, "独立物资 root 的碰撞体必须在退役当帧退出客户端物理世界。");
            Assert.That(serverPackage.activeInHierarchy, Is.True,
                "不能以屏蔽全部掉落视觉的方式清理本地初始物资。");

            yield return null;
            yield return null;
            Assert.That(bootstrap == null, Is.True);
            foreach (WorldItemPickup supply in supplies)
                Assert.That(supply == null, Is.True,
                    "初始物资是独立 world root；销毁 Bootstrap 也必须销毁它拥有的物资。");
            Assert.That(serverPackage.activeInHierarchy, Is.True);
        }

        [UnityTest]
        public IEnumerator SoloBootstrapKeepsItsFourActiveWorldPickups()
        {
            ActivateSoloBattle();
            PlayerGameplayRig rig = CreateRig();
            CityNewInventoryBootstrap bootstrap =
                rig.gameObject.AddComponent<CityNewInventoryBootstrap>();
            yield return Settle();
            WorldItemPickup[] supplies = RequireFourActiveSupplies(bootstrap);
            Assert.That(supplies.Select(pickup => pickup.Definition.StableId),
                Is.EquivalentTo(new[] { "medical_kit", "armor_pack", "rifle_ammo",
                    "handgun_ammo" }));
            Assert.That(rig.GetComponent<PlayerInventoryController>().enabled, Is.True);
        }

        [UnityTest]
        public IEnumerator DestroyingSoloBootstrapReleasesItsIndependentPickupRoots()
        {
            ActivateSoloBattle();
            PlayerGameplayRig rig = CreateRig();
            CityNewInventoryBootstrap bootstrap =
                rig.gameObject.AddComponent<CityNewInventoryBootstrap>();
            yield return Settle();
            WorldItemPickup[] supplies = RequireFourActiveSupplies(bootstrap);

            Object.Destroy(bootstrap);
            yield return null;
            yield return null;
            foreach (WorldItemPickup supply in supplies)
                Assert.That(supply == null, Is.True,
                    "Bootstrap 生命周期结束后不能留下独立拾取物和世界碰撞体。");
        }

        [UnityTest]
        public IEnumerator AcceptanceClientRequestsCoopBeforeCityNewMarkerAndRigInitialize()
        {
            ActivateSoloBattle();
            RequestAcceptanceRoute("client-a");
            Assert.That(GameModeContext.RequestedMode, Is.EqualTo(GameModeId.Coop));
            Assert.That(GameModeContext.RequestedStage,
                Is.EqualTo(GameModeStage.CoopBattle));
            Assert.That(GameModeContext.IsActive(GameModeId.SoloBattle,
                GameModeStage.Battle), Is.True,
                "验收入口只请求转场，不能抢先代替场景 marker 激活上下文。");

            var sceneRoot = new GameObject("Authored CityNew Mode Marker");
            sceneRoot.SetActive(false);
            created.Add(sceneRoot);
            GameModeSceneMarker marker = sceneRoot.AddComponent<GameModeSceneMarker>();
            marker.Configure(GameModeId.SoloBattle, GameModeStage.Battle);
            // Exercise the real public contract used by marker.Awake without
            // also installing scene BGM during a quiet CLI regression run.
            Assert.That(marker.ActivateContext(), Is.True, marker.ActivationError);
            Assert.That(GameModeContext.IsActive(GameModeId.Coop,
                GameModeStage.CoopBattle), Is.True);

            PlayerGameplayRig rig = CreateRig();
            CityNewInventoryBootstrap bootstrap =
                rig.gameObject.AddComponent<CityNewInventoryBootstrap>();
            yield return Settle();
            Assert.That(rig.GetComponent<PlayerInventoryController>(), Is.Null,
                "验收客户端也必须走正式 Coop composition，不可用一次性 isolation 假装同模式。");
            Assert.That(bootstrap.Pickups == null || bootstrap.Pickups.Length == 0,
                Is.True);
        }

        [Test]
        public void AcceptanceServerDoesNotOverrideExistingRequestedRoute()
        {
            ActivateSoloBattle();
            GameModeContext.BeginTransition(GameModeId.Tutorial, GameModeStage.Tutorial);
            int epoch = GameModeContext.Epoch;
            RequestAcceptanceRoute("server");
            Assert.That(GameModeContext.Epoch, Is.EqualTo(epoch));
            Assert.That(GameModeContext.RequestedMode, Is.EqualTo(GameModeId.Tutorial));
            Assert.That(GameModeContext.RequestedStage,
                Is.EqualTo(GameModeStage.Tutorial));
            Assert.That(GameModeContext.IsActive(GameModeId.SoloBattle,
                GameModeStage.Battle), Is.True);
        }

        [UnityTest]
        public IEnumerator CoopDeathNeverOpensSoloFailureOrPausesWorld()
        {
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            foreach (AudioSource audio in rig.GetComponentsInChildren<AudioSource>(true))
                audio.mute = true;
            rig.CompositionRoot.PlayerHealth.ApplyDamage(new DamageInfo(
                1000f, Vector3.zero, Vector3.forward, rig.gameObject));
            yield return null;
            Assert.That(rig.CompositionRoot.PlayerHealth.IsDead, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "联机倒地/失败不能由本地单机Controller暂停世界。");
            Assert.That(rig.GetComponent<GameplayLockCoordinator>().State
                .IsReasonActive(GameplayLockReason.Defeat), Is.False);
            PlayerFailureFlowController failure = rig.GetComponent<PlayerFailureFlowController>();
            Assert.That(failure == null || !failure.IsFailed, Is.True);
        }

        [UnityTest]
        public IEnumerator ExistingLocalEconomyIsDisabledAndUnsubscribedWithoutDestroyingHud()
        {
            GameModeContext.BeginTransition(GameModeId.SoloBattle, GameModeStage.Battle);
            PlayerGameplayRig rig = CreateRig();
            yield return Settle();
            PlayerInventoryController inventory = rig.GetComponent<PlayerInventoryController>();
            ItemDefinition medical = Resources.Load<ItemDefinition>(
                "Content/CityNew/Items/MedicalKit");
            Assert.That(medical, Is.Not.Null);
            inventory.RegisterItem(medical);
            Assert.That(inventory.BindQuickSlot(0, medical.StableId), Is.True);
            Assert.That(HasSubscriber(rig.CompositionRoot.PlayerHealth, "VitalsChanged",
                typeof(PlayerInventoryController)), Is.True,
                "测试必须覆盖已经启动且实际订阅了事件的库存组件。");
            Assert.That(inventory.Open(), Is.True);
            UnifiedGameHud hud = rig.CompositionRoot.HudBootstrap.Hud;
            InventoryView sharedView = hud.GetComponent<InventoryView>();
            Assert.That(sharedView.IsVisible, Is.True);
            Assert.That(hud.GetComponent<ConsumableQuickSlotHud>().GetBoundId(0),
                Is.EqualTo(medical.StableId));
            Assert.That(HasSubscriber(rig.Loadout.CurrentWeapon, "AmmoChanged",
                typeof(PlayerInventoryController)), Is.True);
            var legacyPackage = new GameObject("Existing Solo Pickup");
            created.Add(legacyPackage);
            legacyPackage.AddComponent<WorldItemPickup>().Configure(medical, 1);
            var serverPackage = new GameObject("Pure Server Package Visual");
            created.Add(serverPackage);
            serverPackage.AddComponent<ReplicatedWorldItemVisual>()
                .Build(ItemEffectType.RestoreHealth);
            var localComponents = new Behaviour[] { inventory,
                rig.GetComponent<PlayerUpgradeController>(),
                rig.GetComponent<PlayerWorldPickupController>(),
                rig.GetComponent<PlayerRunProgression>(),
                rig.GetComponent<PlayerLootRewardController>(),
                rig.GetComponent<PlayerFailureFlowController>() };

            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            CoopSceneContentIsolation.DisableLegacySceneEnemies();
            foreach (Behaviour local in localComponents)
                Assert.That(local == null || !local.enabled, Is.True,
                    $"{local?.GetType().Name} 不能继续运行本地经济逻辑。");
            Assert.That(legacyPackage.activeSelf, Is.False,
                "转场前已生成的单机拾取实体也必须停用。");
            Assert.That(serverPackage.activeSelf, Is.True,
                "隔离不能停用纯服务器掉落视觉。");
            foreach (AudioSource audio in rig.GetComponentsInChildren<AudioSource>(true))
                audio.mute = true;
            rig.CompositionRoot.PlayerHealth.ApplyDamage(new DamageInfo(
                1000f, Vector3.zero, Vector3.forward, rig.gameObject));
            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "等待OnDestroy解绑的同一帧死亡也不能触发单机失败暂停。");
            yield return null;
            yield return null;
            Assert.That(HasSubscriber(rig.CompositionRoot.PlayerHealth, "VitalsChanged",
                typeof(PlayerInventoryController)), Is.False,
                "只禁用Update不够：Health回调不能继续刷新/覆盖服务器背包。");
            Assert.That(HasSubscriber(rig.GetComponent<GameplayLockCoordinator>(),
                "ModalStateChanged", typeof(PlayerInventoryController)), Is.False);
            Assert.That(HasSubscriber(rig.Loadout.CurrentWeapon, "AmmoChanged",
                typeof(PlayerInventoryController)), Is.False);
            Assert.That(HasSubscriber(rig.CompositionRoot.PlayerHealth, "Died",
                typeof(PlayerFailureFlowController)), Is.False);
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.enabled, Is.True);
            Assert.That(hud.GetComponent<InventoryView>(), Is.SameAs(sharedView),
                "隔离本地控制器不能删除联机需要复用的纯视觉界面。");
            Assert.That(sharedView.IsVisible, Is.False);
            ConsumableQuickSlotHud quickSlots = hud.GetComponent<ConsumableQuickSlotHud>();
            if (quickSlots != null)
            {
                Assert.That(quickSlots.GetBoundId(0), Is.Empty,
                    "快捷槽不能保留已销毁本地库存/可用性委托。");
                Transform quickRoot = hud.HudLayer.Find("ConsumableQuickSlotHud");
                Assert.That(quickRoot == null || !quickRoot.gameObject.activeSelf, Is.True);
            }
        }

        private PlayerGameplayRig CreateRig()
        {
            PlayerGameplayRig rig = PlayerGameplayRig.Create(Vector3.zero, Quaternion.identity);
            created.Add(rig.gameObject);
            Assert.That(rig.TryValidate(out string rigError), Is.True, rigError);
            PlayerCombatCompositionRoot composition = rig.CompositionRoot;
            Assert.That(composition, Is.Not.Null);
            Assert.That(composition.enabled, Is.True);
            Assert.That(composition.HasConfigurationError, Is.False,
                composition.ConfigurationError);
            // Dynamic construction takes place inside the test coroutine, not
            // during scene activation. Finish the public composition contract
            // before letting consumer Start callbacks run on the next frame.
            Assert.That(composition.TryInitialize(), Is.True,
                composition.ConfigurationError);
            Assert.That(rig.Combat.IsInitialized, Is.True);
            return rig;
        }

        private static void ActivateSoloBattle()
        {
            GameModeContext.BeginTransition(GameModeId.SoloBattle, GameModeStage.Battle);
            Assert.That(GameModeContext.TryActivate(GameModeId.SoloBattle,
                GameModeStage.Battle, out string error), Is.True, error);
        }

        private static WorldItemPickup[] RequireFourActiveSupplies(
            CityNewInventoryBootstrap bootstrap)
        {
            Assert.That(bootstrap.Pickups, Is.Not.Null);
            Assert.That(bootstrap.Pickups.Length, Is.EqualTo(4),
                "Fixture 必须覆盖真实 Bootstrap.Start 生成的全部四个初始物资。");
            WorldItemPickup[] supplies = bootstrap.Pickups.ToArray();
            foreach (WorldItemPickup supply in supplies)
            {
                Assert.That(supply, Is.Not.Null);
                Assert.That(supply.gameObject.activeInHierarchy, Is.True);
                Collider collider = supply.GetComponent<Collider>();
                Assert.That(collider, Is.TypeOf<BoxCollider>());
                Assert.That(collider.enabled, Is.True);
                Assert.That(collider.isTrigger, Is.False);
            }
            return supplies;
        }

        private static void RequestAcceptanceRoute(string role)
        {
            // Acceptance is a separate runtime assembly and its installer is
            // intentionally internal. Parse its real command-line contract and
            // invoke the actual pre-scene route helper, without launching a
            // socket, changing credentials, or adding a test-only mode setter.
            Type argumentsType = Type.GetType(
                "FPS.Networking.Acceptance.Issue100RuntimeArguments, FPS.Networking.Acceptance",
                throwOnError: true);
            MethodInfo parse = argumentsType.GetMethod("TryParse",
                BindingFlags.Public | BindingFlags.Static);
            var arguments = new object[]
            {
                new[] { "-issue100-acceptance", "-issue100-role", role,
                    "-issue100-scenario", "rtt-000-loss-00", "-issue100-run-id",
                    "composition-fixture", "-issue100-output", System.IO.Path.GetTempPath(),
                    "-issue99-match", "composition-match", "-issue86-account",
                    "composition-account" },
                null, null
            };
            Assert.That(parse, Is.Not.Null);
            Assert.That((bool)parse.Invoke(null, arguments), Is.True, arguments[2] as string);
            Type runtimeType = Type.GetType(
                "FPS.Networking.Acceptance.Issue100AcceptanceRuntime, FPS.Networking.Acceptance",
                throwOnError: true);
            MethodInfo request = runtimeType.GetMethod("RequestClientBattleRoute",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(request, Is.Not.Null,
                "验收 BeforeSceneLoad 入口必须在场景 marker 运行前绑定正式客户端模式路由。");
            request.Invoke(null, new[] { arguments[1] });
        }

        private static IEnumerator Settle()
        {
            // HUD creation yields once, then the inventory waits for that HUD
            // before subscribing the quick-slot view. Allow both coroutines.
            for (int frame = 0; frame < 5; frame++) yield return null;
        }

        private static bool HasSubscriber(object publisher, string eventName, Type subscriberType)
        {
            if (publisher == null) return false;
            FieldInfo field = null;
            for (Type type = publisher.GetType(); type != null && field == null;
                 type = type.BaseType)
                field = type.GetField(eventName, BindingFlags.Instance |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            var callbacks = field?.GetValue(publisher) as Delegate;
            return callbacks != null && callbacks.GetInvocationList()
                .Any(callback => callback.Method.DeclaringType == subscriberType);
        }
    }
}
