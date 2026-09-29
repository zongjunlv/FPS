using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared single/co-op package geometry. This factory contains no inventory,
/// interaction, loot roll, ownership or damage logic.
/// </summary>
public sealed class WorldItemVisualFactory : IDisposable
{
    private readonly Dictionary<ItemEffectType, Material> bodies = new();
    private readonly Dictionary<ItemEffectType, Material> marks = new();

    public GameObject Create(string name, Transform parent,
        ItemEffectType effect, bool includeBodyCollider = false)
    {
        GameObject package = GameObject.CreatePrimitive(PrimitiveType.Cube);
        package.name = name;
        package.transform.SetParent(parent, false);
        package.transform.localScale = new Vector3(0.7f, 0.42f, 0.5f);
        package.GetComponent<Renderer>().sharedMaterial = MaterialFor(effect, false);
        if (!includeBodyCollider) RemoveCollider(package);
        CreateMark(package.transform, Vector3.forward * 0.52f, effect);
        CreateMark(package.transform, Vector3.back * 0.52f, effect);
        return package;
    }

    private void CreateMark(Transform parent, Vector3 position,
        ItemEffectType effect)
    {
        CreateBar(parent, position, new Vector3(0.28f, 0.08f, 0.035f), effect);
        CreateBar(parent, position, new Vector3(0.08f, 0.28f, 0.035f), effect);
    }

    private void CreateBar(Transform parent, Vector3 position, Vector3 scale,
        ItemEffectType effect)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "ItemMark";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localScale = scale;
        bar.GetComponent<Renderer>().sharedMaterial = MaterialFor(effect, true);
        RemoveCollider(bar);
    }

    private static void RemoveCollider(GameObject value)
    {
        Collider collider = value.GetComponent<Collider>();
        if (collider == null) return;
        collider.enabled = false;
        if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
        else UnityEngine.Object.DestroyImmediate(collider);
    }

    private Material MaterialFor(ItemEffectType effect, bool mark)
    {
        Dictionary<ItemEffectType, Material> cache = mark ? marks : bodies;
        if (cache.TryGetValue(effect, out Material existing)) return existing;
        Shader shader = Shader.Find(mark
            ? "Universal Render Pipeline/Unlit"
            : "Universal Render Pipeline/Lit");
        shader ??= Shader.Find(mark ? "Unlit/Color" : "Standard");
        Color color = effect switch
        {
            ItemEffectType.RestoreArmor => mark
                ? new Color(0.28f, 0.62f, 1f)
                : new Color(0.12f, 0.24f, 0.35f),
            ItemEffectType.AddRifleAmmo => mark
                ? new Color(0.75f, 0.9f, 0.3f)
                : new Color(0.25f, 0.28f, 0.18f),
            ItemEffectType.AddHandgunAmmo => mark
                ? new Color(1f, 0.7f, 0.22f)
                : new Color(0.32f, 0.24f, 0.14f),
            _ => mark ? new Color(0.86f, 0.12f, 0.12f)
                : new Color(0.86f, 0.89f, 0.88f)
        };
        Material material = shader != null ? new Material(shader) { color = color } : null;
        cache[effect] = material;
        return material;
    }

    public void Dispose()
    {
        Release(bodies);
        Release(marks);
    }

    private static void Release(Dictionary<ItemEffectType, Material> cache)
    {
        foreach (Material material in cache.Values)
        {
            if (material == null) continue;
            if (Application.isPlaying) UnityEngine.Object.Destroy(material);
            else UnityEngine.Object.DestroyImmediate(material);
        }
        cache.Clear();
    }
}

/// <summary>Owns only the disposable visual resources of a replicated package.</summary>
public sealed class ReplicatedWorldItemVisual : MonoBehaviour
{
    private WorldItemVisualFactory factory;

    public void Build(ItemEffectType effect)
    {
        if (factory != null) return;
        factory = new WorldItemVisualFactory();
        factory.Create("Package", transform, effect);
    }

    private void OnDestroy() => factory?.Dispose();
}
