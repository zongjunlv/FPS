using System.Collections;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue94NetworkPresentationTests
    {
        private GameObject authorityObject;
        private GameObject replicaObject;
        private GameObject appearanceObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (appearanceObject != null) Object.Destroy(appearanceObject);
            if (replicaObject != null) Object.Destroy(replicaObject);
            if (authorityObject != null) Object.Destroy(authorityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SnapshotSwitchesRemoteWeaponWithoutLeavingOldVisual()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetworkPlayerReplica replica = CreateReplica(authority);
            Transform visualRoot = new GameObject(
                "ThirdPersonVisualRoot").transform;
            visualRoot.SetParent(replicaObject.transform, false);
            NetworkThirdPersonAnimator animation =
                replicaObject.AddComponent<NetworkThirdPersonAnimator>();
            NetworkPlayerAppearancePresenter presenter =
                replicaObject.AddComponent<NetworkPlayerAppearancePresenter>();
            ThirdPersonWeaponCatalog catalog = Resources.Load<
                ThirdPersonWeaponCatalog>(
                ThirdPersonWeaponCatalog.ResourcesPath);
            presenter.Configure(visualRoot, catalog,
                NetworkPresentationIds.Rifle, true);
            presenter.RefreshRepresentation();
            GameObject originalWeapon = presenter.ThirdPersonWeapon;

            Assert.That(authority.TryApplyPresentationCommand(10,
                new NetcodePresentationCommand
                {
                    PlayerId = 1,
                    Sequence = 1,
                    Action = NetworkPresentationAction.SwitchWeapon,
                    WeaponId = NetworkPresentationIds.Handgun
                }), Is.True);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState state), Is.True);
            replica.ConsumeServerState(state, false, state.ServerTick);
            yield return null;

            Assert.That(replica.WeaponId,
                Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(presenter.CurrentWeaponDefinition.StableId,
                Is.EqualTo(NetworkPresentationIds.Handgun));
            Assert.That(presenter.ThirdPersonWeapon,
                Is.Not.SameAs(originalWeapon));
            Assert.That(visualRoot.GetComponentsInChildren<
                    ThirdPersonWeaponRig>(true),
                Has.Exactly(1).Matches<ThirdPersonWeaponRig>(value =>
                    value.gameObject.activeSelf));
            Assert.That(animation.BoundAnimator, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator SequencedActionTriggersAnimatorExactlyOnce()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            authority.RegisterPlayerClient(10, 1);
            NetcodePlayerCommand shot = NetcodePlayerCommand.FromDomain(
                new PlayerInputCommand(
                    1, 1, 1001, 1, 0d, 0d, 0d, 0d,
                    fire: true, claimedPosition: default));
            Assert.That(authority.TryQueueCommand(10, shot, true), Is.True);
            Assert.That(authority.ServerStep().Commands[0].Accepted, Is.True);
            NetworkPlayerReplica replica = CreateReplica(authority);
            NetworkThirdPersonAnimator driver =
                replicaObject.AddComponent<NetworkThirdPersonAnimator>();
            PlayerAppearanceCatalog catalog = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            appearanceObject = Object.Instantiate(
                catalog.DefaultDefinition.VisualPrefab);
            driver.BindAnimator(appearanceObject.GetComponent<Animator>());
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState baseline), Is.True);
            replica.ConsumeServerState(baseline, false, baseline.ServerTick);

            Assert.That(authority.TryApplyPresentationCommand(10,
                new NetcodePresentationCommand
                {
                    PlayerId = 1,
                    Sequence = 1,
                    Action = NetworkPresentationAction.Reload,
                    WeaponId = NetworkPresentationIds.Rifle
                }), Is.True);
            Assert.That(authority.TryGetPlayerState(1,
                out NetcodePlayerState current), Is.True);
            replica.ConsumeServerState(current, false, current.ServerTick);
            replica.ConsumeServerState(current, false, current.ServerTick);
            yield return null;

            Assert.That(driver.CombatActionCount, Is.EqualTo(1));
            Assert.That(driver.LastCombatAction,
                Is.EqualTo(ThirdPersonCombatAction.Reload));
        }

        [UnityTest]
        public IEnumerator SnapshotAppliesAimSprintCrouchAndGroundedTogether()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(authority);
            var state = new NetcodePlayerState
            {
                ServerTick = 10,
                PlayerId = 1,
                Position = Vector3.zero,
                Health = 100f,
                Velocity = Vector3.forward * 6f,
                Stance = (byte)PlayerStance.Crouching,
                Grounded = false,
                Sprinting = true,
                Aiming = true,
                AppearanceId = "character.quaternius.female-dark",
                WeaponId = NetworkPresentationIds.Handgun
            };

            replica.ConsumeServerState(state, false, 10d);
            yield return null;

            Assert.That(replica.PresentedCrouching, Is.True);
            Assert.That(replica.PresentedGrounded, Is.False);
            Assert.That(replica.PresentedSprinting, Is.True);
            Assert.That(replica.PresentedAiming, Is.True);
            Assert.That(replica.AppearanceId,
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(replica.WeaponId,
                Is.EqualTo(NetworkPresentationIds.Handgun));
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            authorityObject = new GameObject("Issue94 Authority");
            NetworkCoopSessionAuthority authority =
                authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(
                    tickRate: 60,
                    maximumPastCommandTicks: 16,
                    historyCapacity: 32),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 20d), 1d, 100d)
                });
            return authority;
        }

        private NetworkPlayerReplica CreateReplica(
            NetworkCoopSessionAuthority authority)
        {
            replicaObject = new GameObject("Issue94 Replica");
            NetworkPlayerReplica replica =
                replicaObject.AddComponent<NetworkPlayerReplica>();
            replica.Bind(authority, 1);
            return replica;
        }
    }
}
