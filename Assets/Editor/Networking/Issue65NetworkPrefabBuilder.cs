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

            GameObject replica = new GameObject("CoopPlayerReplica");
            try
            {
                replica.AddComponent<NetworkObject>();
                replica.AddComponent<NetworkPlayerReplica>();
                replica.AddComponent<NetworkVerticalSliceInputDriver>();
                AddPlayerVisual(replica.transform);
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

        private static void AddPlayerVisual(Transform parent)
        {
            GameObject capsule = GameObject.CreatePrimitive(
                PrimitiveType.Capsule);
            capsule.name = "RemotePlayerVisual";
            capsule.transform.SetParent(parent, false);
            capsule.transform.localPosition = new Vector3(0f, 1f, 0f);
            Collider collider = capsule.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            Renderer renderer = capsule.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<
                    Material>("Default-Material.mat");
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
