using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CityNewInventoryBootstrap : MonoBehaviour
{
    private ItemDefinition medicalKit;
    private GameObject pickupObject;
    private readonly List<Material> runtimeMaterials = new();

    public ItemDefinition MedicalKit => medicalKit;
    public WorldItemPickup Pickup { get; private set; }

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

        medicalKit = ScriptableObject.CreateInstance<ItemDefinition>();
        medicalKit.name = "医疗包";
        medicalKit.Configure(
            "medical_kit",
            "医疗包",
            "战地急救物资，使用后立即恢复生命值。",
            Resources.Load<Sprite>("UI/Icons/health"),
            ItemType.Consumable,
            5,
            ItemEffectType.RestoreHealth,
            35f);
        inventory.RegisterItem(medicalKit);
        CreatePickup();
    }

    private void OnDestroy()
    {
        if (medicalKit != null)
        {
            Destroy(medicalKit);
        }

        for (int index = 0; index < runtimeMaterials.Count; index++)
        {
            if (runtimeMaterials[index] != null)
            {
                Destroy(runtimeMaterials[index]);
            }
        }
    }

    private void CreatePickup()
    {
        pickupObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pickupObject.name = "MedicalKitPickup";
        Vector3 forward = Vector3.ProjectOnPlane(
            transform.forward,
            Vector3.up).normalized;
        Vector3 position = transform.position +
            forward * 2.2f + transform.right * 0.75f + Vector3.up * 0.5f;

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
        Renderer body = pickupObject.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        shader ??= Shader.Find("Standard");

        if (shader != null)
        {
            Material material = new Material(shader)
            {
                color = new Color(0.86f, 0.89f, 0.88f, 1f)
            };
            body.material = material;
            runtimeMaterials.Add(material);
        }

        CreateMedicalMark(pickupObject.transform, Vector3.forward * 0.52f);
        CreateMedicalMark(pickupObject.transform, Vector3.back * 0.52f);
        Pickup = pickupObject.AddComponent<WorldItemPickup>();
        Pickup.Configure(medicalKit, 1);
    }

    private void CreateMedicalMark(Transform parent, Vector3 position)
    {
        CreateMarkBar(parent, position, new Vector3(0.28f, 0.08f, 0.035f));
        CreateMarkBar(parent, position, new Vector3(0.08f, 0.28f, 0.035f));
    }

    private void CreateMarkBar(
        Transform parent,
        Vector3 position,
        Vector3 scale)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "MedicalMark";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = position;
        bar.transform.localScale = scale;
        Collider collider = bar.GetComponent<Collider>();

        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = bar.GetComponent<Renderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        shader ??= Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material material = new Material(shader)
            {
                color = new Color(0.86f, 0.12f, 0.12f, 1f)
            };
            renderer.material = material;
            runtimeMaterials.Add(material);
        }
    }
}
