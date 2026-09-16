using System;
using UnityEngine;

public static class ThirdPersonWeaponFactory
{
    public const string WeaponSocketName = "WeaponSocket";

    public static ThirdPersonWeaponRig Create(
        ThirdPersonWeaponDefinition definition,
        Animator characterAnimator,
        out Transform weaponSocket)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));
        if (characterAnimator == null)
            throw new ArgumentNullException(nameof(characterAnimator));
        if (!definition.TryValidate(out string error))
            throw new InvalidOperationException(error);

        Transform rightHand = characterAnimator.GetBoneTransform(
            HumanBodyBones.RightHand);
        if (rightHand == null)
            throw new InvalidOperationException("角色缺少 Humanoid 右手骨。 ");
        RemoveExistingSocket(rightHand);

        var socketObject = new GameObject(WeaponSocketName);
        weaponSocket = socketObject.transform;
        weaponSocket.SetParent(rightHand, false);
        weaponSocket.SetLocalPositionAndRotation(
            Vector3.zero, Quaternion.identity);
        weaponSocket.localScale = Vector3.one;

        GameObject instance = UnityEngine.Object.Instantiate(
            definition.CalibratedPrefab, weaponSocket, false);
        instance.name = $"ThirdPersonWeapon_{definition.StableId}";
        instance.transform.SetLocalPositionAndRotation(
            Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        ThirdPersonWeaponRig rig = instance.GetComponent<ThirdPersonWeaponRig>();
        DisableGameplayComponents(instance, rig);
        return rig;
    }

    public static void RemoveExistingSocket(Transform rightHand)
    {
        if (rightHand == null) return;
        for (int index = rightHand.childCount - 1; index >= 0; index--)
        {
            Transform child = rightHand.GetChild(index);
            if (child.name != WeaponSocketName) continue;
            child.gameObject.SetActive(false);
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(child.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void DisableGameplayComponents(
        GameObject instance,
        ThirdPersonWeaponRig rig)
    {
        foreach (MonoBehaviour behaviour in instance
                     .GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != rig) behaviour.enabled = false;
        }
        foreach (Animator animator in instance
                     .GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
        foreach (Collider collider in instance
                     .GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (Rigidbody body in instance
                     .GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (AudioSource source in instance
                     .GetComponentsInChildren<AudioSource>(true))
            source.enabled = false;
        foreach (ParticleSystem particles in instance
                     .GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
