using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAppearanceHost : MonoBehaviour
{
    public const string VisualRootName = "PlayerVisualRoot";

    [SerializeField] private PlayerAppearanceCatalog catalog;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private string requestedAppearanceId;
    [SerializeField] private bool spawnOnAwake = true;
    [SerializeField] private GameObject currentInstance;

    public PlayerAppearanceCatalog Catalog => catalog;
    public Transform VisualRoot => visualRoot;
    public string RequestedAppearanceId => requestedAppearanceId;
    public GameObject CurrentInstance => currentInstance;
    public PlayerAppearanceDefinition CurrentDefinition { get; private set; }
    public bool UsedFallback { get; private set; }

    public void Configure(PlayerAppearanceCatalog appearanceCatalog,
        Transform presentationRoot, string appearanceId, bool spawnAtRuntime)
    {
        catalog = appearanceCatalog;
        visualRoot = presentationRoot;
        requestedAppearanceId = appearanceId ?? string.Empty;
        spawnOnAwake = spawnAtRuntime;
    }

    private void Awake()
    {
        EnsureVisualRoot();
        if (spawnOnAwake) Apply(requestedAppearanceId);
    }

    public GameObject Apply(string appearanceId)
    {
        EnsureVisualRoot();
        requestedAppearanceId = appearanceId ?? string.Empty;
        currentInstance = PlayerAppearanceFactory.Create(catalog,
            requestedAppearanceId, visualRoot, out PlayerAppearanceDefinition definition,
            out bool usedFallback);
        CurrentDefinition = definition;
        UsedFallback = usedFallback;
        return currentInstance;
    }

    private void EnsureVisualRoot()
    {
        if (visualRoot != null) return;
        Transform existing = transform.Find(VisualRootName);
        if (existing != null)
        {
            visualRoot = existing;
            return;
        }
        var root = new GameObject(VisualRootName);
        visualRoot = root.transform;
        visualRoot.SetParent(transform, false);
    }
}
