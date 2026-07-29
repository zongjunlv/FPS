using UnityEngine;

public static class DamageableResolver
{
    public static IDamageable Find(Transform origin)
    {
        Transform current = origin;

        while (current != null)
        {
            MonoBehaviour[] behaviours =
                current.GetComponents<MonoBehaviour>();

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IDamageable damageable)
                {
                    return damageable;
                }
            }

            current = current.parent;
        }

        return null;
    }
}
