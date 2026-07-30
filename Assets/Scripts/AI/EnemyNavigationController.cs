using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public sealed class EnemyNavigationController : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float patrolRadius = 5f;
    [SerializeField, Min(2)] private int generatedPatrolPointCount = 4;
    [SerializeField, Min(0.05f)] private float arrivalDistance = 0.45f;
    [SerializeField, Min(0.1f)] private float fallbackMoveSpeed = 2.6f;

    private readonly List<Vector3> patrolPoints = new();
    private NavMeshAgent agent;
    private int patrolIndex;
    private bool hasDestination;
    private bool destinationIsPatrol;
    private bool movementStopped;

    public Vector3 Destination { get; private set; }
    public int PatrolPointCount => patrolPoints.Count;
    public bool HasReachedDestination
    {
        get
        {
            if (!hasDestination)
            {
                return false;
            }

            Vector3 offset = Destination - transform.position;
            offset.y = 0f;

            if (offset.sqrMagnitude <=
                arrivalDistance * arrivalDistance)
            {
                return true;
            }

            if (!UsesNavMesh || agent.pathPending)
            {
                return false;
            }

            float navMeshArrivalDistance = Mathf.Max(
                arrivalDistance,
                agent.stoppingDistance + 0.15f);

            if (agent.hasPath &&
                !float.IsInfinity(agent.remainingDistance))
            {
                return agent.remainingDistance <=
                    navMeshArrivalDistance;
            }

            return agent.velocity.sqrMagnitude <= 0.01f;
        }
    }
    public bool UsesNavMesh =>
        agent != null &&
        agent.enabled &&
        agent.isOnNavMesh;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        ConfigureAgent();
        GeneratePatrolPoints();
    }

    public void AttachToNavMesh()
    {
        if (agent == null)
        {
            agent = gameObject.AddComponent<NavMeshAgent>();
        }

        ConfigureAgent();

        if (!agent.isOnNavMesh &&
            NavMesh.SamplePosition(
                transform.position,
                out NavMeshHit hit,
                3f,
                NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
        }

        GeneratePatrolPoints();
    }

    private void ConfigureAgent()
    {
        if (agent == null)
        {
            return;
        }

        agent.speed = 2.8f;
        agent.angularSpeed = 420f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.3f;
        agent.radius = 0.35f;
        agent.height = 1.2f;
    }

    private void Update()
    {
        if (movementStopped || !hasDestination || UsesNavMesh)
        {
            return;
        }

        Vector3 direction = Destination - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <=
            arrivalDistance * arrivalDistance)
        {
            return;
        }

        Vector3 step = direction.normalized *
            fallbackMoveSpeed * Time.deltaTime;
        transform.position += Vector3.ClampMagnitude(
            step,
            direction.magnitude);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            420f * Time.deltaTime);
    }

    public void ConfigurePatrolPoints(IEnumerable<Vector3> points)
    {
        patrolPoints.Clear();

        if (points != null)
        {
            patrolPoints.AddRange(points);
        }

        patrolIndex = 0;
        hasDestination = false;
    }

    public void TickPatrol()
    {
        if (patrolPoints.Count == 0)
        {
            return;
        }

        if (!hasDestination || !destinationIsPatrol)
        {
            SetPatrolDestination(patrolPoints[patrolIndex]);
            return;
        }

        if (!HasReachedDestination)
        {
            return;
        }

        patrolIndex = (patrolIndex + 1) % patrolPoints.Count;
        SetPatrolDestination(patrolPoints[patrolIndex]);
    }

    public void SetDestination(Vector3 destination)
    {
        SetDestination(destination, false);
    }

    public bool TryResolveReachableDestination(
        Vector3 desired,
        float sampleRadius,
        out Vector3 resolved)
    {
        if (!UsesNavMesh)
        {
            resolved = desired;
            return true;
        }

        if (!NavMesh.SamplePosition(
                desired,
                out NavMeshHit hit,
                Mathf.Max(0.1f, sampleRadius),
                agent.areaMask))
        {
            resolved = default;
            return false;
        }

        NavMeshPath path = new NavMeshPath();

        if (!NavMesh.CalculatePath(
                transform.position,
                hit.position,
                agent.areaMask,
                path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            resolved = default;
            return false;
        }

        resolved = hit.position;
        return true;
    }

    public void Stop()
    {
        movementStopped = true;

        if (UsesNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private void SetPatrolDestination(Vector3 destination)
    {
        SetDestination(destination, true);
    }

    private void SetDestination(
        Vector3 destination,
        bool isPatrolDestination)
    {
        Vector3 destinationOffset = destination - Destination;
        destinationOffset.y = 0f;
        bool destinationChanged =
            !hasDestination ||
            destinationIsPatrol != isPatrolDestination ||
            destinationOffset.sqrMagnitude > 0.01f;
        bool resumeAfterStop = movementStopped;

        Destination = destination;
        hasDestination = true;
        destinationIsPatrol = isPatrolDestination;
        movementStopped = false;

        if (UsesNavMesh)
        {
            agent.isStopped = false;

            if (destinationChanged || resumeAfterStop)
            {
                agent.SetDestination(destination);
            }
        }
    }

    private void GeneratePatrolPoints()
    {
        patrolPoints.Clear();
        Vector3 origin = transform.position;

        if (UsesNavMesh)
        {
            GenerateReachableNavMeshPoints(origin);

            if (patrolPoints.Count >= 2)
            {
                patrolIndex = 0;
                hasDestination = false;
                return;
            }
        }

        for (int index = 0;
             index < generatedPatrolPointCount;
             index++)
        {
            float angle = index * Mathf.PI * 2f /
                generatedPatrolPointCount;
            Vector3 candidate = origin + new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)) * patrolRadius;

            if (NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    2f,
                    NavMesh.AllAreas))
            {
                candidate = hit.position;
            }

            patrolPoints.Add(candidate);
        }
    }

    private void GenerateReachableNavMeshPoints(Vector3 origin)
    {
        int candidateCount = generatedPatrolPointCount * 4;

        for (int index = 0;
             index < candidateCount &&
             patrolPoints.Count < generatedPatrolPointCount;
             index++)
        {
            float angle = index * Mathf.PI * 2f / candidateCount;
            float distance = Mathf.Lerp(
                1.25f,
                patrolRadius,
                (index % 4) / 3f);
            Vector3 candidate = origin + new Vector3(
                Mathf.Cos(angle),
                0f,
                Mathf.Sin(angle)) * distance;

            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    1.5f,
                    NavMesh.AllAreas) ||
                Vector3.Distance(origin, hit.position) <
                arrivalDistance * 2f)
            {
                continue;
            }

            NavMeshPath path = new NavMeshPath();

            if (!NavMesh.CalculatePath(
                    origin,
                    hit.position,
                    NavMesh.AllAreas,
                    path) ||
                path.status != NavMeshPathStatus.PathComplete)
            {
                continue;
            }

            bool duplicate = false;

            foreach (Vector3 point in patrolPoints)
            {
                if (Vector3.Distance(point, hit.position) < 0.75f)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                patrolPoints.Add(hit.position);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;

        for (int index = 0;
             index < patrolPoints.Count;
             index++)
        {
            Gizmos.DrawWireSphere(
                patrolPoints[index],
                0.25f);
            Gizmos.DrawLine(
                patrolPoints[index],
                patrolPoints[(index + 1) % patrolPoints.Count]);
        }

        if (hasDestination)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, Destination);
            Gizmos.DrawWireSphere(Destination, arrivalDistance);
        }
    }
}
