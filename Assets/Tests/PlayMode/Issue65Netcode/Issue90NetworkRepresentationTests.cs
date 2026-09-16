using System.Collections;
using System.Linq;
using FPS.Networking.Domain;
using FPS.Networking.Netcode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue90NetworkRepresentationTests
    {
        private GameObject authorityObject;
        private GameObject replicaObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (replicaObject != null) Object.Destroy(replicaObject);
            if (authorityObject != null) Object.Destroy(authorityObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemoteReplicaBuildsSelectedHumanoidAndWeapon()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(
                authority,
                "character.quaternius.female-dark",
                owner: false,
                out NetworkPlayerAppearancePresenter presenter,
                out NetworkVerticalSliceInputDriver driver);

            presenter.RefreshRepresentation();
            yield return null;

            Assert.That(replica.AppearanceId,
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(presenter.CurrentAppearance, Is.Not.Null);
            Assert.That(presenter.CurrentAppearance
                    .GetComponent<PlayerAppearanceInstance>().StableId,
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(presenter.CurrentAppearance
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true),
                Is.Not.Empty);
            Assert.That(presenter.ThirdPersonWeapon, Is.Not.Null);
            Transform rightHand = presenter.CurrentAppearance
                .GetComponent<Animator>()
                .GetBoneTransform(HumanBodyBones.RightHand);
            Assert.That(presenter.WeaponSocket.parent, Is.SameAs(rightHand));
            Assert.That(presenter.ThirdPersonWeapon.transform.parent,
                Is.SameAs(presenter.WeaponSocket));
            Assert.That(presenter.ThirdPersonWeapon.transform.localPosition,
                Is.EqualTo(Vector3.zero));
            Assert.That(presenter.ThirdPersonWeapon.transform.localRotation,
                Is.EqualTo(Quaternion.identity));
            Assert.That(presenter.VisualRoot
                    .GetComponentsInChildren<Renderer>(true),
                Has.All.Matches<Renderer>(renderer => renderer.enabled &&
                    renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly));
            Assert.That(driver.enabled, Is.False,
                "远端副本不得启用本地输入驱动。");
        }

        [UnityTest]
        public IEnumerator OwnerKeepsFirstPersonRigAndHidesThirdPersonMeshes()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(
                authority,
                "character.quaternius.male-dark",
                owner: true,
                out NetworkPlayerAppearancePresenter presenter,
                out NetworkVerticalSliceInputDriver driver);

            presenter.RefreshRepresentation();
            yield return null;

            Renderer[] renderers = presenter.VisualRoot
                .GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            Assert.That(renderers,
                Has.All.Matches<Renderer>(renderer => renderer.enabled &&
                    renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly));
            Assert.That(driver.enabled, Is.True);
            PlayerGameplayRig firstPersonRig = PlayerGameplayRig.LoadPrefab();
            Assert.That(firstPersonRig, Is.Not.Null);
            Assert.That(firstPersonRig.FirstPersonCamera, Is.Not.Null);
            Assert.That(firstPersonRig.Weapons.Count, Is.EqualTo(2));
            Assert.That(replica.IsLocallyControlled, Is.True);
        }

        [UnityTest]
        public IEnumerator AppearanceSwitchAndDeathNeverLeaveDoubleVisuals()
        {
            NetworkCoopSessionAuthority authority = CreateAuthority();
            NetworkPlayerReplica replica = CreateReplica(
                authority,
                "character.quaternius.male-light",
                owner: false,
                out NetworkPlayerAppearancePresenter presenter,
                out _);
            presenter.RefreshRepresentation();

            replica.ConfigureServerAppearance(
                "character.quaternius.female-dark");
            yield return null;

            PlayerAppearanceInstance[] appearances = presenter.VisualRoot
                .GetComponentsInChildren<PlayerAppearanceInstance>(true);
            Assert.That(appearances.Count(value => value.gameObject.activeSelf),
                Is.EqualTo(1));
            Assert.That(appearances.Single(value => value.gameObject.activeSelf)
                    .StableId,
                Is.EqualTo("character.quaternius.female-dark"));
            Assert.That(presenter.VisualRoot
                    .GetComponentsInChildren<ThirdPersonWeaponRig>(true)
                    .Count(value => value.gameObject.activeSelf),
                Is.EqualTo(1));

            var dead = new AuthoritativePlayerState(
                1, default, 0d, 0, 0d, 0d);
            replica.ConsumeServerState(
                NetcodePlayerState.FromDomain(1, dead),
                treatAsLocalOwner: false,
                currentEstimatedServerTick: 1d);
            yield return null;

            Assert.That(presenter.VisualRoot
                    .GetComponentsInChildren<Renderer>(true),
                Has.All.Matches<Renderer>(renderer => !renderer.enabled));
        }

        private NetworkPlayerReplica CreateReplica(
            NetworkCoopSessionAuthority authority,
            string appearanceId,
            bool owner,
            out NetworkPlayerAppearancePresenter presenter,
            out NetworkVerticalSliceInputDriver driver)
        {
            replicaObject = new GameObject("Issue90 Replica");
            NetworkPlayerReplica replica =
                replicaObject.AddComponent<NetworkPlayerReplica>();
            driver = replicaObject.AddComponent<
                NetworkVerticalSliceInputDriver>();
            Transform visualRoot = new GameObject(
                "ThirdPersonVisualRoot").transform;
            visualRoot.SetParent(replicaObject.transform, false);
            presenter = replicaObject.AddComponent<
                NetworkPlayerAppearancePresenter>();
            ThirdPersonWeaponCatalog weaponCatalog = Resources.Load<
                ThirdPersonWeaponCatalog>(
                ThirdPersonWeaponCatalog.ResourcesPath);
            presenter.Configure(
                visualRoot,
                weaponCatalog,
                weaponCatalog.DefaultWeaponId,
                true);
            replica.ConfigureServerIdentity(authority, 1, appearanceId);
            if (owner) replica.EnableOwnerTestHook(authority, 1);
            replica.ApplyOwnershipPolicy();
            return replica;
        }

        private NetworkCoopSessionAuthority CreateAuthority()
        {
            authorityObject = new GameObject("Issue90 Authority");
            NetworkCoopSessionAuthority authority =
                authorityObject.AddComponent<NetworkCoopSessionAuthority>();
            authority.EnableServerTestHook();
            authority.ConfigureServer(
                new CoopServerRules(
                    tickRate: 10,
                    maximumPastCommandTicks: 8,
                    historyCapacity: 16,
                    maximumMoveSpeed: 5d),
                new[] { new CoopPlayerSpawn(1, default) },
                new[]
                {
                    new CoopTargetSpawn(1,
                        new NetVector3(0d, 0d, 20d), 1d, 100d)
                });
            return authority;
        }
    }
}
