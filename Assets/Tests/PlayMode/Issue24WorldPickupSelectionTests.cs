using System;
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
    public sealed class Issue24WorldPickupSelectionTests
    {
        private const string CityNewScene =
            "Assets/ImportPackages/CSAssets2026/Scenes/CityNew.unity";

        [TearDown]
        public void RestoreRuntimeState()
        {
            Time.timeScale = 1f;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator PickupBindingsAndSelectedCollectionRemainSeparated()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            yield return null;
            Type inputType = RuntimeTypeResolver.GetType(
                "PlayerInputReader");
            Type pickupControllerType = RuntimeTypeResolver.GetType(
                "PlayerWorldPickupController");
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component input = player.GetComponent(inputType);
            InputActionAsset asset = (InputActionAsset)inputType
                .GetProperty("ActionsAsset").GetValue(input);
            InputActionMap playerMap = asset.FindActionMap("Player", true);
            InputAction pickupAction = playerMap.FindAction("Pickup", true);
            InputAction interactAction = playerMap.FindAction(
                "Interact", true);

            Assert.That(HasBinding(pickupAction, "<Keyboard>/f"), Is.True);
            Assert.That(HasBinding(interactAction, "<Keyboard>/e"), Is.True);
            Assert.That(pickupAction.interactions, Is.Empty,
                "F pickup must be a press action, not the terminal Hold action.");

            Component controller = player.GetComponent(pickupControllerType);
            yield return WaitForNearbyPickups(
                pickupControllerType,
                controller,
                2);
            object selected = pickupControllerType
                .GetProperty("SelectedPickup").GetValue(controller);
            Type pickupType = selected.GetType();
            int quantityBefore = (int)pickupType
                .GetProperty("RemainingQuantity").GetValue(selected);
            Assert.That(pickupControllerType
                .GetMethod("TryPickupSelected")
                .Invoke(controller, null), Is.EqualTo(true));
            int remaining = (int)pickupType
                .GetProperty("RemainingQuantity").GetValue(selected);
            Assert.That(remaining, Is.LessThan(quantityBefore));
        }

        [UnityTest]
        public IEnumerator VerticalListShowsNamesAndScrollSelectsWithoutCyclingWeapon()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            yield return null;
            Type pickupControllerType = RuntimeTypeResolver.GetType(
                "PlayerWorldPickupController");
            Type viewType = RuntimeTypeResolver.GetType(
                "WorldPickupListHud");
            Type combatType = RuntimeTypeResolver.GetType(
                "PlayerCombatController");
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component controller = player.GetComponent(pickupControllerType);
            Component combat = player.GetComponent(combatType);
            yield return WaitForNearbyPickups(
                pickupControllerType,
                controller,
                2);
            Component view = (Component)pickupControllerType
                .GetProperty("View").GetValue(controller);
            Assert.That(view, Is.Not.Null);
            Assert.That(viewType.GetProperty("IsVisible").GetValue(view),
                Is.EqualTo(true));
            Assert.That(viewType.GetProperty("VisibleRowCount").GetValue(view),
                Is.GreaterThanOrEqualTo(2));
            Assert.That(viewType.GetProperty("HasOuterPanel").GetValue(view),
                Is.EqualTo(false));
            Assert.That(viewType.GetProperty("HasHeaderOrFooter")
                .GetValue(view), Is.EqualTo(false));
            Assert.That(viewType.GetMethod("GetRowText")
                .Invoke(view, new object[] { 0 }).ToString(),
                Does.Contain("×"));
            object selectedBefore = pickupControllerType
                .GetProperty("SelectedPickup").GetValue(controller);
            int weaponBefore = (int)combatType
                .GetProperty("EquippedWeaponIndex").GetValue(combat);
            Assert.That(pickupControllerType.GetMethod(
                    "ProcessScrollSelectionInput")
                .Invoke(controller, new object[] { -1, 1f }),
                Is.EqualTo(true));

            object selectedAfter = pickupControllerType
                .GetProperty("SelectedPickup").GetValue(controller);
            Assert.That(selectedAfter, Is.Not.SameAs(selectedBefore));
            Assert.That(pickupControllerType.GetMethod(
                    "ProcessScrollSelectionInput")
                .Invoke(controller, new object[] { -1, 1.02f }),
                Is.EqualTo(true),
                "A second discrete wheel event must remain selectable.");
            pickupControllerType.GetMethod("ProcessScrollSelectionInput")
                .Invoke(controller, new object[] { 0, 1.2f });
            Assert.That(pickupControllerType.GetMethod(
                    "ProcessScrollSelectionInput")
                .Invoke(controller, new object[] { -1, 1.21f }),
                Is.EqualTo(true),
                "A new scroll gesture after the idle gap must advance once.");
            Assert.That(combatType.GetProperty("EquippedWeaponIndex")
                .GetValue(combat), Is.EqualTo(weaponBefore),
                "When the pickup list is visible, scroll must not cycle weapons.");

            pickupControllerType.GetMethod("ConfigureRange")
                .Invoke(controller, new object[] { 5f });
            yield return WaitForNearbyPickups(
                pickupControllerType,
                controller,
                4);
            var visited = new HashSet<string>();

            for (int index = 0; index < 4; index++)
            {
                float gestureTime = 2f + index * 0.2f;
                Assert.That(pickupControllerType.GetMethod(
                        "ProcessScrollSelectionInput")
                    .Invoke(
                        controller,
                        new object[] { -1, gestureTime }),
                    Is.EqualTo(true));
                Component selected = (Component)pickupControllerType
                    .GetProperty("SelectedPickup").GetValue(controller);
                visited.Add(selected.GetEntityId().ToString());
                pickupControllerType.GetMethod(
                        "ProcessScrollSelectionInput")
                    .Invoke(
                        controller,
                        new object[] { 0, gestureTime + 0.15f });
            }

            Assert.That(visited.Count, Is.EqualTo(4),
                "Items added to an already visible stack must all become scroll-selectable.");

            Type inputType = RuntimeTypeResolver.GetType(
                "PlayerInputReader");
            Component input = player.GetComponent(inputType);
            MethodInfo clearInput = inputType.GetMethod(
                "ClearBufferedGameplayInput",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo bufferDelta = inputType.GetMethod(
                "BufferWeaponCycleDelta");
            MethodInfo consumeDirection = inputType.GetMethod(
                "ConsumeWeaponCycleDirection");
            clearInput.Invoke(input, null);

            for (int index = 0; index < 4; index++)
            {
                bufferDelta.Invoke(
                    input,
                    new object[] { -0.25f, 10d + index * 0.01d });
                Assert.That(consumeDirection.Invoke(input, null),
                    Is.EqualTo(index == 0 ? -1 : 0));
            }

            bufferDelta.Invoke(input, new object[] { -0.25f, 10.2d });
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(-1),
                "A new wheel gesture after a clear input gap must produce the next step.");

            clearInput.Invoke(input, null);
            bufferDelta.Invoke(input, new object[] { -0.5f, 11d });
            bufferDelta.Invoke(input, new object[] { -0.5f, 11d });
            Assert.That(consumeDirection.Invoke(input, null),
                Is.EqualTo(-1),
                "Duplicate callbacks from the same event must still produce only one step.");
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(0));
            bufferDelta.Invoke(input, new object[] { -0.5f, 11.01d });
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(0));
            bufferDelta.Invoke(input, new object[] { 0.5f, 11.02d });
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(1),
                "Reversing scroll direction must start a new gesture immediately.");

            clearInput.Invoke(input, null);
            float[] capturedGesture =
            {
                0.35f, 0.35f, 0.35f, 0.35f, 0.30f, 0.35f,
                0.35f, 0.30f, 0.25f, 0.45f, 0.20f, 0.15f,
                0.10f, 0.05f
            };
            double capturedTime = 12d;

            for (int index = 0; index < capturedGesture.Length; index++)
            {
                bufferDelta.Invoke(
                    input,
                    new object[] { capturedGesture[index], capturedTime });
                Assert.That(consumeDirection.Invoke(input, null),
                    Is.EqualTo(index == 0 ? 1 : 0),
                    "A captured momentum tail must remain one gesture.");
                capturedTime += 0.018d;
            }

            capturedTime += 0.05d;
            bufferDelta.Invoke(input, new object[] { 0.35f, capturedTime });
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(0));
            bufferDelta.Invoke(
                input,
                new object[] { 0.35f, capturedTime + 0.018d });
            Assert.That(consumeDirection.Invoke(input, null), Is.EqualTo(1),
                "A renewed same-direction impulse must start the next gesture even before the idle timeout.");
        }

        [UnityTest]
        public IEnumerator WallOcclusionRemovesPickupFromReachableCandidates()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return SceneManager.LoadSceneAsync(
                CityNewScene,
                LoadSceneMode.Single);
            yield return null;
            yield return null;
            Type controllerType = RuntimeTypeResolver.GetType(
                "PlayerWorldPickupController");
            Type pickupType = RuntimeTypeResolver.GetType(
                "WorldItemPickup");
            Type definitionType = RuntimeTypeResolver.GetType(
                "ItemDefinition");
            Type playerControllerType = RuntimeTypeResolver.GetType(
                "PlayerController");
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            Component controller = player.GetComponent(controllerType);
            Component playerController =
                player.GetComponent(playerControllerType);
            Camera camera = (Camera)playerControllerType
                .GetProperty("AimCamera").GetValue(playerController);
            ScriptableObject definition = CreateDefinition(definitionType);
            GameObject pickupObject = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            GameObject wall = null;

            try
            {
                pickupObject.name = "Occluded Test Pickup";
                pickupObject.transform.localScale = Vector3.one * 0.3f;
                pickupObject.transform.position =
                    camera.transform.position +
                    camera.transform.forward.normalized * 2f;
                Component pickup = pickupObject.AddComponent(pickupType);
                pickupType.GetMethod("Configure").Invoke(
                    pickup,
                    new object[] { definition, 1 });
                Physics.SyncTransforms();
                Assert.That(controllerType.GetMethod("IsPickupReachable")
                    .Invoke(controller, new[] { pickup }), Is.EqualTo(true));

                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Pickup Occlusion Wall";
                Vector3 horizontalForward = Vector3.ProjectOnPlane(
                    camera.transform.forward,
                    Vector3.up).normalized;
                wall.transform.position = Vector3.Lerp(
                    camera.transform.position,
                    pickupObject.transform.position,
                    0.5f);
                wall.transform.rotation = Quaternion.LookRotation(
                    horizontalForward.sqrMagnitude > 0.01f
                        ? horizontalForward
                        : Vector3.forward);
                wall.transform.localScale = new Vector3(1.5f, 2.5f, 0.18f);
                Physics.SyncTransforms();
                Assert.That(controllerType.GetMethod("IsPickupReachable")
                    .Invoke(controller, new[] { pickup }), Is.EqualTo(false),
                    "A nearby pickup behind a wall must not be listed or collected.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(pickupObject);
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        private static IEnumerator WaitForNearbyPickups(
            Type controllerType,
            Component controller,
            int minimumCount)
        {
            float deadline = Time.realtimeSinceStartup + 20f;

            while (Time.realtimeSinceStartup < deadline &&
                   (controller == null ||
                    (int)controllerType.GetProperty("NearbyPickupCount")
                        .GetValue(controller) < minimumCount))
            {
                yield return null;
            }

            Assert.That(controller, Is.Not.Null);
            Assert.That(controllerType.GetProperty("NearbyPickupCount")
                .GetValue(controller), Is.GreaterThanOrEqualTo(minimumCount));
        }

        private static bool HasBinding(
            InputAction action,
            string expectedPath)
        {
            for (int index = 0; index < action.bindings.Count; index++)
            {
                string path = action.bindings[index].path;

                if (string.Equals(
                        path,
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static ScriptableObject CreateDefinition(Type definitionType)
        {
            Type itemType = RuntimeTypeResolver.GetType("ItemType");
            Type effectType = RuntimeTypeResolver.GetType(
                "ItemEffectType");
            ScriptableObject definition = ScriptableObject.CreateInstance(
                definitionType);
            definitionType.GetMethod("Configure").Invoke(
                definition,
                new object[]
                {
                    "occluded_test_item",
                    "遮挡测试物品",
                    "测试墙体遮挡。",
                    null,
                    Enum.Parse(itemType, "Consumable"),
                    2,
                    Enum.Parse(effectType, "RestoreHealth"),
                    10f
                });
            return definition;
        }

    }
}
