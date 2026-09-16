using System;
using UnityEngine;

public sealed class PlayerAppearanceInstance : MonoBehaviour
{
    [SerializeField] private string stableId;
    public string StableId => stableId;
    public void Configure(string id) => stableId = id ?? string.Empty;
}

public static class PlayerAppearanceFactory
{
    public static GameObject Create(PlayerAppearanceCatalog catalog,
        string requestedId, Transform visualRoot,
        out PlayerAppearanceDefinition resolvedDefinition,
        out bool usedFallback)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (visualRoot == null) throw new ArgumentNullException(nameof(visualRoot));
        resolvedDefinition = catalog.Resolve(requestedId, out usedFallback);
        string error = "安全默认角色不存在。";
        if (resolvedDefinition == null ||
            !resolvedDefinition.TryValidate(out error))
        {
            throw new InvalidOperationException(
                $"无法生成玩家外观：{error}");
        }

        ClearActiveAppearance(visualRoot);
        GameObject instance = UnityEngine.Object.Instantiate(
            resolvedDefinition.VisualPrefab, visualRoot, false);
        instance.name = $"Appearance_{resolvedDefinition.StableId}";
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        Animator animator = instance.GetComponent<Animator>();
        animator.avatar = resolvedDefinition.Avatar;
        animator.runtimeAnimatorController =
            resolvedDefinition.AnimatorController;
        animator.applyRootMotion = false;

        ConfigureLod(instance, resolvedDefinition.LodConfiguration);
        PlayerAppearanceInstance marker =
            instance.GetComponent<PlayerAppearanceInstance>();
        if (marker == null) marker = instance.AddComponent<PlayerAppearanceInstance>();
        marker.Configure(resolvedDefinition.StableId);
        return instance;
    }

    private static void ClearActiveAppearance(Transform visualRoot)
    {
        PlayerAppearanceInstance[] instances = visualRoot
            .GetComponentsInChildren<PlayerAppearanceInstance>(true);
        foreach (PlayerAppearanceInstance instance in instances)
        {
            if (instance == null || instance.transform == visualRoot) continue;
            instance.gameObject.SetActive(false);
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(instance.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(instance.gameObject);
        }
    }

    private static void ConfigureLod(GameObject instance,
        PlayerAppearanceLodConfiguration settings)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        LODGroup group = instance.GetComponent<LODGroup>();
        if (group == null) group = instance.AddComponent<LODGroup>();
        group.fadeMode = settings.FadeMode;
        group.animateCrossFading = settings.FadeMode == LODFadeMode.CrossFade;
        var lod = new LOD(settings.VisibleHeight, renderers)
        {
            fadeTransitionWidth = settings.CrossFadeWidth
        };
        group.SetLODs(new[] { lod });
        group.RecalculateBounds();
    }
}
