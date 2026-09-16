using UnityEngine;

public sealed class HumanoidCharacterMarker : MonoBehaviour
{
    [SerializeField] private string stableId;
    [SerializeField] private float targetHeight =
        HumanoidCharacterStandard.StandardHeight;
    [SerializeField] private string sourceModelPath;

    public string StableId => stableId;
    public float TargetHeight => targetHeight;
    public string SourceModelPath => sourceModelPath;

    public void Configure(string id, float height, string modelPath)
    {
        stableId = id?.Trim() ?? string.Empty;
        targetHeight = Mathf.Max(0.1f, height);
        sourceModelPath = modelPath?.Trim() ?? string.Empty;
    }
}
