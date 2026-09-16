using System;
using FPS.Networking.Netcode;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace FPS.Editor.Networking
{
    public static class Issue65NetworkPrefabBuilder
    {
        private const string ResourceFolder =
            "Assets/Resources/Networking";
        private const string AuthorityPath =
            ResourceFolder + "/CoopSessionAuthority.prefab";
        private const string ReplicaPath =
            ResourceFolder + "/CoopPlayerReplica.prefab";
        private const string ThirdPersonWeaponPath =
            ResourceFolder + "/ThirdPersonRifleVisual.prefab";
        private const string ThirdPersonWeaponSourcePath =
            "Assets/ImportPackages/CSAssets2026/Infima Games/" +
            "Low Poly Shooter Pack - Free Sample/Prefabs/Weapons/" +
            "P_LPSP_WEP_AR_01.prefab";
        private const string DefaultPrefabsPath =
            "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("FPS/Issue 65/Rebuild Network Prefabs")]
        public static void Build()
        {
            EnsureFolder("Assets", "Resources");
            EnsureFolder("Assets/Resources", "Networking");

            GameObject authority = new GameObject("CoopSessionAuthority");
            try
            {
                authority.AddComponent<NetworkObject>();
                authority.AddComponent<NetworkCoopSessionAuthority>();
                PrefabUtility.SaveAsPrefabAsset(authority, AuthorityPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(authority);
            }

            GameObject thirdPersonWeapon = BuildThirdPersonWeaponVisual();
            GameObject replica = new GameObject("CoopPlayerReplica");
            try
            {
                replica.AddComponent<NetworkObject>();
                replica.AddComponent<NetworkPlayerReplica>();
                replica.AddComponent<NetworkVerticalSliceInputDriver>();
                replica.AddComponent<NetworkThirdPersonAnimator>();
                Transform visualRoot = new GameObject(
                    "ThirdPersonVisualRoot").transform;
                visualRoot.SetParent(replica.transform, false);
                NetworkPlayerAppearancePresenter presenter =
                    replica.AddComponent<NetworkPlayerAppearancePresenter>();
                presenter.Configure(
                    visualRoot,
                    thirdPersonWeapon,
                    castOwnerShadows: true,
                    new Vector3(0.02f, 0.04f, 0.02f),
                    new Vector3(0f, 90f, 90f),
                    Vector3.one);
                PrefabUtility.SaveAsPrefabAsset(replica, ReplicaPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(replica);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            RegisterDefaultPrefabs();
            Debug.Log(
                "[Issue65] Rebuilt authority and player replica prefabs.");
        }

        private static GameObject BuildThirdPersonWeaponVisual()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                ThirdPersonWeaponSourcePath);
            if (source == null)
            {
                throw new InvalidOperationException(
                    $"第三人称武器源资源缺失：{ThirdPersonWeaponSourcePath}");
            }

            GameObject root = new GameObject("ThirdPersonRifleVisual");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(
                    source);
                model.name = "Model";
                model.transform.SetParent(root.transform, false);
                model.transform.SetLocalPositionAndRotation(
                    Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;

                foreach (MonoBehaviour behaviour in root
                             .GetComponentsInChildren<MonoBehaviour>(true))
                    UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (Animator animator in root
                             .GetComponentsInChildren<Animator>(true))
                    UnityEngine.Object.DestroyImmediate(animator);
                foreach (Collider collider in root
                             .GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                foreach (Rigidbody body in root
                             .GetComponentsInChildren<Rigidbody>(true))
                    UnityEngine.Object.DestroyImmediate(body);
                foreach (AudioSource sourceAudio in root
                             .GetComponentsInChildren<AudioSource>(true))
                    UnityEngine.Object.DestroyImmediate(sourceAudio);
                foreach (ParticleSystem particles in root
                             .GetComponentsInChildren<ParticleSystem>(true))
                    UnityEngine.Object.DestroyImmediate(particles.gameObject);
                foreach (Renderer renderer in root
                             .GetComponentsInChildren<Renderer>(true))
                {
                    renderer.shadowCastingMode =
                        UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    root, ThirdPersonWeaponPath);
                if (prefab == null)
                    throw new InvalidOperationException(
                        "无法生成第三人称武器表现 Prefab。");
                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void RegisterDefaultPrefabs()
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<
                NetworkPrefabsList>(DefaultPrefabsPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, DefaultPrefabsPath);
            }

            Register(list, AssetDatabase.LoadAssetAtPath<GameObject>(
                AuthorityPath));
            Register(list, AssetDatabase.LoadAssetAtPath<GameObject>(
                ReplicaPath));
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
        }

        private static void Register(
            NetworkPrefabsList list,
            GameObject prefab)
        {
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Failed to load generated network prefab.");
            }

            if (!list.Contains(prefab))
            {
                list.Add(new NetworkPrefab
                {
                    Override = NetworkPrefabOverride.None,
                    Prefab = prefab
                });
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
