using UnityEngine;

[CreateAssetMenu(fileName = "Projectile", menuName = "Scriptable Objects/ProjectileDefinition")]
public class ProjectileDefinition : ScriptableObject
{
    public string BulletName;
    public float Speed;
    public float Damage;
}
