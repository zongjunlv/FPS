using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode
{
    public class CityNewSmokeTests : InputTestFixture
    {
        private const string ScenePath =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [UnityTest]
        public IEnumerator CityNewBuildSceneLoadsCoreGameplay()
        {
            yield return LoadCityNew();

            Scene activeScene = SceneManager.GetActiveScene();
            Assert.That(activeScene.path, Is.EqualTo(ScenePath));

            List<GameObject> sceneObjects = GetSceneObjects(activeScene);
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerController");

            Assert.That(player, Is.Not.Null, "PlayerController is missing.");
            Assert.That(
                player.GetComponent<CharacterController>(),
                Is.Not.Null,
                "CharacterController is missing from the player.");
            Assert.That(
                player.GetComponent("PlayerInputReader"),
                Is.Not.Null,
                "PlayerInputReader is missing from the player.");
            Assert.That(
                player.GetComponent("PlayerCombatController"),
                Is.Not.Null,
                "PlayerCombatController is missing from the player.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "WeaponController"),
                Is.Not.Null,
                "WeaponController is missing from the scene.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "EnemyController"),
                Is.Not.Null,
                "EnemyController is missing from the scene.");
            Assert.That(
                FindObjectWithComponent(sceneObjects, "ThirdPersonLookController"),
                Is.Null,
                "The obsolete third-person look component must be removed.");

            foreach (GameObject sceneObject in sceneObjects)
            {
                foreach (Component component in sceneObject.GetComponents<Component>())
                {
                    Assert.That(
                        component,
                        Is.Not.Null,
                        $"{GetHierarchyPath(sceneObject)} has a missing script.");
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WeaponHitscanCreatesImpactWithoutProjectile()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");

            Assert.That(weaponObject, Is.Not.Null);
            Assert.That(player, Is.Not.Null);

            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            Assert.That(tryFire, Is.Not.Null, "WeaponController.TryFire is missing.");
            Camera aimCamera = FindChildByName(
                    player.transform,
                    "MainCamera")
                .GetComponent<Camera>();
            Ray aimRay = aimCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            GameObject target =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = aimRay.GetPoint(3f);
            Physics.SyncTransforms();
            Assert.That(
                target.GetComponent<Collider>().Raycast(
                    aimRay,
                    out RaycastHit targetHit,
                    10f),
                Is.True);

            int projectileCountBefore = CountComponentsByName(
                "ProjectileController");
            bool didFire = (bool)tryFire.Invoke(weapon, null);
            int projectileCountAfter = CountComponentsByName(
                "ProjectileController");
            GameObject impact = FindObjectByName("Concrete(Clone)");

            Assert.That(didFire, Is.True, "The equipped weapon did not fire.");
            Assert.That(
                projectileCountAfter,
                Is.EqualTo(projectileCountBefore),
                "Hitscan firing must not instantiate a projectile.");
            Assert.That(impact, Is.Not.Null);
            Assert.That(
                Vector3.Distance(
                    impact.transform.position,
                    targetHit.point),
                Is.LessThan(0.03f),
                "The impact effect must appear on the aimed surface.");
        }

        [UnityTest]
        public IEnumerator HitscanShotCreatesHighSpeedVisualTracer()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            Component weapon =
                weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire =
                weapon.GetType().GetMethod("TryFire");
            Camera aimCamera = FindChildByName(
                    player.transform,
                    "MainCamera")
                .GetComponent<Camera>();
            Ray aimRay = aimCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            GameObject target =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = aimRay.GetPoint(8f);
            Physics.SyncTransforms();

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);

            GameObject tracerObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "ShotTracerController");

            Assert.That(
                tracerObject,
                Is.Not.Null,
                "Hitscan fire must create a visual tracer.");

            Component tracer =
                tracerObject.GetComponent("ShotTracerController");
            PropertyInfo speed =
                tracer.GetType().GetProperty("Speed");
            LineRenderer line =
                tracerObject.GetComponent<LineRenderer>();

            Assert.That((float)speed.GetValue(tracer), Is.GreaterThanOrEqualTo(200f));
            Assert.That(line, Is.Not.Null);
            Assert.That(line.positionCount, Is.EqualTo(2));
            Assert.That(
                CountComponentsByName("ProjectileController"),
                Is.EqualTo(0),
                "The tracer must be visual-only, not a physical projectile.");
        }

        [UnityTest]
        public IEnumerator ImpactMarkRemainsForThreeSeconds()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            Component weapon =
                weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire =
                weapon.GetType().GetMethod("TryFire");
            Camera aimCamera = FindChildByName(
                    player.transform,
                    "MainCamera")
                .GetComponent<Camera>();
            Ray aimRay = aimCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            GameObject target =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.transform.position = aimRay.GetPoint(3f);
            Physics.SyncTransforms();

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            GameObject impact = FindObjectByName("Concrete(Clone)");

            Assert.That(impact, Is.Not.Null);
            yield return new WaitForSeconds(2.8f);
            Assert.That(
                impact,
                Is.Not.Null,
                "The impact mark must remain visible for three seconds.");

            yield return new WaitForSeconds(0.4f);
            Assert.That(
                impact == null,
                Is.True,
                "The impact object must be cleaned up after three seconds.");
        }

        [UnityTest]
        public IEnumerator HitscanDamageCanDestroyEnemyAtCrosshair()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            GameObject enemy = FindObjectWithComponent(
                sceneObjects,
                "EnemyController");
            Component weapon =
                weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire =
                weapon.GetType().GetMethod("TryFire");
            PropertyInfo fireInterval =
                weapon.GetType().GetProperty("FireInterval");
            Camera aimCamera = FindChildByName(
                    player.transform,
                    "MainCamera")
                .GetComponent<Camera>();

            foreach (UnityEngine.AI.NavMeshAgent agent in
                     enemy.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>())
            {
                agent.enabled = false;
            }

            Ray aimRay = aimCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            Collider enemyCollider =
                enemy.GetComponentInChildren<Collider>();
            enemy.transform.position +=
                aimRay.GetPoint(3f) - enemyCollider.bounds.center;
            Physics.SyncTransforms();

            Assert.That(
                enemyCollider.Raycast(
                    aimRay,
                    out _,
                    10f),
                Is.True);

            float shotDelay =
                (float)fireInterval.GetValue(weapon) + 0.01f;

            for (int shot = 0; shot < 10 && enemy != null; shot++)
            {
                Assert.That(
                    (bool)tryFire.Invoke(weapon, null),
                    Is.True);
                yield return new WaitForSeconds(shotDelay);
            }

            Assert.That(
                enemy == null,
                Is.True,
                "Repeated hitscan damage must destroy the aimed enemy.");
            Assert.That(
                CountComponentsByName("ProjectileController"),
                Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator SuccessfulShotConsumesExactlyOneRound()
        {
            yield return LoadCityNew();

            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");

            Assert.That(
                currentAmmo,
                Is.Not.Null,
                "WeaponController.CurrentAmmo is missing.");

            int ammoBefore = (int)currentAmmo.GetValue(weapon);
            bool didFire = (bool)tryFire.Invoke(weapon, null);
            int ammoAfter = (int)currentAmmo.GetValue(weapon);

            Assert.That(didFire, Is.True);
            Assert.That(
                ammoAfter,
                Is.EqualTo(ammoBefore - 1),
                "A successful shot must consume exactly one round.");
        }

        [Test]
        public void RuntimeAmmoStatePartiallyReloadsWithoutChangingCapacity()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");

            Assert.That(
                ammoStateType,
                Is.Not.Null,
                "WeaponAmmoState runtime module is missing.");

            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 30, 5 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo advanceReload =
                ammoStateType.GetMethod("AdvanceReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");
            PropertyInfo magazineCapacity =
                ammoStateType.GetProperty("MagazineCapacity");

            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);
            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);

            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That(
                (bool)advanceReload.Invoke(
                    ammoState,
                    new object[] { 1f, 0.5f }),
                Is.True);

            Assert.That((int)currentAmmo.GetValue(ammoState), Is.EqualTo(30));
            Assert.That((int)reserveAmmo.GetValue(ammoState), Is.EqualTo(3));
            Assert.That(
                (int)magazineCapacity.GetValue(ammoState),
                Is.EqualTo(30),
                "Reloading must not mutate static magazine capacity.");
        }

        [Test]
        public void RuntimeAmmoStateUsesOnlyAvailableReserve()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");
            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 3, 2 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo advanceReload =
                ammoStateType.GetMethod("AdvanceReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");

            Assert.That(
                (bool)tryBeginReload.Invoke(ammoState, null),
                Is.False,
                "A full magazine must not start reloading.");

            for (int index = 0; index < 3; index++)
            {
                Assert.That(
                    (bool)tryConsumeRound.Invoke(ammoState, null),
                    Is.True);
            }

            Assert.That(
                (bool)tryConsumeRound.Invoke(ammoState, null),
                Is.False,
                "An empty magazine cannot consume another round.");
            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That(
                (bool)advanceReload.Invoke(
                    ammoState,
                    new object[] { 1f, 0.5f }),
                Is.True);
            Assert.That((int)currentAmmo.GetValue(ammoState), Is.EqualTo(2));
            Assert.That((int)reserveAmmo.GetValue(ammoState), Is.EqualTo(0));
            Assert.That(
                (bool)tryBeginReload.Invoke(ammoState, null),
                Is.False,
                "Reloading must not start when reserve ammo is empty.");
        }

        [Test]
        public void CancellingReloadDoesNotTransferAmmo()
        {
            System.Type ammoStateType =
                System.Type.GetType("WeaponAmmoState, Assembly-CSharp");
            object ammoState = System.Activator.CreateInstance(
                ammoStateType,
                new object[] { 30, 90 });
            MethodInfo tryConsumeRound =
                ammoStateType.GetMethod("TryConsumeRound");
            MethodInfo tryBeginReload =
                ammoStateType.GetMethod("TryBeginReload");
            MethodInfo cancelReload =
                ammoStateType.GetMethod("CancelReload");
            PropertyInfo currentAmmo =
                ammoStateType.GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                ammoStateType.GetProperty("ReserveAmmo");
            PropertyInfo isReloading =
                ammoStateType.GetProperty("IsReloading");

            Assert.That((bool)tryConsumeRound.Invoke(ammoState, null), Is.True);
            int currentBeforeReload = (int)currentAmmo.GetValue(ammoState);
            int reserveBeforeReload = (int)reserveAmmo.GetValue(ammoState);

            Assert.That((bool)tryBeginReload.Invoke(ammoState, null), Is.True);
            Assert.That((bool)cancelReload.Invoke(ammoState, null), Is.True);

            Assert.That((bool)isReloading.GetValue(ammoState), Is.False);
            Assert.That(
                (int)currentAmmo.GetValue(ammoState),
                Is.EqualTo(currentBeforeReload));
            Assert.That(
                (int)reserveAmmo.GetValue(ammoState),
                Is.EqualTo(reserveBeforeReload));
        }

        [Test]
        public void WeaponSwitchStateStartsCompletesAndInterrupts()
        {
            System.Type switchStateType =
                System.Type.GetType("WeaponSwitchState, Assembly-CSharp");

            Assert.That(
                switchStateType,
                Is.Not.Null,
                "WeaponSwitchState runtime module is missing.");

            object switchState = System.Activator.CreateInstance(
                switchStateType,
                new object[] { 2, 0 });
            MethodInfo tryBeginSwitch =
                switchStateType.GetMethod("TryBeginSwitch");
            MethodInfo advance =
                switchStateType.GetMethod("Advance");
            MethodInfo interrupt =
                switchStateType.GetMethod("Interrupt");
            PropertyInfo currentIndex =
                switchStateType.GetProperty("CurrentIndex");
            PropertyInfo pendingIndex =
                switchStateType.GetProperty("PendingIndex");
            PropertyInfo isSwitching =
                switchStateType.GetProperty("IsSwitching");

            Assert.That(
                (bool)tryBeginSwitch.Invoke(
                    switchState,
                    new object[] { 1 }),
                Is.True);
            Assert.That((bool)isSwitching.GetValue(switchState), Is.True);
            Assert.That((int)pendingIndex.GetValue(switchState), Is.EqualTo(1));
            Assert.That(
                (bool)advance.Invoke(
                    switchState,
                    new object[] { 0.2f, 0.6f }),
                Is.False);

            Assert.That((bool)interrupt.Invoke(switchState, null), Is.True);
            Assert.That((bool)isSwitching.GetValue(switchState), Is.False);
            Assert.That((int)currentIndex.GetValue(switchState), Is.EqualTo(0));

            Assert.That(
                (bool)tryBeginSwitch.Invoke(
                    switchState,
                    new object[] { 1 }),
                Is.True);
            Assert.That(
                (bool)advance.Invoke(
                    switchState,
                    new object[] { 0.6f, 0.6f }),
                Is.True);
            Assert.That((bool)isSwitching.GetValue(switchState), Is.False);
            Assert.That((int)currentIndex.GetValue(switchState), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator NumberKeysAndMouseWheelSwitchWeapons()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                yield return LoadCityNew();

                GameObject player = FindObjectWithComponent(
                    GetSceneObjects(SceneManager.GetActiveScene()),
                    "PlayerCombatController");
                Component combat =
                    player.GetComponent("PlayerCombatController");
                Component inputReader =
                    player.GetComponent("PlayerInputReader");
                PropertyInfo equippedWeaponIndex =
                    combat.GetType().GetProperty("EquippedWeaponIndex");
                PropertyInfo isSwitching =
                    combat.GetType().GetProperty("IsSwitching");
                PropertyInfo weaponSelection =
                    inputReader.GetType().GetProperty("WeaponSelection");

                Assert.That(
                    (int)equippedWeaponIndex.GetValue(combat),
                    Is.EqualTo(0));

                PressAndRelease(keyboard.digit2Key);
                yield return null;

                Assert.That(
                    (int)weaponSelection.GetValue(inputReader),
                    Is.EqualTo(1),
                    "Digit 2 input must remain latched until LateUpdate.");
                yield return null;

                Assert.That(
                    (bool)isSwitching.GetValue(combat),
                    Is.True,
                    "Digit 2 must start switching to the pistol.");

                yield return new WaitForSeconds(0.9f);

                Assert.That(
                    (int)equippedWeaponIndex.GetValue(combat),
                    Is.EqualTo(1));

                Set(mouse.scroll, new Vector2(0f, 120f));
                yield return null;
                Set(mouse.scroll, Vector2.zero);
                yield return null;
                yield return new WaitForSeconds(0.9f);

                Assert.That(
                    (int)equippedWeaponIndex.GetValue(combat),
                    Is.EqualTo(0),
                    "Mouse wheel must cycle back to the AR.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator SemiAutomaticPistolFiresOnceWhileAttackIsHeld()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                yield return LoadCityNew();

                GameObject player = FindObjectWithComponent(
                    GetSceneObjects(SceneManager.GetActiveScene()),
                    "PlayerCombatController");
                Component combat =
                    player.GetComponent("PlayerCombatController");
                PropertyInfo equippedWeapon =
                    combat.GetType().GetProperty("EquippedWeapon");
                MethodInfo trySelectWeapon =
                    combat.GetType().GetMethod("TrySelectWeapon");
                Component ar =
                    (Component)equippedWeapon.GetValue(combat);
                PropertyInfo arDamage =
                    ar.GetType().GetProperty("Damage");
                PropertyInfo arCapacity =
                    ar.GetType().GetProperty("MagazineCapacity");
                PropertyInfo arFireInterval =
                    ar.GetType().GetProperty("FireInterval");
                PropertyInfo arVerticalRecoil =
                    ar.GetType().GetProperty("VerticalRecoil");

                Assert.That(
                    (bool)trySelectWeapon.Invoke(
                        combat,
                        new object[] { 1 }),
                    Is.True);
                yield return new WaitForSeconds(0.9f);

                Component pistol =
                    (Component)equippedWeapon.GetValue(combat);
                PropertyInfo weaponName =
                    pistol.GetType().GetProperty("WeaponName");
                PropertyInfo isAutomatic =
                    pistol.GetType().GetProperty("IsAutomatic");
                PropertyInfo damage =
                    pistol.GetType().GetProperty("Damage");
                PropertyInfo magazineCapacity =
                    pistol.GetType().GetProperty("MagazineCapacity");
                PropertyInfo fireInterval =
                    pistol.GetType().GetProperty("FireInterval");
                PropertyInfo verticalRecoil =
                    pistol.GetType().GetProperty("VerticalRecoil");
                PropertyInfo currentAmmo =
                    pistol.GetType().GetProperty("CurrentAmmo");

                Assert.That(
                    (string)weaponName.GetValue(pistol),
                    Is.EqualTo("Pistol"));
                Assert.That(
                    (bool)isAutomatic.GetValue(pistol),
                    Is.False);
                Assert.That(
                    (float)damage.GetValue(pistol),
                    Is.Not.EqualTo((float)arDamage.GetValue(ar)));
                Assert.That(
                    (int)magazineCapacity.GetValue(pistol),
                    Is.Not.EqualTo((int)arCapacity.GetValue(ar)));
                Assert.That(
                    (float)fireInterval.GetValue(pistol),
                    Is.Not.EqualTo((float)arFireInterval.GetValue(ar)));
                Assert.That(
                    (float)verticalRecoil.GetValue(pistol),
                    Is.Not.EqualTo((float)arVerticalRecoil.GetValue(ar)));

                int ammoBefore = (int)currentAmmo.GetValue(pistol);
                Press(mouse.leftButton);
                yield return new WaitForSeconds(0.7f);
                Release(mouse.leftButton);
                yield return null;

                Assert.That(
                    (int)currentAmmo.GetValue(pistol),
                    Is.EqualTo(ammoBefore - 1),
                    "Holding attack must fire a semi-auto pistol only once.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator WeaponSwitchBlocksCombatAndPreservesEachWeaponAmmo()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                yield return LoadCityNew();

                List<GameObject> sceneObjects =
                    GetSceneObjects(SceneManager.GetActiveScene());
                GameObject player = FindObjectWithComponent(
                    sceneObjects,
                    "PlayerCombatController");
                Component combat =
                    player.GetComponent("PlayerCombatController");
                Component hud = FindObjectWithComponent(
                    sceneObjects,
                    "AmmoHudPresenter").GetComponent("AmmoHudPresenter");
                PropertyInfo equippedWeapon =
                    combat.GetType().GetProperty("EquippedWeapon");
                PropertyInfo equippedWeaponIndex =
                    combat.GetType().GetProperty("EquippedWeaponIndex");
                PropertyInfo isSwitching =
                    combat.GetType().GetProperty("IsSwitching");
                MethodInfo trySelectWeapon =
                    combat.GetType().GetMethod("TrySelectWeapon");
                MethodInfo cancelWeaponSwitch =
                    combat.GetType().GetMethod("CancelWeaponSwitch");
                PropertyInfo hudWeaponName =
                    hud.GetType().GetProperty("WeaponNameText");
                PropertyInfo hudFireMode =
                    hud.GetType().GetProperty("FireModeText");
                PropertyInfo hudAmmo =
                    hud.GetType().GetProperty("DisplayText");

                Component ar =
                    (Component)equippedWeapon.GetValue(combat);
                MethodInfo arTryFire = ar.GetType().GetMethod("TryFire");
                PropertyInfo currentAmmo =
                    ar.GetType().GetProperty("CurrentAmmo");
                PropertyInfo reserveAmmo =
                    ar.GetType().GetProperty("ReserveAmmo");
                PropertyInfo isReloading =
                    ar.GetType().GetProperty("IsReloading");

                Assert.That((bool)arTryFire.Invoke(ar, null), Is.True);
                int arAmmo = (int)currentAmmo.GetValue(ar);
                int arReserve = (int)reserveAmmo.GetValue(ar);

                Assert.That(
                    (bool)trySelectWeapon.Invoke(
                        combat,
                        new object[] { 1 }),
                    Is.True);
                PressAndRelease(keyboard.rKey);
                Press(mouse.leftButton);
                yield return null;
                Release(mouse.leftButton);
                yield return null;

                Assert.That((bool)isSwitching.GetValue(combat), Is.True);
                Assert.That(
                    (int)currentAmmo.GetValue(ar),
                    Is.EqualTo(arAmmo),
                    "Switching must block firing.");
                Assert.That(
                    (bool)isReloading.GetValue(ar),
                    Is.False,
                    "Switching must block reload input.");

                Assert.That(
                    (bool)cancelWeaponSwitch.Invoke(combat, null),
                    Is.True);
                Assert.That((bool)isSwitching.GetValue(combat), Is.False);
                Assert.That(
                    (int)equippedWeaponIndex.GetValue(combat),
                    Is.EqualTo(0),
                    "Interrupted switching must restore the source weapon.");

                Assert.That(
                    (bool)trySelectWeapon.Invoke(
                        combat,
                        new object[] { 1 }),
                    Is.True);
                yield return new WaitForSeconds(0.9f);

                Component pistol =
                    (Component)equippedWeapon.GetValue(combat);
                MethodInfo pistolTryFire =
                    pistol.GetType().GetMethod("TryFire");
                Assert.That(
                    (bool)pistolTryFire.Invoke(pistol, null),
                    Is.True);
                int pistolAmmo = (int)currentAmmo.GetValue(pistol);
                int pistolReserve = (int)reserveAmmo.GetValue(pistol);

                Assert.That(
                    (string)hudWeaponName.GetValue(hud),
                    Is.EqualTo("Pistol"));
                Assert.That(
                    (string)hudFireMode.GetValue(hud),
                    Is.EqualTo("SEMI"));
                Assert.That(
                    (string)hudAmmo.GetValue(hud),
                    Is.EqualTo($"{pistolAmmo} / {pistolReserve}"));

                Assert.That(
                    (bool)trySelectWeapon.Invoke(
                        combat,
                        new object[] { 0 }),
                    Is.True);
                yield return new WaitForSeconds(0.9f);

                Assert.That(
                    (int)currentAmmo.GetValue(ar),
                    Is.EqualTo(arAmmo));
                Assert.That(
                    (int)reserveAmmo.GetValue(ar),
                    Is.EqualTo(arReserve));
                Assert.That(
                    (string)hudWeaponName.GetValue(hud),
                    Is.EqualTo("AR"));
                Assert.That(
                    (string)hudFireMode.GetValue(hud),
                    Is.EqualTo("AUTO"));

                Assert.That(
                    (bool)trySelectWeapon.Invoke(
                        combat,
                        new object[] { 1 }),
                    Is.True);
                yield return new WaitForSeconds(0.9f);

                Assert.That(
                    (int)currentAmmo.GetValue(pistol),
                    Is.EqualTo(pistolAmmo));
                Assert.That(
                    (int)reserveAmmo.GetValue(pistol),
                    Is.EqualTo(pistolReserve));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator WeaponSwitchDoesNotRestartUnholsterAnimationOnCompletion()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            MethodInfo trySelectWeapon =
                combat.GetType().GetMethod("TrySelectWeapon");
            Animator animator = player.GetComponentInChildren<Animator>();
            int holsterLayer = animator.GetLayerIndex("Layer Holster");

            Assert.That(holsterLayer, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                (bool)trySelectWeapon.Invoke(
                    combat,
                    new object[] { 1 }),
                Is.True);

            yield return new WaitForSeconds(0.7f);
            AnimatorStateInfo beforeCompletion =
                animator.GetCurrentAnimatorStateInfo(holsterLayer);

            Assert.That(
                beforeCompletion.IsName("Layer Holster.Unholster"),
                Is.True,
                "The destination weapon should already be unholstering.");

            yield return new WaitForSeconds(0.15f);
            AnimatorStateInfo afterCompletion =
                animator.GetCurrentAnimatorStateInfo(holsterLayer);

            Assert.That(
                afterCompletion.IsName("Layer Holster.Unholster"),
                Is.True);
            Assert.That(
                animator.IsInTransition(holsterLayer),
                Is.False,
                "Completing a switch must not start another holster-layer transition.");
            Assert.That(
                afterCompletion.normalizedTime,
                Is.GreaterThan(beforeCompletion.normalizedTime),
                "Completing a switch must not restart the unholster animation.");
        }

        [UnityTest]
        public IEnumerator WeaponSwitchUsesAnimatorHolsteredParameter()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            Component loadout =
                player.GetComponent("WeaponLoadoutController");
            MethodInfo trySelectWeapon =
                combat.GetType().GetMethod("TrySelectWeapon");
            PropertyInfo switchDuration =
                loadout.GetType().GetProperty("SwitchDuration");
            Animator animator = player.GetComponentInChildren<Animator>();
            int holsterLayer = animator.GetLayerIndex("Layer Holster");

            Assert.That(
                (bool)trySelectWeapon.Invoke(
                    combat,
                    new object[] { 1 }),
                Is.True);

            float presentationSwapTime =
                (float)switchDuration.GetValue(loadout) * 0.5f;
            yield return new WaitForSeconds(
                presentationSwapTime - 0.12f);

            AnimatorStateInfo holsterState =
                animator.GetCurrentAnimatorStateInfo(holsterLayer);
            AnimatorStateInfo nextHolsterState =
                animator.GetNextAnimatorStateInfo(holsterLayer);
            int holsteredParameter =
                Animator.StringToHash("Holstered");

            Assert.That(
                animator.GetBool(holsteredParameter),
                Is.True,
                "The switch must drive the controller's Holstered parameter.");
            Assert.That(
                holsterState.IsName("Layer Holster.Holster") ||
                nextHolsterState.IsName("Layer Holster.Holster"),
                Is.True,
                "The holster layer must naturally transition toward Holster.");
        }

        [UnityTest]
        public IEnumerator BothWeaponsHitscanAtCameraCrosshair()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject player = FindObjectWithComponent(
                sceneObjects,
                "PlayerCombatController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            PropertyInfo equippedWeapon =
                combat.GetType().GetProperty("EquippedWeapon");
            MethodInfo trySelectWeapon =
                combat.GetType().GetMethod("TrySelectWeapon");
            Camera aimCamera = FindChildByName(
                    player.transform,
                    "MainCamera")
                .GetComponent<Camera>();
            Ray crosshairRay = aimCamera.ViewportPointToRay(
                new Vector3(0.5f, 0.5f, 0f));
            GameObject aimTarget =
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            aimTarget.name = "CrosshairAimTarget";
            aimTarget.transform.position = crosshairRay.GetPoint(3f);
            aimTarget.transform.localScale = Vector3.one * 0.5f;
            Collider aimTargetCollider =
                aimTarget.GetComponent<Collider>();
            Physics.SyncTransforms();

            for (int weaponIndex = 0; weaponIndex < 2; weaponIndex++)
            {
                if (weaponIndex > 0)
                {
                    Assert.That(
                        (bool)trySelectWeapon.Invoke(
                            combat,
                            new object[] { weaponIndex }),
                        Is.True);
                    yield return new WaitForSeconds(0.9f);
                }

                Component weapon =
                    (Component)equippedWeapon.GetValue(combat);
                MethodInfo tryFire =
                    weapon.GetType().GetMethod("TryFire");
                Ray currentCrosshairRay =
                    aimCamera.ViewportPointToRay(
                        new Vector3(0.5f, 0.5f, 0f));

                Assert.That(
                    aimTargetCollider.Raycast(
                        currentCrosshairRay,
                        out RaycastHit targetHit,
                        10f),
                    Is.True);

                Assert.That(
                    (bool)tryFire.Invoke(weapon, null),
                    Is.True);

                GameObject impact =
                    FindObjectByName("Concrete(Clone)");

                Assert.That(impact, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        impact.transform.position,
                        targetHit.point),
                    Is.LessThan(0.03f),
                    $"{weapon.gameObject.name} hitscan impact must align " +
                    "with the camera crosshair.");
                Assert.That(
                    CountComponentsByName("ProjectileController"),
                    Is.EqualTo(0));

                Object.Destroy(impact);
                yield return null;
            }

            Object.Destroy(aimTarget);
        }

        [UnityTest]
        public IEnumerator PistolMuzzleFlashRemainsOffUntilShot()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerCombatController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            MethodInfo trySelectWeapon =
                combat.GetType().GetMethod("TrySelectWeapon");
            PropertyInfo equippedWeapon =
                combat.GetType().GetProperty("EquippedWeapon");

            Assert.That(
                (bool)trySelectWeapon.Invoke(
                    combat,
                    new object[] { 1 }),
                Is.True);
            yield return new WaitForSeconds(1.1f);

            Component pistol =
                (Component)equippedWeapon.GetValue(combat);
            ParticleSystem[] particles =
                pistol.GetComponentsInChildren<ParticleSystem>(true);
            Light muzzleLight =
                pistol.GetComponentInChildren<Light>(true);

            Assert.That(particles, Is.Not.Empty);
            Assert.That(
                muzzleLight.enabled,
                Is.False,
                "The pistol muzzle light must be off before firing.");

            foreach (ParticleSystem particle in particles)
            {
                Assert.That(
                    particle.IsAlive(true),
                    Is.False,
                    $"{particle.name} must not play automatically.");
            }

            MethodInfo tryFire = pistol.GetType().GetMethod("TryFire");
            Assert.That((bool)tryFire.Invoke(pistol, null), Is.True);
            Assert.That(muzzleLight.enabled, Is.True);
            Assert.That(
                System.Array.Exists(
                    particles,
                    particle => particle.isPlaying),
                Is.True,
                "Firing must explicitly play the muzzle particles.");
        }

        [UnityTest]
        public IEnumerator PistolHitscanDoesNotCreateProjectile()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerCombatController");
            Component combat =
                player.GetComponent("PlayerCombatController");
            MethodInfo trySelectWeapon =
                combat.GetType().GetMethod("TrySelectWeapon");
            PropertyInfo equippedWeapon =
                combat.GetType().GetProperty("EquippedWeapon");

            Assert.That(
                (bool)trySelectWeapon.Invoke(
                    combat,
                    new object[] { 1 }),
                Is.True);
            yield return new WaitForSeconds(0.9f);

            Component pistol =
                (Component)equippedWeapon.GetValue(combat);
            MethodInfo tryFire = pistol.GetType().GetMethod("TryFire");
            int projectileCountBefore =
                CountComponentsByName("ProjectileController");

            Assert.That((bool)tryFire.Invoke(pistol, null), Is.True);

            Assert.That(
                CountComponentsByName("ProjectileController"),
                Is.EqualTo(projectileCountBefore),
                "Pistol hitscan must resolve without a physical projectile.");
        }

        [UnityTest]
        public IEnumerator EmptyMagazineDoesNotSpawnProjectileAndThrottlesFeedback()
        {
            yield return LoadCityNew();

            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo magazineCapacity =
                weapon.GetType().GetProperty("MagazineCapacity");
            PropertyInfo fireInterval =
                weapon.GetType().GetProperty("FireInterval");
            PropertyInfo dryFireFeedbackCount =
                weapon.GetType().GetProperty("DryFireFeedbackCount");
            int capacity = (int)magazineCapacity.GetValue(weapon);
            float shotDelay = (float)fireInterval.GetValue(weapon) + 0.01f;

            for (int index = 0; index < capacity; index++)
            {
                if (index > 0)
                {
                    yield return new WaitForSeconds(shotDelay);
                }

                Assert.That(
                    (bool)tryFire.Invoke(weapon, null),
                    Is.True,
                    $"Shot {index + 1} should fire.");
            }

            Assert.That((int)currentAmmo.GetValue(weapon), Is.EqualTo(0));
            yield return new WaitForSeconds(shotDelay);

            int projectilesBefore =
                CountComponentsByName("ProjectileController");
            int feedbackBefore =
                (int)dryFireFeedbackCount.GetValue(weapon);

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.False);
            Assert.That(
                CountComponentsByName("ProjectileController"),
                Is.EqualTo(projectilesBefore),
                "Dry firing must not instantiate a damaging projectile.");
            Assert.That(
                (int)dryFireFeedbackCount.GetValue(weapon),
                Is.EqualTo(feedbackBefore + 1));

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.False);
            Assert.That(
                (int)dryFireFeedbackCount.GetValue(weapon),
                Is.EqualTo(feedbackBefore + 1),
                "Automatic dry-fire feedback must be rate limited.");
        }

        [UnityTest]
        public IEnumerator ReloadBlocksFireAndTransfersAmmoOnCompletion()
        {
            yield return LoadCityNew();

            GameObject playerObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerAnimatorController");
            Component playerAnimator =
                playerObject.GetComponent("PlayerAnimatorController");
            PropertyInfo isReloadAnimationPlaying =
                playerAnimator.GetType().GetProperty(
                    "IsReloadAnimationPlaying");
            GameObject weaponObject = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
            MethodInfo tryStartReload =
                weapon.GetType().GetMethod("TryStartReload");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                weapon.GetType().GetProperty("ReserveAmmo");
            PropertyInfo magazineCapacity =
                weapon.GetType().GetProperty("MagazineCapacity");
            PropertyInfo reloadDuration =
                weapon.GetType().GetProperty("ReloadDuration");
            PropertyInfo isReloading =
                weapon.GetType().GetProperty("IsReloading");

            Assert.That(tryStartReload, Is.Not.Null);
            Assert.That(reserveAmmo, Is.Not.Null);
            Assert.That(magazineCapacity, Is.Not.Null);
            Assert.That(reloadDuration, Is.Not.Null);
            Assert.That(isReloading, Is.Not.Null);

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            int ammoAfterShot = (int)currentAmmo.GetValue(weapon);
            int reserveBeforeReload = (int)reserveAmmo.GetValue(weapon);

            Assert.That(
                (bool)tryStartReload.Invoke(weapon, null),
                Is.True);
            Assert.That((bool)isReloading.GetValue(weapon), Is.True);
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.True);
            Assert.That(
                (bool)tryFire.Invoke(weapon, null),
                Is.False,
                "Reloading must block firing.");
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo(ammoAfterShot));

            yield return new WaitForSeconds(
                (float)reloadDuration.GetValue(weapon) + 0.1f);

            Assert.That((bool)isReloading.GetValue(weapon), Is.False);
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo((int)magazineCapacity.GetValue(weapon)));
            Assert.That(
                (int)reserveAmmo.GetValue(weapon),
                Is.EqualTo(reserveBeforeReload - 1));
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.False,
                "Completing a reload must leave the arms animation state.");

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            int currentBeforeCancelledReload =
                (int)currentAmmo.GetValue(weapon);
            int reserveBeforeCancelledReload =
                (int)reserveAmmo.GetValue(weapon);
            MethodInfo cancelReload =
                weapon.GetType().GetMethod("CancelReload");

            Assert.That(
                (bool)tryStartReload.Invoke(weapon, null),
                Is.True);
            Assert.That((bool)cancelReload.Invoke(weapon, null), Is.True);
            Assert.That(
                (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                Is.False,
                "Cancelling a reload must leave the arms animation state.");
            Assert.That(
                (int)currentAmmo.GetValue(weapon),
                Is.EqualTo(currentBeforeCancelledReload));
            Assert.That(
                (int)reserveAmmo.GetValue(weapon),
                Is.EqualTo(reserveBeforeCancelledReload));
        }

        [UnityTest]
        public IEnumerator ReloadInputStartsReloadAndExitsAim()
        {
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            try
            {
                yield return LoadCityNew();

                List<GameObject> sceneObjects =
                    GetSceneObjects(SceneManager.GetActiveScene());
                GameObject player = FindObjectWithComponent(
                    sceneObjects,
                    "PlayerController");
                Component playerController =
                    player.GetComponent("PlayerController");
                Component inputReader =
                    player.GetComponent("PlayerInputReader");
                Component playerAnimator =
                    player.GetComponent("PlayerAnimatorController");
                Component weapon = FindObjectWithComponent(
                    sceneObjects,
                    "WeaponController").GetComponent("WeaponController");
                MethodInfo trySetAiming =
                    playerController.GetType().GetMethod("TrySetAiming");
                MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");
                PropertyInfo isAiming =
                    playerController.GetType().GetProperty("IsAiming");
                PropertyInfo isReloading =
                    weapon.GetType().GetProperty("IsReloading");
                PropertyInfo reloadPressed =
                    inputReader.GetType().GetProperty("ReloadPressed");
                PropertyInfo isReloadAnimationPlaying =
                    playerAnimator.GetType().GetProperty(
                        "IsReloadAnimationPlaying");

                Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
                Assert.That(
                    (bool)trySetAiming.Invoke(
                        playerController,
                        new object[] { true }),
                    Is.True);

                PressAndRelease(keyboard.rKey);
                yield return null;
                Assert.That(
                    (bool)reloadPressed.GetValue(inputReader),
                    Is.True,
                    "Reload input callback must latch R until LateUpdate.");
                yield return null;

                Assert.That(
                    (bool)isReloading.GetValue(weapon),
                    Is.True,
                    "Pressing R must start a valid reload.");
                Assert.That(
                    (bool)isAiming.GetValue(playerController),
                    Is.False,
                    "Starting a reload must exit ADS.");
                Assert.That(
                    isReloadAnimationPlaying,
                    Is.Not.Null,
                    "PlayerAnimatorController must expose reload animation state.");
                Assert.That(
                    (bool)isReloadAnimationPlaying.GetValue(playerAnimator),
                    Is.True,
                    "A successful reload must animate the first-person arms.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }

        [UnityTest]
        public IEnumerator AmmoHudTracksWeaponRuntimeState()
        {
            yield return LoadCityNew();

            List<GameObject> sceneObjects =
                GetSceneObjects(SceneManager.GetActiveScene());
            GameObject weaponObject = FindObjectWithComponent(
                sceneObjects,
                "WeaponController");
            Component weapon = weaponObject.GetComponent("WeaponController");
            GameObject hudObject = FindObjectWithComponent(
                sceneObjects,
                "AmmoHudPresenter");

            Assert.That(
                hudObject,
                Is.Not.Null,
                "AmmoHudPresenter is missing from the player.");

            Component hud = hudObject.GetComponent("AmmoHudPresenter");
            PropertyInfo displayText =
                hud.GetType().GetProperty("DisplayText");
            PropertyInfo currentAmmo =
                weapon.GetType().GetProperty("CurrentAmmo");
            PropertyInfo reserveAmmo =
                weapon.GetType().GetProperty("ReserveAmmo");
            MethodInfo tryFire = weapon.GetType().GetMethod("TryFire");

            string initialText =
                $"{currentAmmo.GetValue(weapon)} / " +
                $"{reserveAmmo.GetValue(weapon)}";
            Assert.That(
                (string)displayText.GetValue(hud),
                Is.EqualTo(initialText));

            Assert.That((bool)tryFire.Invoke(weapon, null), Is.True);
            yield return null;

            string textAfterShot =
                $"{currentAmmo.GetValue(weapon)} / " +
                $"{reserveAmmo.GetValue(weapon)}";
            Assert.That(
                (string)displayText.GetValue(hud),
                Is.EqualTo(textAfterShot));
        }

        [UnityTest]
        public IEnumerator PlayerCrouchLowersCapsuleAndViewThenRestoresThem()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            CharacterController capsule =
                player.GetComponent<CharacterController>();
            Transform cameraPivot = FindChildByName(
                player.transform,
                "CameraPitchPivot");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");
            PropertyInfo isCrouching =
                controller.GetType().GetProperty("IsCrouching");

            Assert.That(
                trySetCrouching,
                Is.Not.Null,
                "PlayerController.TrySetCrouching is missing.");
            Assert.That(
                isCrouching,
                Is.Not.Null,
                "PlayerController.IsCrouching is missing.");
            Assert.That(cameraPivot, Is.Not.Null);

            float standingHeight = capsule.height;
            float standingViewHeight = cameraPivot.localPosition.y;

            Assert.That(standingHeight, Is.EqualTo(1.8f).Within(0.01f));
            Assert.That(capsule.radius, Is.EqualTo(0.4f).Within(0.01f));
            Assert.That(
                capsule.center.y - capsule.height * 0.5f,
                Is.EqualTo(0f).Within(0.01f),
                "The standing capsule must keep its feet at the player root.");
            Assert.That(
                standingViewHeight,
                Is.EqualTo(1.65f).Within(0.01f),
                "The camera must use eye height instead of capsule-top height.");

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { true }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.True);
            Assert.That(capsule.height, Is.EqualTo(1.2f).Within(0.01f));
            Assert.That(capsule.center.y, Is.EqualTo(0.6f).Within(0.01f));
            Assert.That(
                capsule.center.y - capsule.height * 0.5f,
                Is.EqualTo(0f).Within(0.01f),
                "Crouching must not lift or sink the player's feet.");
            Assert.That(
                cameraPivot.localPosition.y,
                Is.EqualTo(1.05f).Within(0.01f));

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.False);
            Assert.That(capsule.height, Is.EqualTo(standingHeight).Within(0.01f));
            Assert.That(
                cameraPivot.localPosition.y,
                Is.EqualTo(standingViewHeight).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator PlayerCannotStandUnderLowCeiling()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");
            PropertyInfo isCrouching =
                controller.GetType().GetProperty("IsCrouching");

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { true }),
                Is.True);
            yield return WaitForStanceTransition();

            GameObject ceiling = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            ceiling.name = "CrouchClearanceTestCeiling";
            ceiling.transform.position =
                player.transform.position + Vector3.up * 1.45f;
            ceiling.transform.localScale = new Vector3(2f, 0.2f, 2f);
            Physics.SyncTransforms();

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.False,
                "Standing must be rejected while headroom is blocked.");
            Assert.That((bool)isCrouching.GetValue(controller), Is.True);

            Object.Destroy(ceiling);
            yield return null;
            Physics.SyncTransforms();

            Assert.That(
                (bool)trySetCrouching.Invoke(controller, new object[] { false }),
                Is.True);
            yield return WaitForStanceTransition();

            Assert.That((bool)isCrouching.GetValue(controller), Is.False);
        }

        [UnityTest]
        public IEnumerator PlayerPauseStopsTimeAndReleasesCursor()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo setPaused =
                controller.GetType().GetMethod("SetPaused");
            PropertyInfo isPaused =
                controller.GetType().GetProperty("IsPaused");

            Assert.That(setPaused, Is.Not.Null);
            Assert.That(
                isPaused,
                Is.Not.Null,
                "PlayerController.IsPaused is missing.");

            try
            {
                setPaused.Invoke(controller, new object[] { true });

                Assert.That((bool)isPaused.GetValue(controller), Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(0f));
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
                Assert.That(Cursor.visible, Is.True);
            }
            finally
            {
                setPaused.Invoke(controller, new object[] { false });
            }

            Assert.That((bool)isPaused.GetValue(controller), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator PlayerLookUsesDeviceSensitivityAndInvertY()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo calculateLookDelta =
                controller.GetType().GetMethod("CalculateLookDelta");
            PropertyInfo invertY =
                controller.GetType().GetProperty("InvertY");

            Assert.That(
                calculateLookDelta,
                Is.Not.Null,
                "PlayerController.CalculateLookDelta is missing.");
            Assert.That(
                invertY,
                Is.Not.Null,
                "PlayerController.InvertY is missing.");

            Vector2 mouseDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 5f), true, 0.5f });
            Vector2 gamepadDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(1f, 0.5f), false, 0.5f });

            Assert.That(mouseDelta.x, Is.EqualTo(1f).Within(0.01f));
            Assert.That(mouseDelta.y, Is.EqualTo(-0.5f).Within(0.01f));
            Assert.That(gamepadDelta.x, Is.EqualTo(90f).Within(0.01f));
            Assert.That(gamepadDelta.y, Is.EqualTo(-45f).Within(0.01f));

            invertY.SetValue(controller, true);
            Vector2 invertedMouseDelta =
                (Vector2)calculateLookDelta.Invoke(
                    controller,
                    new object[] { new Vector2(10f, 5f), true, 0.5f });

            Assert.That(
                invertedMouseDelta.y,
                Is.EqualTo(0.5f).Within(0.01f));
        }

        [UnityTest]
        public IEnumerator PlayerMovementRulesPreventSpeedBoosts()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo calculateVelocity =
                controller.GetType().GetMethod("CalculateHorizontalVelocity");
            MethodInfo trySetCrouching =
                controller.GetType().GetMethod("TrySetCrouching");

            Assert.That(
                calculateVelocity,
                Is.Not.Null,
                "PlayerController.CalculateHorizontalVelocity is missing.");

            Vector3 idleVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.zero, true });
            Vector3 diagonalSprintVelocity =
                (Vector3)calculateVelocity.Invoke(
                    controller,
                    new object[] { Vector2.one, true });
            Vector3 backwardVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.down, true });

            Assert.That(idleVelocity.magnitude, Is.EqualTo(0f).Within(0.01f));
            Assert.That(
                diagonalSprintVelocity.magnitude,
                Is.EqualTo(5f).Within(0.01f),
                "Diagonal movement must not exceed sprint speed.");
            Assert.That(
                backwardVelocity.magnitude,
                Is.EqualTo(2f).Within(0.01f),
                "Backward movement must use walk speed.");

            trySetCrouching.Invoke(controller, new object[] { true });
            Vector3 crouchingVelocity = (Vector3)calculateVelocity.Invoke(
                controller,
                new object[] { Vector2.one, true });

            Assert.That(
                crouchingVelocity.magnitude,
                Is.EqualTo(1.5f).Within(0.01f),
                "Crouching must override sprint input.");
        }

        [UnityTest]
        public IEnumerator PlayerAimTransitionsCameraWeaponCrosshairAndSensitivity()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetAiming =
                controller.GetType().GetMethod("TrySetAiming");
            MethodInfo calculateLookDelta =
                controller.GetType().GetMethod("CalculateLookDelta");
            PropertyInfo isAiming =
                controller.GetType().GetProperty("IsAiming");
            PropertyInfo aimBlend =
                controller.GetType().GetProperty("AimBlend");
            PropertyInfo isAdsCrosshairActive =
                controller.GetType().GetProperty("IsAdsCrosshairActive");
            PropertyInfo aimMode =
                controller.GetType().GetProperty("AimMode");
            Camera playerCamera =
                FindChildByName(player.transform, "MainCamera")
                    .GetComponent<Camera>();
            Transform weaponTransform = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "WeaponController").transform;
            Animator animator = player.GetComponentInChildren<Animator>();

            Assert.That(trySetAiming, Is.Not.Null);
            Assert.That(aimBlend, Is.Not.Null);
            Assert.That(isAdsCrosshairActive, Is.Not.Null);
            Assert.That(aimMode, Is.Not.Null);
            Assert.That(playerCamera, Is.Not.Null);
            Assert.That(weaponTransform, Is.Not.Null);
            Assert.That(animator, Is.Not.Null);
            aimMode.SetValue(
                controller,
                System.Enum.Parse(aimMode.PropertyType, "Toggle"));

            float hipFov = playerCamera.fieldOfView;
            Vector3 hipWeaponPosition = playerCamera.transform
                .InverseTransformPoint(weaponTransform.position);
            Vector2 hipLookDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 0f), true, 0.5f });

            Assert.That(
                (bool)trySetAiming.Invoke(controller, new object[] { true }),
                Is.True);
            Assert.That(
                playerCamera.fieldOfView,
                Is.EqualTo(hipFov).Within(0.01f),
                "Entering ADS must start a transition instead of snapping.");

            yield return new WaitForSeconds(0.1f);

            Assert.That(
                playerCamera.fieldOfView,
                Is.LessThan(hipFov - 0.1f)
                    .And.GreaterThan(hipFov - 14.9f),
                "ADS FOV must be between the hip and target values mid-transition.");

            yield return new WaitForSeconds(0.2f);

            Assert.That((bool)isAiming.GetValue(controller), Is.True);
            Assert.That(
                (float)aimBlend.GetValue(controller),
                Is.GreaterThan(0.95f));
            Assert.That(playerCamera.fieldOfView, Is.LessThan(hipFov - 5f));
            Assert.That(
                Vector3.Distance(
                    playerCamera.transform.InverseTransformPoint(
                        weaponTransform.position),
                    hipWeaponPosition),
                Is.GreaterThan(0.001f));
            Assert.That(
                (bool)isAdsCrosshairActive.GetValue(controller),
                Is.True);
            Assert.That(
                animator.GetFloat("Aiming"),
                Is.GreaterThan(0.8f),
                "Animator Aiming must continue to drive the weapon pose.");

            Vector2 adsLookDelta = (Vector2)calculateLookDelta.Invoke(
                controller,
                new object[] { new Vector2(10f, 0f), true, 0.5f });
            Assert.That(
                adsLookDelta.x,
                Is.LessThan(hipLookDelta.x * 0.75f),
                "ADS must reduce look sensitivity.");

            trySetAiming.Invoke(controller, new object[] { false });
            yield return new WaitForSeconds(0.3f);

            Assert.That((bool)isAiming.GetValue(controller), Is.False);
            Assert.That(playerCamera.fieldOfView, Is.EqualTo(hipFov).Within(0.1f));
            Assert.That(
                (bool)isAdsCrosshairActive.GetValue(controller),
                Is.False);
            Assert.That(animator.GetFloat("Aiming"), Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator SprintCancelsAimAndBlocksReentry()
        {
            yield return LoadCityNew();

            GameObject player = FindObjectWithComponent(
                GetSceneObjects(SceneManager.GetActiveScene()),
                "PlayerController");
            Component controller = player.GetComponent("PlayerController");
            MethodInfo trySetAiming =
                controller.GetType().GetMethod("TrySetAiming");
            PropertyInfo isAiming =
                controller.GetType().GetProperty("IsAiming");
            PropertyInfo isSprinting =
                controller.GetType().GetProperty("IsSprinting");
            PropertyInfo aimMode =
                controller.GetType().GetProperty("AimMode");
            MethodInfo applyAimingState = controller.GetType().GetMethod(
                "ApplyAimingState",
                BindingFlags.Instance | BindingFlags.NonPublic);

            aimMode.SetValue(
                controller,
                System.Enum.Parse(aimMode.PropertyType, "Toggle"));
            Assert.That(applyAimingState, Is.Not.Null);
            Assert.That(
                (bool)trySetAiming.Invoke(controller, new object[] { true }),
                Is.True);
            Assert.That((bool)isAiming.GetValue(controller), Is.True);

            Assert.That(
                (bool)applyAimingState.Invoke(
                    controller,
                    new object[] { true, true }),
                Is.False,
                "ADS cannot start while sprinting.");
            Assert.That(
                (bool)isAiming.GetValue(controller),
                Is.False,
                "Starting a sprint must cancel ADS.");
            Assert.That(
                (bool)isSprinting.GetValue(controller),
                Is.False,
                "The scene starts without sprint input; movement-state " +
                "simulation must not leak into live input.");
        }

        [UnityTest]
        public IEnumerator ToggleAimRespondsToTwoRightMouseClicks()
        {
            Mouse mouse = InputSystem.AddDevice<Mouse>();

            try
            {
                yield return LoadCityNew();

                GameObject player = FindObjectWithComponent(
                    GetSceneObjects(SceneManager.GetActiveScene()),
                    "PlayerController");
                Component controller =
                    player.GetComponent("PlayerController");
                PropertyInfo aimMode =
                    controller.GetType().GetProperty("AimMode");
                PropertyInfo isAiming =
                    controller.GetType().GetProperty("IsAiming");

                aimMode.SetValue(
                    controller,
                    System.Enum.Parse(aimMode.PropertyType, "Toggle"));

                Press(mouse.rightButton);
                yield return null;

                Assert.That(
                    (bool)isAiming.GetValue(controller),
                    Is.True,
                    "The first right click must enter Toggle ADS.");

                Release(mouse.rightButton);
                yield return null;
                Press(mouse.rightButton);
                yield return null;

                Assert.That(
                    (bool)isAiming.GetValue(controller),
                    Is.False,
                    "The second right click must exit Toggle ADS.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
            }
        }

        private static IEnumerator WaitForStanceTransition()
        {
            yield return new WaitForSeconds(0.25f);
        }

        private static IEnumerator LoadCityNew()
        {
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(ScenePath);

            Assert.That(
                buildIndex,
                Is.GreaterThanOrEqualTo(0),
                $"{ScenePath} must be enabled in Build Settings.");

            yield return SceneManager.LoadSceneAsync(
                buildIndex,
                LoadSceneMode.Single);
            yield return null;
        }

        private static int CountComponentsByName(string componentName)
        {
            int count = 0;

            foreach (GameObject sceneObject in
                     GetSceneObjects(SceneManager.GetActiveScene()))
            {
                if (sceneObject.GetComponent(componentName) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static Transform FindChildByName(
            Transform root,
            string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static List<GameObject> GetSceneObjects(Scene scene)
        {
            var results = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    results.Add(child.gameObject);
                }
            }

            return results;
        }

        private static GameObject FindObjectWithComponent(
            IEnumerable<GameObject> sceneObjects,
            string componentName)
        {
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (sceneObject.GetComponent(componentName) != null)
                {
                    return sceneObject;
                }
            }

            return null;
        }

        private static GameObject FindObjectByName(string objectName)
        {
            foreach (GameObject sceneObject in
                     GetSceneObjects(SceneManager.GetActiveScene()))
            {
                if (sceneObject.name == objectName)
                {
                    return sceneObject;
                }
            }

            return null;
        }

        private static string GetHierarchyPath(GameObject gameObject)
        {
            string path = gameObject.name;
            Transform parent = gameObject.transform.parent;

            while (parent != null)
            {
                path = $"{parent.name}/{path}";
                parent = parent.parent;
            }

            return path;
        }
    }
}
