using UnityEngine;

[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Scriptable Objects/EnemyDefinition")]
public class EnemyDefinition : ScriptableObject
{
    [SerializeField] private string stableId;
    [Header("Name")]
    [SerializeField] public string Name;
    [Header("Attributes")]
    [SerializeField] public float HP;
    [SerializeField] public float Attack;
    [SerializeField] public float Defend;
    [SerializeField, Min(0)] private int rewardExperience = 40;

    public string StableId => stableId;
    public int RewardExperience => Mathf.Max(0, rewardExperience);

    public void Configure(
        string id,
        string displayName,
        float health,
        float attack,
        float defend,
        int experience)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new System.ArgumentException(
                "An enemy requires a stable ID.", nameof(id));
        }

        stableId = id.Trim();
        Name = displayName ?? string.Empty;
        HP = Mathf.Max(1f, health);
        Attack = Mathf.Max(1f, attack);
        Defend = Mathf.Max(0f, defend);
        rewardExperience = Mathf.Max(0, experience);
    }
}
