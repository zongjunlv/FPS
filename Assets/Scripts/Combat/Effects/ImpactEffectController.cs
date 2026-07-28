using UnityEngine;

public class ImpactEffectController : MonoBehaviour
{
    [SerializeField, Min(0f)] private float lifetime = 3f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
