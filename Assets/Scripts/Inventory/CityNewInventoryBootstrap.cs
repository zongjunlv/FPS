using System.Collections.Generic;
using FPS.Core.GameModes;
using UnityEngine;

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
        if (!GameModeContext.IsActive(
                GameModeId.SoloBattle,
                GameModeStage.Battle))
        {
            return;
        }

        PlayerInventoryController inventory =
            GetComponent<PlayerInventoryController>();

        if (inventory == null)
        {
            return;
        }

        CityNewContentCatalog catalog = CityNewContentCatalog.LoadDefault();
        string error = string.Empty;
        if (catalog == null || !catalog.TryValidate(out error))
        {
            Debug.LogError(
                "CityNew inventory content is unavailable: " +
                (catalog == null ? "default catalog is missing." : error),
                this);
            return;
        }

        definitions = new ItemDefinition[catalog.Items.Count];
        for (int index = 0; index < definitions.Length; index++)
        {
            definitions[index] = catalog.Items[index];
        }

        for (int index = 0; index < definitions.Length; index++)
        {
            inventory.RegisterItem(definitions[index]);
        }

        inventory.BindQuickSlot(0, "medical_kit");
        inventory.BindQuickSlot(1, "armor_pack");

        ItemDefinition medicalKit = FindDefinition("medical_kit");
        ItemDefinition armorPack = FindDefinition("armor_pack");
        ItemDefinition rifleAmmo = FindDefinition("rifle_ammo");
        ItemDefinition handgunAmmo = FindDefinition("handgun_ammo");
        if (medicalKit == null || armorPack == null ||
            rifleAmmo == null || handgunAmmo == null)
        {
            Debug.LogError(
                "CityNew inventory requires medical_kit, armor_pack, " +
                "rifle_ammo and handgun_ammo assets.", this);
            return;
        }

        pickups = new WorldItemPickup[4];
        pickups[0] = CreatePickup(
            medicalKit,
            1,
            new Vector3(0.75f, 0f, 2.2f),
            new Color(0.86f, 0.89f, 0.88f, 1f),
            new Color(0.86f, 0.12f, 0.12f, 1f));
        pickups[1] = CreatePickup(
            armorPack,
            2,
            new Vector3(-0.75f, 0f, 2.2f),
            new Color(0.12f, 0.24f, 0.35f, 1f),
            new Color(0.28f, 0.62f, 1f, 1f));
        pickups[2] = CreatePickup(
            rifleAmmo,
            4,
            new Vector3(1.65f, 0f, 3.25f),
            new Color(0.25f, 0.28f, 0.18f, 1f),
            new Color(0.75f, 0.9f, 0.3f, 1f));
        pickups[3] = CreatePickup(
            handgunAmmo,
            4,
            new Vector3(-1.65f, 0f, 3.25f),
            new Color(0.32f, 0.24f, 0.14f, 1f),
            new Color(1f, 0.7f, 0.22f, 1f));
    }

    private void OnDestroy()
    {
        for (int index = 0; index < runtimeMaterials.Count; index++)
        {
            if (runtimeMaterials[index] != null)
            {
                Destroy(runtimeMaterials[index]);
            }
        }
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
