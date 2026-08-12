using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityNewInventoryBootstrap : MonoBehaviour
{
    private readonly List<Material> runtimeMaterials = new();
    private readonly List<GameObject> pickupObjects = new();
    private ItemDefinition[] definitions;
    private WorldItemPickup[] pickups;

    public ItemDefinition MedicalKit => FindDefinition("medical_kit");
    public WorldItemPickup Pickup => pickups != null && pickups.Length > 0
        ? pickups[0]
        : null;
    public ItemDefinition[] Definitions => definitions;
    public WorldItemPickup[] Pickups => pickups;

    private void Start()
    {
        if (SceneManager.GetActiveScene().name != "CityNew")
        {
            return;
        }

        PlayerInventoryController inventory =
            GetComponent<PlayerInventoryController>();

        if (inventory == null)
        {
            return;
        }

        HudIconCatalog icons = new();
        definitions = new[]
        {
            CreateDefinition(
                "medical_kit",
                "医疗包",
                "战地急救物资，使用后立即恢复生命值。",
                icons.Get(HudIconId.Health),
                5,
                ItemEffectType.RestoreHealth,
                35f),
            CreateDefinition(
                "armor_pack",
                "护甲包",
                "便携式护甲修复组件，可恢复受损护甲。",
                icons.Get(HudIconId.Armor),
                5,
                ItemEffectType.RestoreArmor,
                35f),
            CreateDefinition(
                "rifle_ammo",
                "步枪弹药",
                "标准步枪弹药箱，只补充步枪备弹。",
                icons.Get(HudIconId.Rifle),
                4,
                ItemEffectType.AddRifleAmmo,
                60f),
            CreateDefinition(
                "handgun_ammo",
                "手枪弹药",
                "轻型手枪弹药盒，只补充手枪备弹。",
                icons.Get(HudIconId.Handgun),
                6,
                ItemEffectType.AddHandgunAmmo,
                24f)
        };

        for (int index = 0; index < definitions.Length; index++)
        {
            inventory.RegisterItem(definitions[index]);
        }

        pickups = new WorldItemPickup[definitions.Length];
        pickups[0] = CreatePickup(
            definitions[0],
            1,
            new Vector3(0.75f, 0f, 2.2f),
            new Color(0.86f, 0.89f, 0.88f, 1f),
            new Color(0.86f, 0.12f, 0.12f, 1f));
        pickups[1] = CreatePickup(
            definitions[1],
            2,
            new Vector3(-0.75f, 0f, 2.2f),
            new Color(0.12f, 0.24f, 0.35f, 1f),
            new Color(0.28f, 0.62f, 1f, 1f));
        pickups[2] = CreatePickup(
            definitions[2],
            4,
            new Vector3(1.65f, 0f, 3.25f),
            new Color(0.25f, 0.28f, 0.18f, 1f),
            new Color(0.75f, 0.9f, 0.3f, 1f));
        pickups[3] = CreatePickup(
            definitions[3],
            4,
            new Vector3(-1.65f, 0f, 3.25f),
            new Color(0.32f, 0.24f, 0.14f, 1f),
            new Color(1f, 0.7f, 0.22f, 1f));
    }

    private void OnDestroy()
    {
        if (definitions != null)
        {
            for (int index = 0; index < definitions.Length; index++)
            {
                if (definitions[index] != null)
                {
                    Destroy(definitions[index]);
                }
            }
        }

        for (int index = 0; index < runtimeMaterials.Count; index++)
        {
            if (runtimeMaterials[index] != null)
            {
                Destroy(runtimeMaterials[index]);
            }
        }
    }

    private ItemDefinition CreateDefinition(
        string id,
        string displayName,
        string description,
        Sprite icon,
        int maximumStack,
        ItemEffectType effectType,
        float amount)
    {
        ItemDefinition definition =
            ScriptableObject.CreateInstance<ItemDefinition>();
        definition.name = displayName;
        definition.Configure(
            id,
            displayName,
            description,
            icon,
            ItemType.Consumable,
            maximumStack,
            effectType,
            amount);
        return definition;
    }

    private WorldItemPickup CreatePickup(
        ItemDefinition definition,
        int quantity,
        Vector3 localOffset,
        Color bodyColor,
        Color markColor)
    {
        GameObject pickupObject = GameObject.CreatePrimitive(
            PrimitiveType.Cube);
        pickupObjects.Add(pickupObject);
        pickupObject.name = definition.StableId + "_Pickup";
        Vector3 forward = Vector3.ProjectOnPlane(
            transform.forward,
            Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 position = transform.position +
            right * localOffset.x +
            forward * localOffset.z +
            Vector3.up * 0.5f;

        if (Physics.Raycast(
                position + Vector3.up * 4f,
                Vector3.down,
                out RaycastHit hit,
                10f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore))
        {
            position.y = hit.point.y + 0.24f;
        }

        pickupObject.transform.position = position;
        pickupObject.transform.rotation = Quaternion.LookRotation(-forward);
        pickupObject.transform.localScale = new Vector3(0.7f, 0.42f, 0.5f);
        ApplyMaterial(pickupObject.GetComponent<Renderer>(), bodyColor, false);
        CreateMark(pickupObject.transform, Vector3.forward * 0.52f, markColor);
        CreateMark(pickupObject.transform, Vector3.back * 0.52f, markColor);
        WorldItemPickup pickup =
            pickupObject.AddComponent<WorldItemPickup>();
        pickup.Configure(definition, quantity);
        return pickup;
    }

    private void CreateMark(
        Transform parent,
        Vector3 position,
        Color color)
    {
        CreateMarkBar(
            parent,
            position,
            new Vector3(0.28f, 0.08f, 0.035f),
            color);
        CreateMarkBar(
            parent,
            position,
            new Vector3(0.08f, 0.28f, 0.035f),
            color);
    }

    private void CreateMarkBar(
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Color color)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "ItemMark";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localScale = scale;
        Collider collider = bar.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        ApplyMaterial(bar.GetComponent<Renderer>(), color, true);
    }

    private void ApplyMaterial(
        Renderer renderer,
        Color color,
        bool unlit)
    {
        Shader shader = unlit
            ? Shader.Find("Universal Render Pipeline/Unlit")
            : Shader.Find("Universal Render Pipeline/Lit");
        shader ??= Shader.Find(unlit ? "Unlit/Color" : "Standard");

        if (shader == null)
        {
            return;
        }

        Material material = new Material(shader) { color = color };
        renderer.material = material;
        runtimeMaterials.Add(material);
    }

    private ItemDefinition FindDefinition(string stableId)
    {
        if (definitions == null)
        {
            return null;
        }

        for (int index = 0; index < definitions.Length; index++)
        {
            if (definitions[index] != null &&
                definitions[index].StableId == stableId)
            {
                return definitions[index];
            }
        }

        return null;
    }
}
