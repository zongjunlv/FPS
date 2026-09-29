using System.Collections;
using System.Collections.Generic;
using FPS.Core.GameModes;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class CoopShotSpreadParityTests
    {
        private GameObject rigObject;
        private GameObject cameraObject;
        private readonly HashSet<UnifiedGameHud> oldHuds = new();
        private readonly HashSet<EventSystem> oldSystems = new();

        [SetUp]
        public void Setup()
        {
            GameModeContext.ResetForTests();
            GameModeContext.BeginTransition(GameModeId.Coop, GameModeStage.CoopBattle);
            foreach (var hud in Object.FindObjectsByType<UnifiedGameHud>(FindObjectsInactive.Include, FindObjectsSortMode.None)) oldHuds.Add(hud);
            foreach (var system in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None)) oldSystems.Add(system);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (rigObject != null) Object.Destroy(rigObject);
            if (cameraObject != null) Object.Destroy(cameraObject);
            foreach (var hud in Object.FindObjectsByType<UnifiedGameHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!oldHuds.Contains(hud)) Object.Destroy(hud.gameObject);
            foreach (var system in Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!oldSystems.Contains(system)) Object.Destroy(system.gameObject);
            GameModeContext.ResetForTests();
            oldHuds.Clear();
            oldSystems.Clear();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator NetworkShotUsesPreShotSpreadAndRejectedFeedbackCreatesNoEffects()
        {
            PlayerGameplayRig rig = PlayerGameplayRig.Create(new Vector3(0, 50, 0), Quaternion.identity);
            rigObject = rig.gameObject;
            Assert.That(rig.CompositionRoot.TryInitialize(), Is.True);
            WeaponController weapon = rig.Combat.EquippedWeapon;
            cameraObject = new GameObject("Spread parity camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(weapon.MuzzleTransform.position, weapon.MuzzleTransform.rotation);
            weapon.ConfigureAiming(camera, rig.transform, null, null, null);
            weapon.SetFiringContext(1f, 0f, false);
            weapon.SetSpreadSampleOverride(Vector2.right);
            float before = weapon.CurrentSpreadDegrees;
            Assert.That(weapon.TryPredictNetworkFire(), Is.True);
            Ray ray = weapon.CreateNetworkShotRay();
            Assert.That(Vector3.Angle(camera.transform.forward, ray.direction), Is.EqualTo(before).Within(0.01f),
                "网络首发必须像单机一样先采样本发散布，再累积下一发的 bloom。");
            Assert.That(weapon.CurrentSpreadDegrees, Is.GreaterThan(before));

            var feedback = rig.gameObject.AddComponent<NetworkCombatFeedbackPresenter>();
            feedback.Present(new NetcodeShotFeedbackEvent { RejectionReason = CommandRejectionReason.FireRateExceeded, Sequence = 1 });
            Assert.That(feedback.RejectedShotCount, Is.EqualTo(1));
            Assert.That(feedback.PresentedShotCount, Is.Zero);
            Assert.That(feedback.PresentedHitCount, Is.Zero);
            yield return null;
        }
    }
}
