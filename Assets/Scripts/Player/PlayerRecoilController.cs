using System;
using UnityEngine;

public class PlayerRecoilController : MonoBehaviour
{
    [SerializeField] private Transform recoilPivot;
    [SerializeField, Min(0f)] private float returnSpeed = 10f;
    [SerializeField, Min(0f)] private float snappiness = 10f;
    [SerializeField, Min(0f)] private float maxVerticalRecoil = 10f;
    [SerializeField, Min(0f)] private float maxHorizontalRecoil = 3f;

    private Vector2 targetRecoil;
    private Vector2 currentRecoil;
    private Quaternion initialRotation;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        initialRotation = recoilPivot.localRotation;
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void AddRecoil(float verticalRecoil, float horizontalRecoil)
    {
        targetRecoil.x = Mathf.Clamp(targetRecoil.x + verticalRecoil, 0f, maxVerticalRecoil);
        targetRecoil.y = Mathf.Clamp(
            targetRecoil.y + UnityEngine.Random.Range(-horizontalRecoil,horizontalRecoil), 
            -maxHorizontalRecoil, 
            maxHorizontalRecoil
        );
        
    }

    private void LateUpdate()
    {
        float returnFactor = 1f - Mathf.Exp(-returnSpeed * Time.deltaTime);
        float followFactor = 1f - Mathf.Exp(-snappiness * Time.deltaTime);
        targetRecoil = Vector2.Lerp(targetRecoil, Vector2.zero, returnFactor);
        currentRecoil = Vector2.Lerp(currentRecoil, targetRecoil, followFactor);
        Quaternion recoilRotation = Quaternion.Euler(-currentRecoil.x, currentRecoil.y, 0f);
        recoilPivot.localRotation = initialRotation * recoilRotation;
    }
}
