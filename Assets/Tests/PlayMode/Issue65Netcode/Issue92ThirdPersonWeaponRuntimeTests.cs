using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FPS.Tests.PlayMode.Issue65
{
    public sealed class Issue92ThirdPersonWeaponRuntimeTests
    {
        private GameObject root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryCharacterAndWeaponUsesZeroOffsetRightHandSocket()
        {
            PlayerAppearanceCatalog appearances = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            ThirdPersonWeaponCatalog weapons = Resources.Load<
                ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.ResourcesPath);
            root = new GameObject("Issue92 Runtime Matrix");

            foreach (PlayerAppearanceDefinition appearance in
                     appearances.Definitions)
            {
                Transform anchor = new GameObject(
                    $"Anchor_{appearance.StableId}").transform;
                anchor.SetParent(root.transform, false);
                GameObject character = PlayerAppearanceFactory.Create(
                    appearances, appearance.StableId, anchor,
                    out _, out bool usedFallback);
                Assert.That(usedFallback, Is.False);
                Animator animator = character.GetComponent<Animator>();
                Transform rightHand = animator.GetBoneTransform(
                    HumanBodyBones.RightHand);

                foreach (ThirdPersonWeaponDefinition weapon in
                         weapons.Definitions)
                {
                    ThirdPersonWeaponRig rig = ThirdPersonWeaponFactory.Create(
                        weapon, animator, out Transform socket);
                    Assert.That(socket.parent, Is.SameAs(rightHand));
                    Assert.That(socket.localPosition, Is.EqualTo(Vector3.zero));
                    Assert.That(socket.localRotation,
                        Is.EqualTo(Quaternion.identity));
                    Assert.That(socket.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(rig.transform.localPosition,
                        Is.EqualTo(Vector3.zero));
                    Assert.That(rig.transform.localRotation,
                        Is.EqualTo(Quaternion.identity));
                    Assert.That(rig.transform.localScale,
                        Is.EqualTo(Vector3.one));
                    Assert.That(rig.TryValidate(out string error), Is.True,
                        $"{appearance.StableId}/{weapon.StableId}: {error}");
                }
                yield return null;
                Assert.That(rightHand.Cast<Transform>().Count(child =>
                        child.gameObject.activeSelf &&
                        child.name == ThirdPersonWeaponFactory.WeaponSocketName),
                    Is.EqualTo(1), appearance.StableId);
            }
        }

        [UnityTest]
        public IEnumerator SwitchingWeaponNeverLeavesAnActiveDuplicate()
        {
            PlayerAppearanceCatalog appearances = Resources.Load<
                PlayerAppearanceCatalog>(PlayerAppearanceCatalog.ResourcesPath);
            ThirdPersonWeaponCatalog weapons = Resources.Load<
                ThirdPersonWeaponCatalog>(ThirdPersonWeaponCatalog.ResourcesPath);
            root = new GameObject("Issue92 Switch");
            GameObject character = PlayerAppearanceFactory.Create(
                appearances, appearances.DefaultAppearanceId, root.transform,
                out _, out _);
            Animator animator = character.GetComponent<Animator>();
            Transform rightHand = animator.GetBoneTransform(
                HumanBodyBones.RightHand);

            foreach (ThirdPersonWeaponDefinition definition in weapons.Definitions)
                ThirdPersonWeaponFactory.Create(definition, animator, out _);
            yield return null;

            ThirdPersonWeaponRig[] active = rightHand
                .GetComponentsInChildren<ThirdPersonWeaponRig>(true)
                .Where(value => value.gameObject.activeInHierarchy).ToArray();
            Assert.That(active, Has.Length.EqualTo(1));
            Assert.That(active[0].name,
                Does.Contain(weapons.Definitions.Last().StableId));
        }
    }
}
