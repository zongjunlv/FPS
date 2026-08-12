using UnityEngine;

public sealed class PlayerRuntimeCombatStats : MonoBehaviour
{
    public float WeaponDamageMultiplier { get; private set; } = 1f;

    public float ApplyWeaponDamage(float baseDamage)
    {
        return Mathf.Max(0f, baseDamage) * WeaponDamageMultiplier;
    }

    public void SetWeaponDamageMultiplier(float multiplier)
    {
        WeaponDamageMultiplier = Mathf.Max(0f, multiplier);
    }

    public void ResetRuntimeModifiers()
    {
        WeaponDamageMultiplier = 1f;
    }
}
