using UnityEngine;

[CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Scriptable Objects/EnemyDefinition")]
public class EnemyDefinition : ScriptableObject
{
    [Header("Name")]
    [SerializeField] public string Name;
    [Header("Attributes")]
    [SerializeField] public float HP;
    [SerializeField] public float Attack;
    [SerializeField] public float Defend;
}
