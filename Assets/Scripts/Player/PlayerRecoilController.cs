using System;
using UnityEngine;

[DefaultExecutionOrder(200)]
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
    private float movementAmount;
    private float lastRecoilTime = -100f;
    private int consecutiveShots;
    public Vector2 TargetRecoil => targetRecoil;
    public Vector2 CurrentRecoil => currentRecoil;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        initialRotation = recoilPivot.localRotation;
    }

    public void SetMovementAmount(float amount)
    {
        movementAmount = Mathf.Clamp01(amount);
    }

    public void AddRecoil(float verticalRecoil, float horizontalRecoil)
    {
        if (Time.unscaledTime - lastRecoilTime <= 0.24f)
        {
            consecutiveShots++;
        }
        else
        {
            consecutiveShots = 1;
        }

        lastRecoilTime = Time.unscaledTime;
        float movementMultiplier =
            Mathf.Lerp(1f, 1.3f, movementAmount);
        float burstMultiplier =
            1f + Mathf.Min(0.65f, (consecutiveShots - 1) * 0.15f);
        float totalMultiplier =
            movementMultiplier * burstMultiplier;
        targetRecoil.x = Mathf.Clamp(
            targetRecoil.x + verticalRecoil * totalMultiplier,
            0f,
            maxVerticalRecoil);
        targetRecoil.y = Mathf.Clamp(
            targetRecoil.y + UnityEngine.Random.Range(
                -horizontalRecoil,
                horizontalRecoil) * totalMultiplier,
            -maxHorizontalRecoil, 
            maxHorizontalRecoil
        );
        
    }

    private void LateUpdate()
    {
        if (recoilPivot == null)
        {
            return;
        }

        float returnFactor = 1f - Mathf.Exp(-returnSpeed * Time.deltaTime);
        float followFactor = 1f - Mathf.Exp(-snappiness * Time.deltaTime);
        targetRecoil = Vector2.Lerp(targetRecoil, Vector2.zero, returnFactor);
        currentRecoil = Vector2.Lerp(currentRecoil, targetRecoil, followFactor);
        Quaternion recoilRotation = Quaternion.Euler(-currentRecoil.x, currentRecoil.y, 0f);
        recoilPivot.localRotation = initialRotation * recoilRotation;
    }
}
