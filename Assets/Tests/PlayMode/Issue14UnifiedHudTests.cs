using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public sealed class Issue14UnifiedHudTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        [Test]
        public void MultipleLocksKeepGameplayLockedUntilLastRelease()
        {
            Type stateType = RuntimeTypeResolver.GetType("GameplayLockState");
            Type reasonType = RuntimeTypeResolver.GetType("GameplayLockReason");
            Assert.That(stateType, Is.Not.Null);
            Assert.That(reasonType, Is.Not.Null);

            object state = Activator.CreateInstance(stateType);
            object pause = Enum.Parse(reasonType, "PauseMenu");
            object inventory = Enum.Parse(reasonType, "Inventory");
            IDisposable pauseLock = (IDisposable)stateType.GetMethod("Acquire")
                .Invoke(state, new[] { pause });
            IDisposable inventoryLock = (IDisposable)stateType.GetMethod("Acquire")
                .Invoke(state, new[] { inventory });

            Assert.That(stateType.GetProperty("ActiveLockCount").GetValue(state), Is.EqualTo(2));
            pauseLock.Dispose();
            Assert.That(stateType.GetProperty("IsLocked").GetValue(state), Is.EqualTo(true),
                "释放暂停锁不得解除仍在生效的背包锁。");
            inventoryLock.Dispose();
            inventoryLock.Dispose();
            Assert.That(stateType.GetProperty("IsLocked").GetValue(state), Is.EqualTo(false));
            Assert.That(stateType.GetProperty("ActiveLockCount").GetValue(state), Is.EqualTo(0),
                "锁句柄重复释放必须幂等。");
        }

        [TestCase(1920, 1080)]
        [TestCase(1920, 1200)]
        [TestCase(3840, 2160)]
        public void FullSafeAreaProducesFullScreenAnchors(int width, int height)
        {
            Type fitterType = RuntimeTypeResolver.GetType("SafeAreaFitter");
            object anchors = fitterType.GetMethod("Calculate")?.Invoke(
                null, new object[] { new Rect(0f, 0f, width, height), width, height });

            Assert.That(anchors, Is.Not.Null);
            Type anchorType = anchors.GetType();
            Assert.That((Vector2)anchorType.GetProperty("Minimum").GetValue(anchors), Is.EqualTo(Vector2.zero));
            Assert.That((Vector2)anchorType.GetProperty("Maximum").GetValue(anchors), Is.EqualTo(Vector2.one));
        }

        [Test]
        public void SafeAreaInsetsAreNormalizedAndClamped()
        {
            Type fitterType = RuntimeTypeResolver.GetType("SafeAreaFitter");
            object anchors = fitterType.GetMethod("Calculate")?.Invoke(
                null, new object[] { new Rect(100f, 50f, 1720f, 980f), 1920, 1080 });
            Type anchorType = anchors.GetType();
            Vector2 minimum = (Vector2)anchorType.GetProperty("Minimum").GetValue(anchors);
            Vector2 maximum = (Vector2)anchorType.GetProperty("Maximum").GetValue(anchors);

            Assert.That(minimum.x, Is.EqualTo(100f / 1920f).Within(0.0001f));
            Assert.That(minimum.y, Is.EqualTo(50f / 1080f).Within(0.0001f));
            Assert.That(maximum.x, Is.EqualTo(1820f / 1920f).Within(0.0001f));
            Assert.That(maximum.y, Is.EqualTo(1030f / 1080f).Within(0.0001f));
        }

        [Test]
        public void MissingHudIconUsesRuntimePlaceholder()
        {
            Type catalogType = RuntimeTypeResolver.GetType("HudIconCatalog");
            Type iconType = RuntimeTypeResolver.GetType("HudIconId");
            object catalog = Activator.CreateInstance(catalogType);
            object unknownIcon = Enum.ToObject(iconType, 999);

            Assert.That(catalogType.GetMethod("UsesPlaceholder")
                .Invoke(catalog, new[] { unknownIcon }), Is.EqualTo(true));
            Assert.That(catalogType.GetMethod("Get")
                .Invoke(catalog, new[] { unknownIcon }), Is.Not.Null,
                "缺失或未授权图标必须回退到可见占位图。");
        }

        [Test]
        public void WeaponIconsUseTrimmedVisibleBounds()
        {
            Type catalogType = RuntimeTypeResolver.GetType("HudIconCatalog");
            Type iconType = RuntimeTypeResolver.GetType("HudIconId");
            object catalog = Activator.CreateInstance(catalogType);
            Sprite rifle = (Sprite)catalogType.GetMethod("Get").Invoke(
                catalog,
                new[] { Enum.Parse(iconType, "Rifle") });
            Sprite handgun = (Sprite)catalogType.GetMethod("Get").Invoke(
                catalog,
                new[] { Enum.Parse(iconType, "Handgun") });

            Assert.That(rifle.rect.width, Is.LessThan(rifle.texture.width * 0.6f));
            Assert.That(handgun.rect.width, Is.LessThan(handgun.texture.width * 0.3f));
            Assert.That(rifle.rect.height, Is.LessThan(rifle.texture.height * 0.6f));
            Assert.That(handgun.rect.height, Is.LessThan(handgun.texture.height * 0.6f));
        }

        [UnityTest]
        public IEnumerator CityNewBuildsOneBoundUnifiedHud()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene, LoadSceneMode.Single);

            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
            }

            Type hudType = RuntimeTypeResolver.GetType("UnifiedGameHud");
            Assert.That(hudType, Is.Not.Null);
            UnityEngine.Object[] huds = UnityEngine.Object.FindObjectsByType(
                hudType, FindObjectsInactive.Include);
            Assert.That(huds, Has.Length.EqualTo(1),
                "CityNew 只能生成一个统一 HUD 根节点。");

            Component hud = (Component)huds[0];
            Assert.That(hud.gameObject.name, Is.EqualTo("GameUICanvas"));
            Assert.That(hudType.GetProperty("IsBound").GetValue(hud), Is.EqualTo(true));
            Assert.That(hudType.GetProperty("LegacyPresentationsDisabled").GetValue(hud), Is.EqualTo(true));
            Assert.That(hud.transform.Find("SafeArea/HUDLayer"), Is.Not.Null);
            Assert.That(hud.transform.Find("ModalLayer"), Is.Not.Null);
            Assert.That(hud.transform.Find("OverlayLayer"), Is.Not.Null);
            Transform weaponHud = hud.transform.Find(
                "SafeArea/HUDLayer/WeaponHud");
            Assert.That(weaponHud.Find("AmmoIcon"), Is.Null,
                "弹药图标压缩后会形成下划线/竖线，不应继续显示。");
            Assert.That(weaponHud.Find("AmmoDivider"), Is.Null,
                "弹药数字附近不应存在造成下划线观感的装饰线。");
            Transform currentAmmo = weaponHud.Find("CurrentAmmo");
            Transform reserveAmmo = weaponHud.Find("ReserveAmmo");
            Assert.That(currentAmmo, Is.Not.Null);
            Assert.That(reserveAmmo, Is.Not.Null);
            Type uiTextType = RuntimeTypeResolver.GetType(
                "UnityEngine.UI.Text, UnityEngine.UI");
            Assert.That(currentAmmo.GetComponent(uiTextType), Is.Not.Null,
                "弹药数字应使用不支持下划线装饰的 UGUI Text。");
            Assert.That(reserveAmmo.GetComponent(uiTextType), Is.Not.Null);

            Type scalerType = RuntimeTypeResolver.GetType(
                "UnityEngine.UI.CanvasScaler, UnityEngine.UI");
            Component scaler = hud.GetComponent(scalerType);
            Vector2 referenceResolution = (Vector2)scalerType
                .GetProperty("referenceResolution").GetValue(scaler);
            Assert.That(referenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));

            Type eventSystemType = RuntimeTypeResolver.GetType(
                "UnityEngine.EventSystems.EventSystem, UnityEngine.UI");
            Assert.That(UnityEngine.Object.FindAnyObjectByType(eventSystemType), Is.Not.Null);

            FieldInfo healthField = hudType.GetField(
                "health", BindingFlags.Instance | BindingFlags.NonPublic);
            object health = healthField.GetValue(hud);
            Type healthType = health.GetType();
            Type damageInfoType = RuntimeTypeResolver.GetType("DamageInfo");
            int refreshBefore = (int)hudType.GetProperty("VitalsRefreshCount")
                .GetValue(hud);
            object damage = Activator.CreateInstance(
                damageInfoType,
                new object[] { 25f, Vector3.zero, Vector3.back, null });
            healthType.GetMethod("ApplyDamage").Invoke(health, new[] { damage });
            yield return null;

            Assert.That(hudType.GetProperty("ArmorText").GetValue(hud).ToString(),
                Does.Contain("075"), "护甲变化事件应直接刷新 UGUI 状态栏。");
            Component armorFill = (Component)hudType.GetField(
                "armorFill", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(hud);
            Component healthFill = (Component)hudType.GetField(
                "healthFill", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(hud);
            Assert.That(((RectTransform)armorFill.transform).sizeDelta.x,
                Is.EqualTo(217.5f).Within(0.1f),
                "护甲降至 75% 时即时护甲条宽度也必须降至 75%。");
            Assert.That(((RectTransform)healthFill.transform).sizeDelta.x,
                Is.EqualTo(290f).Within(0.1f));

            object secondDamage = Activator.CreateInstance(
                damageInfoType,
                new object[] { 100f, Vector3.zero, Vector3.back, null });
            healthType.GetMethod("ApplyDamage").Invoke(
                health,
                new[] { secondDamage });
            yield return null;

            Assert.That(((RectTransform)armorFill.transform).sizeDelta.x,
                Is.Zero.Within(0.1f));
            Assert.That(((RectTransform)healthFill.transform).sizeDelta.x,
                Is.EqualTo(217.5f).Within(0.1f),
                "护甲耗尽后生命降至 75%，生命条宽度必须同步降至 75%。");
            Assert.That((int)hudType.GetProperty("VitalsRefreshCount").GetValue(hud),
                Is.GreaterThan(refreshBefore));
        }

        [UnityTest]
        public IEnumerator NestedRuntimeLocksRestoreOnlyAfterLastRelease()
        {
            yield return SceneManager.LoadSceneAsync(
                CityNewScene, LoadSceneMode.Single);
            yield return null;
            yield return null;

            Type coordinatorType = RuntimeTypeResolver.GetType(
                "GameplayLockCoordinator");
            Type reasonType = RuntimeTypeResolver.GetType(
                "GameplayLockReason");
            Component coordinator = (Component)UnityEngine.Object
                .FindAnyObjectByType(coordinatorType);
            Assert.That(coordinator, Is.Not.Null);

            IDisposable pauseLock = (IDisposable)coordinatorType
                .GetMethod("Acquire").Invoke(coordinator, new[]
                {
                    Enum.Parse(reasonType, "PauseMenu")
                });
            IDisposable inventoryLock = (IDisposable)coordinatorType
                .GetMethod("Acquire").Invoke(coordinator, new[]
                {
                    Enum.Parse(reasonType, "Inventory")
                });
            Type playerType = RuntimeTypeResolver.GetType("PlayerController");
            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            Component player = coordinator.GetComponent(playerType);
            Component combat = coordinator.GetComponent(combatType);

            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(playerType.GetProperty("GameplayInputEnabled").GetValue(player), Is.False);
            Assert.That(combatType.GetProperty("GameplayInputEnabled").GetValue(combat), Is.False);

            pauseLock.Dispose();
            Assert.That(Time.timeScale, Is.Zero,
                "背包锁仍存在时，释放暂停锁不得恢复游戏。");
            Assert.That(coordinatorType.GetProperty("IsLocked").GetValue(coordinator), Is.True);

            inventoryLock.Dispose();
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(playerType.GetProperty("GameplayInputEnabled").GetValue(player), Is.True);
            Assert.That(combatType.GetProperty("GameplayInputEnabled").GetValue(combat), Is.True);
        }
    }
}
