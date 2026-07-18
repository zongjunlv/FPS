using Unity.VisualScripting;
using UnityEngine;

public class EnemyController : MonoBehaviour
{
    [SerializeField] private EnemyDefinition currentEnemy;
    [SerializeField] private GameObject bombEffect;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private float currentEnemyHP;
    void Start()
    {
        currentEnemyHP = currentEnemy.HP; 
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void GetHit(float Damage)
    {
        Debug.Log(currentEnemyHP);
        currentEnemyHP -= Damage;
        if(currentEnemyHP <= 0)
        {
            Instantiate(bombEffect, transform.position, transform.rotation);
            Destroy(gameObject);
        }
        
    }

}
