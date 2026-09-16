using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Issue81HumanoidRetargetingTests
{
    [UnityTest]
    public IEnumerator AllCharactersRetargetMovementAndCombatWithoutInvalidBones()
    {
        HumanoidCharacterStandard standard = Resources.Load<HumanoidCharacterStandard>(
            "Content/Characters/Humanoid/HumanoidCharacterStandard");
        Assert.That(standard, Is.Not.Null);
        string[] states =
        {
            "Idle", "Walk", "Sprint", "Crouch", "Jump Start",
            "Jump Loop", "Land", "Aim", "Shoot", "Reload"
        };
        foreach (HumanoidCharacterEntry entry in standard.Characters)
        {
            GameObject instance = Object.Instantiate(entry.Prefab);
            try
            {
                Animator animator = instance.GetComponent<Animator>();
                Assert.That(animator, Is.Not.Null, entry.StableId);
                animator.Rebind();
                yield return null;
                foreach (string state in states)
                {
                    animator.Play(state, 0, 0.35f);
                    animator.Update(1f / 30f);
                    yield return null;
                    ValidateBone(animator, HumanBodyBones.Hips, entry.StableId, state);
                    ValidateBone(animator, HumanBodyBones.Head, entry.StableId, state);
                    ValidateBone(animator, HumanBodyBones.LeftHand, entry.StableId, state);
                    ValidateBone(animator, HumanBodyBones.RightHand, entry.StableId, state);
                    ValidateBone(animator, HumanBodyBones.LeftFoot, entry.StableId, state);
                    ValidateBone(animator, HumanBodyBones.RightFoot, entry.StableId, state);
                }
            }
            finally
            {
                Object.Destroy(instance);
            }
            yield return null;
        }
    }

    private static void ValidateBone(
        Animator animator,
        HumanBodyBones bone,
        string character,
        string state)
    {
        Transform transform = animator.GetBoneTransform(bone);
        Assert.That(transform, Is.Not.Null, $"{character}/{state}/{bone}");
        Assert.That(IsFinite(transform.position), Is.True,
            $"{character}/{state}/{bone} position");
        Assert.That(IsFinite(transform.localRotation), Is.True,
            $"{character}/{state}/{bone} rotation");
        Assert.That(transform.localScale.x, Is.GreaterThan(0f),
            $"{character}/{state}/{bone} scale x");
        Assert.That(transform.localScale.y, Is.GreaterThan(0f),
            $"{character}/{state}/{bone} scale y");
        Assert.That(transform.localScale.z, Is.GreaterThan(0f),
            $"{character}/{state}/{bone} scale z");
        Assert.That(Vector3.Distance(
                animator.transform.position,
                transform.position),
            Is.LessThan(4f), $"{character}/{state}/{bone} displacement");
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.x) && float.IsFinite(value.y) &&
        float.IsFinite(value.z) && float.IsFinite(value.w);
}
