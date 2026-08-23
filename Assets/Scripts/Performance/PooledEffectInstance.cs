using UnityEngine;

public sealed class PooledEffectInstance : MonoBehaviour
{
    private ParticleSystem[] particles;
    private RuntimeGameObjectPool owner;
    private float remainingLifetime;
    private string pooledName;
    private Transform followTarget;
    private bool hasFollowTarget;
    private Vector3 followLocalPosition;
    private Quaternion followLocalRotation;
    private EnemyController followedEnemy;
    private int followedEnemyGeneration;

    public void Prepare()
    {
        particles = GetComponentsInChildren<ParticleSystem>(true);
        pooledName = gameObject.name;
    }

    public void Play(
        RuntimeGameObjectPool sourcePool,
        float lifetime)
    {
        Play(sourcePool, lifetime, null);
    }

    public void Play(
        RuntimeGameObjectPool sourcePool,
        float lifetime,
        Transform attachmentTarget)
    {
        owner = sourcePool;
        remainingLifetime = Mathf.Max(0.01f, lifetime);
        followTarget = attachmentTarget;
        hasFollowTarget = attachmentTarget != null;

        if (followTarget != null)
        {
            followedEnemy =
                followTarget.GetComponentInParent<EnemyController>();
            followedEnemyGeneration =
                followedEnemy != null
                    ? followedEnemy.SpawnResetCount
                    : 0;
            followLocalPosition = followTarget.InverseTransformPoint(
                transform.position);
            followLocalRotation = Quaternion.Inverse(
                followTarget.rotation) * transform.rotation;
        }

        foreach (ParticleSystem particle in particles)
        {
            if (particle == null)
            {
                continue;
            }

            particle.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            particle.Play(true);
        }
    }

    private void Update()
    {
        if (hasFollowTarget)
        {
            if (followTarget == null ||
                !followTarget.gameObject.activeInHierarchy ||
                (followedEnemy != null &&
                 followedEnemy.SpawnResetCount != followedEnemyGeneration))
            {
                owner?.Return(gameObject);
                return;
            }

            transform.SetPositionAndRotation(
                followTarget.TransformPoint(followLocalPosition),
                followTarget.rotation * followLocalRotation);
        }

        remainingLifetime -= Time.deltaTime;

        if (remainingLifetime <= 0f)
        {
            owner?.Return(gameObject);
        }
    }

    private void OnDisable()
    {
        if (particles != null)
        {
            foreach (ParticleSystem particle in particles)
            {
                if (particle != null)
                {
                    particle.Stop(
                        true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        owner = null;
        followTarget = null;
        hasFollowTarget = false;
        followedEnemy = null;
        followedEnemyGeneration = 0;
        remainingLifetime = 0f;

        if (!string.IsNullOrEmpty(pooledName))
        {
            gameObject.name = pooledName;
        }
    }
}
