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

            ThirdPersonWeaponCatalog weaponCatalog =
                AssetDatabase.LoadAssetAtPath<ThirdPersonWeaponCatalog>(
                    ThirdPersonWeaponCatalog.DefaultAssetPath);
            if (weaponCatalog == null)
            {
                throw new InvalidOperationException(
                    "第三人称武器目录不存在，请先运行 Issue 92 构建器。");
            }
            if (!weaponCatalog.TryValidate(out string weaponError))
                throw new InvalidOperationException(
                    $"第三人称武器目录不可用：{weaponError}");
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
                    weaponCatalog,
                    weaponCatalog.DefaultWeaponId,
                    castOwnerShadows: true);
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
