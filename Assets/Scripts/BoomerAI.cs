using UnityEngine;
using UnityEngine.AI;

public class BoomerAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;
    public float chargeSpeed = 9f;
    public float rotationSpeed = 360f;

    [Header("Patrol")]
    public float patrolRadius = 12f;
    public float directionChangeTime = 3f;

    [Header("Vision")]
    public float detectionRange = 12f;

    [Range(0f, 360f)]
    public float fieldOfView = 90f;

    [Header("Investigation")]
    public float investigateDuration = 6f;
    public float investigateSpeed = 2.5f;

    [Header("Charge")]
    public float chargeRange = 7f;

    [Header("Explosion")]
    public float explosionDistance = 2.2f;
    public float explosionRadius = 3f;
    public int explosionDamage = 30;
    public GameObject explosionEffect;

    [Header("NavMesh Pathfinding")]
    public float repathInterval = 0.25f;
    public float arrivalDistance = 0.6f;
    public float navMeshSpawnSearch = 3f;

    // =====================================
    // AI STATES
    // =====================================

    private enum AIState
    {
        Patrol,
        Chase,
        Investigate
    }

    private AIState currentState = AIState.Patrol;

    private NavMeshAgent agent;
    private NavMeshPath testPath;
    private Transform mazeWalls;

    private Vector3 homePosition;
    private Vector3 lastKnownPosition;

    private float nextPatrolTime;
    private float nextRepathTime;
    private float nextInitializationAttempt;
    private float investigateTimer;

    private bool ready = false;
    private bool hasExploded = false;
    private bool investigatePathNeeded = false;

    // =====================================
    // INITIALIZATION
    // =====================================

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        testPath = new NavMeshPath();

        if (agent != null)
        {
            // Wait until procedural NavMesh is ready
            agent.enabled = false;
        }
        else
        {
            Debug.LogError(
                "Boomer needs a NavMeshAgent component!"
            );
        }
    }

    private void OnEnable()
    {
        Movement.OnPlayerFootstep += HearFootstep;
    }

    private void OnDisable()
    {
        Movement.OnPlayerFootstep -= HearFootstep;
    }

    private void Start()
    {
        homePosition = transform.position;
    }

    // =====================================
    // MAIN AI DECISION MAKING
    // =====================================

    private void Update()
    {
        if (hasExploded)
            return;

        if (!ready)
        {
            TryInitializeAgent();
            return;
        }

        if (agent == null ||
            !agent.enabled ||
            !agent.isOnNavMesh)
        {
            ready = false;
            return;
        }

        // Check player visibility
        bool playerVisible = CanSeePlayer();

        if (playerVisible)
        {
            lastKnownPosition = player.position;
            investigateTimer = investigateDuration;

            ChangeState(AIState.Chase);
        }
        else if (currentState == AIState.Chase)
        {
            // Lost sight of player
            BeginInvestigation(lastKnownPosition);
        }

        switch (currentState)
        {
            case AIState.Patrol:
                Patrol();
                break;

            case AIState.Chase:
                ChasePlayer();
                break;

            case AIState.Investigate:
                InvestigatePosition();
                break;
        }
    }

    // =====================================
    // CONNECT TO PROCEDURAL NAVMESH
    // =====================================

    private void TryInitializeAgent()
    {
        if (agent == null ||
            Time.time < nextInitializationAttempt)
        {
            return;
        }

        nextInitializationAttempt = Time.time + 0.5f;

        NavMeshHit hit;

        // Find nearby walkable position
        if (!NavMesh.SamplePosition(
            transform.position,
            out hit,
            navMeshSpawnSearch,
            NavMesh.AllAreas
        ))
        {
            return;
        }

        if (agent.enabled)
            agent.enabled = false;

        // Place Boomer near the walkable surface
        transform.position =
            hit.position +
            Vector3.up * agent.baseOffset;

        agent.enabled = true;

        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            return;
        }

        agent.speed = patrolSpeed;
        agent.angularSpeed = rotationSpeed;
        agent.acceleration = 12f;
        agent.stoppingDistance = 0.15f;
        agent.autoBraking = true;
        agent.autoRepath = true;

        homePosition = agent.nextPosition;

        ready = true;
        nextPatrolTime = 0f;
        nextRepathTime = 0f;

        Debug.Log(
            "Boomer connected to NavMesh!"
        );
    }

    // =====================================
    // STATE MANAGEMENT
    // =====================================

    private void ChangeState(AIState newState)
    {
        if (currentState == newState)
            return;

        currentState = newState;
        nextRepathTime = 0f;

        if (ready &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.updateRotation = true;
            agent.ResetPath();
        }

        Debug.Log(
            "Boomer State: " + currentState
        );
    }

    // =====================================
    // NAVMESH PATHFINDING
    // =====================================

    private bool MoveToPosition(
        Vector3 destination,
        float speed,
        float sampleRadius = 1.5f
    )
    {
        if (!ready ||
            !agent.enabled ||
            !agent.isOnNavMesh)
        {
            return false;
        }

        NavMeshHit hit;

        if (!NavMesh.SamplePosition(
            destination,
            out hit,
            sampleRadius,
            agent.areaMask
        ))
        {
            return false;
        }

        // Avoid selecting another floor
        if (Mathf.Abs(
            hit.position.y - agent.nextPosition.y
        ) > 1.5f)
        {
            return false;
        }

        // Verify complete reachable path
        if (!agent.CalculatePath(
            hit.position,
            testPath
        ))
        {
            return false;
        }

        if (testPath.status !=
            NavMeshPathStatus.PathComplete)
        {
            return false;
        }

        agent.speed = speed;
        agent.angularSpeed = rotationSpeed;
        agent.isStopped = false;
        agent.updateRotation = true;

        return agent.SetDestination(hit.position);
    }

    private bool HasReachedDestination()
    {
        if (agent.pathPending)
            return false;

        if (!agent.hasPath)
            return true;

        return !float.IsInfinity(agent.remainingDistance) &&
            agent.remainingDistance <= Mathf.Max(
                arrivalDistance,
                agent.stoppingDistance + 0.1f
            );
    }

    // =====================================
    // RANDOM NAVMESH PATROL
    // =====================================

    private void Patrol()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        agent.updateRotation = true;

        // Continue following current path
        if (!HasReachedDestination())
            return;

        if (Time.time < nextPatrolTime)
            return;

        // Find a random reachable location
        for (int i = 0; i < 30; i++)
        {
            Vector2 random =
                UnityEngine.Random.insideUnitCircle
                * patrolRadius;

            Vector3 candidate =
                homePosition +
                new Vector3(
                    random.x,
                    0f,
                    random.y
                );

            if ((candidate - agent.nextPosition)
                .sqrMagnitude < 4f)
            {
                continue;
            }

            if (MoveToPosition(
                candidate,
                patrolSpeed
            ))
            {
                nextPatrolTime =
                    Time.time +
                    Mathf.Max(
                        0.2f,
                        directionChangeTime
                    );

                return;
            }
        }

        // Retry if no valid patrol point
        nextPatrolTime = Time.time + 1f;
    }

    // =====================================
    // MAZE WALL DETECTION
    // =====================================

    private bool WallBetween(
        Vector3 start,
        Vector3 end
    )
    {
        // MazeGenerator creates MazeWalls
        if (mazeWalls == null)
        {
            GameObject walls =
                GameObject.Find("MazeWalls");

            if (walls != null)
            {
                mazeWalls = walls.transform;
            }
        }

        if (mazeWalls == null)
            return false;

        Vector3 direction = end - start;
        float distance = direction.magnitude;

        if (distance < 0.001f)
            return false;

        RaycastHit[] hits = Physics.RaycastAll(
            start,
            direction.normalized,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore
        );

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null &&
                hit.collider.transform.IsChildOf(
                    mazeWalls
                ))
            {
                return true;
            }
        }

        return false;
    }

    // =====================================
    // PLAYER VISION
    // =====================================

    private bool CanSeePlayer()
    {
        if (player == null)
            return false;

        Vector3 origin =
            transform.position +
            Vector3.up * 0.5f;

        Vector3 destination =
            player.position +
            Vector3.up * 0.5f;

        Vector3 direction =
            destination - origin;

        float distance = direction.magnitude;

        // Detection range
        if (distance > detectionRange)
            return false;

        // Field of view
        Vector3 horizontal = direction;
        horizontal.y = 0f;

        if (horizontal.sqrMagnitude > 0.0001f)
        {
            float angle = Vector3.Angle(
                transform.forward,
                horizontal
            );

            if (angle > fieldOfView * 0.5f)
                return false;
        }

        // Cannot see player through maze walls
        if (WallBetween(origin, destination))
            return false;

        return true;
    }

    // =====================================
    // FOOTSTEP HEARING
    // =====================================

    private void HearFootstep(
        Vector3 soundPosition,
        float soundRadius
    )
    {
        if (player == null ||
            hasExploded ||
            !ready)
        {
            return;
        }

        // Visible player takes priority
        if (CanSeePlayer())
            return;

        float distance = Vector3.Distance(
            transform.position,
            soundPosition
        );

        if (distance > soundRadius)
            return;

        BeginInvestigation(soundPosition);

        Debug.Log(
            "Boomer heard footsteps! Investigating..."
        );
    }

    // =====================================
    // CHASE + FAST CHARGE
    // =====================================

    private void ChasePlayer()
    {
        if (player == null)
            return;

        Vector3 difference =
            player.position - transform.position;

        difference.y = 0f;

        float distance = difference.magnitude;

        // Explode only when close and visible
        if (distance <= explosionDistance &&
            !WallBetween(
                transform.position + Vector3.up * 0.5f,
                player.position + Vector3.up * 0.5f
            ))
        {
            Explode();
            return;
        }

        float currentSpeed = chaseSpeed;

        // SPECIAL ABILITY: FAST CHARGE
        if (distance <= chargeRange)
        {
            currentSpeed = chargeSpeed;
        }

        agent.speed = currentSpeed;

        if (Time.time < nextRepathTime)
            return;

        nextRepathTime =
            Time.time +
            Mathf.Max(
                0.1f,
                repathInterval
            );

        // Follow actual maze corridors
        MoveToPosition(
            player.position,
            currentSpeed
        );
    }

    // =====================================
    // INVESTIGATION
    // =====================================

    private void BeginInvestigation(
        Vector3 position
    )
    {
        lastKnownPosition = position;
        investigateTimer = investigateDuration;
        investigatePathNeeded = true;

        ChangeState(AIState.Investigate);
    }

    private void InvestigatePosition()
    {
        investigateTimer -= Time.deltaTime;

        // Return to patrol after investigation
        if (investigateTimer <= 0f)
        {
            investigatePathNeeded = false;

            ChangeState(AIState.Patrol);
            nextPatrolTime = 0f;

            return;
        }

        if (investigatePathNeeded &&
            Time.time >= nextRepathTime)
        {
            nextRepathTime =
                Time.time +
                Mathf.Max(
                    0.1f,
                    repathInterval
                );

            if (MoveToPosition(
                lastKnownPosition,
                investigateSpeed
            ))
            {
                investigatePathNeeded = false;
            }
        }

        // Look around at last known location
        if (!investigatePathNeeded &&
            HasReachedDestination())
        {
            agent.isStopped = true;
            agent.updateRotation = false;

            transform.Rotate(
                0f,
                90f * Time.deltaTime,
                0f
            );
        }
    }

    // =====================================
    // EXPLOSION + PLAYER DAMAGE
    // =====================================

    private void Explode()
    {
        if (hasExploded)
            return;

        hasExploded = true;

        Debug.Log("BOOM! Boomer Exploded!");

        if (player != null)
        {
            float distance = Vector3.Distance(
                transform.position,
                player.position
            );

            bool blocked = WallBetween(
                transform.position + Vector3.up * 0.5f,
                player.position + Vector3.up * 0.5f
            );

            if (distance <= explosionRadius &&
                !blocked)
            {
                PlayerHealth health =
                    player.GetComponent<PlayerHealth>();

                if (health == null)
                {
                    health =
                        player.GetComponentInParent<PlayerHealth>();
                }

                if (health == null)
                {
                    health =
                        player.GetComponentInChildren<PlayerHealth>();
                }

                if (health != null)
                {
                    health.TakeDamage(
                        explosionDamage
                    );
                }
                else
                {
                    Debug.LogWarning(
                        "PlayerHealth component not found!"
                    );
                }
            }
        }

        // Explosion particle effect
        if (explosionEffect != null)
        {
            GameObject effect = Instantiate(
                explosionEffect,
                transform.position,
                Quaternion.identity
            );

            Destroy(effect, 3f);
        }

        Destroy(gameObject);
    }

    // =====================================
    // VISION + PATROL GIZMOS
    // =====================================

    private void OnDrawGizmosSelected()
    {
        Vector3 origin =
            transform.position +
            Vector3.up * 0.5f;

        Vector3 forward = transform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
            return;

        forward.Normalize();

        float halfAngle = fieldOfView * 0.5f;

        Gizmos.color = Color.yellow;

        Vector3 previous =
            origin +
            Quaternion.Euler(
                0f,
                -halfAngle,
                0f
            ) * forward * detectionRange;

        for (int i = 1; i <= 24; i++)
        {
            float angle =
                -halfAngle +
                fieldOfView * i / 24f;

            Vector3 ray =
                Quaternion.Euler(
                    0f,
                    angle,
                    0f
                ) * forward;

            Vector3 next =
                origin + ray * detectionRange;

            Gizmos.DrawLine(previous, next);

            previous = next;
        }

        Gizmos.color = Color.green;

        Gizmos.DrawLine(
            origin,
            origin + forward * detectionRange
        );

        Gizmos.color = Color.cyan;

        Vector3 center = Application.isPlaying
            ? homePosition
            : transform.position;

        Gizmos.DrawWireSphere(
            center,
            patrolRadius
        );

        if (Application.isPlaying &&
            currentState == AIState.Investigate)
        {
            Gizmos.color = Color.red;

            Gizmos.DrawSphere(
                lastKnownPosition,
                0.25f
            );
        }
    }
}
