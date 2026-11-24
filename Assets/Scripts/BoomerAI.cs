using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class BoomerAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public GameObject explosionEffect;

    [Header("Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;
    public float chargeSpeed = 9f;
    public float investigateSpeed = 2.5f;
    public float patrolRadius = 12f;

    [Header("Detection")]
    public float detectionRange = 12f;
    [Range(0f, 360f)] public float fieldOfView = 90f;
    public float investigateDuration = 6f;
    public float chargeRange = 7f;

    [Header("Explosion")]
    public float explosionDistance = 2.2f;
    public float explosionRadius = 3f;
    public int explosionDamage = 30;

    [Header("NavMesh")]
    public float navMeshSpawnSearch = 3f;
    public float repathInterval = 0.25f;
    public bool showLogs = true;

    private enum State { Patrol, Chase, Investigate }
    private State state = State.Patrol;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right,
        Vector2Int.down, Vector2Int.left
    };

    private NavMeshAgent agent;
    private NavMeshPath path;
    private MazeGenerator maze;
    private Transform mazeWalls;
    private Vector3 patrolTarget;
    private Vector3 lastKnownPosition;
    private Vector2Int previousCell = new Vector2Int(-1, -1);

    private float nextInitTime;
    private float nextPathTime;
    private float nextPatrolTime;
    private float patrolStartTime;
    private float investigationEndTime;
    private float lastSeenTime;
    private bool ready;
    private bool exploded;
    private bool hasPatrolTarget;
    private bool needsInvestigationPath;
    private bool warnedNoMesh;
    private bool wasCharging;
    private float nextWarningTime;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        agent.enabled = false;
    }

    private void OnEnable()
    {
        Movement.OnPlayerFootstep += HearFootstep;
    }

    private void OnDisable()
    {
        Movement.OnPlayerFootstep -= HearFootstep;
    }

    private void Update()
    {
        if (exploded) return;

        if (!ready)
        {
            ConnectToNavMesh();
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            ready = false;
            if (agent.enabled) agent.enabled = false;
            return;
        }

        FindPlayer();

        // Explosion works at close range even if the player is behind Boomer.
        if (player != null && HorizontalDistance(player.position) <= explosionDistance
            && !WallBetweenPlayer())
        {
            Explode();
            return;
        }

        if (CanSeePlayer())
        {
            lastKnownPosition = player.position;
            lastSeenTime = Time.time;
            ChangeState(State.Chase);
        }
        else if (state == State.Chase && Time.time - lastSeenTime > 1f)
        {
            StartInvestigation(lastKnownPosition);
        }

        if (state == State.Chase) Chase();
        else if (state == State.Investigate) Investigate();
        else Patrol();
    }

    private void FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy &&
            player.GetComponentInParent<Movement>() != null) return;

        if (maze == null) maze = FindFirstObjectByType<MazeGenerator>();
        if (maze != null && maze.player != null)
        {
            player = maze.player.transform;
            return;
        }

        Movement movement = FindFirstObjectByType<Movement>();
        if (movement != null) player = movement.transform;
    }

    private void ConnectToNavMesh()
    {
        if (Time.time < nextInitTime) return;
        nextInitTime = Time.time + 0.5f;

        if (maze == null) maze = FindFirstObjectByType<MazeGenerator>();

        NavMeshHit hit;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(transform.position, out hit,
            navMeshSpawnSearch, filter))
        {
            if (!warnedNoMesh && Time.time > 3f)
            {
                Debug.LogWarning(name + ": No NavMesh nearby. Check Agent Type and maze build.");
                warnedNoMesh = true;
            }
            return;
        }

        agent.enabled = false;
        transform.position = hit.position + Vector3.up * agent.baseOffset;
        agent.enabled = true;
        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            return;
        }

        agent.speed = patrolSpeed;
        agent.acceleration = 25f;
        agent.angularSpeed = 720f;
        agent.stoppingDistance = 0.15f;
        agent.autoRepath = true;
        agent.autoBraking = true;
        ready = true;
        nextPatrolTime = 0f;
        FindPlayer();
        Log("connected to NavMesh; starting patrol");
    }

    private bool MoveTo(Vector3 position, float speed, float searchRadius = 1.5f)
    {
        if (!ready || !agent.enabled || !agent.isOnNavMesh) return false;

        NavMeshHit hit;
        NavMeshQueryFilter filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };

        if (!NavMesh.SamplePosition(position, out hit, searchRadius, filter))
            return false;
        if (Mathf.Abs(hit.position.y - agent.nextPosition.y) > 1.5f)
            return false;
        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete)
            return false;

        agent.speed = speed;
        agent.isStopped = false;
        agent.updateRotation = true;
        if (!agent.SetDestination(hit.position)) return false;
        patrolTarget = hit.position;
        return true;
    }

    private void ChangeState(State newState)
    {
        if (state == newState) return;
        state = newState;
        if (newState != State.Chase) wasCharging = false;
        hasPatrolTarget = false;
        nextPathTime = 0f;
        agent.isStopped = false;
        agent.updateRotation = true;
        agent.ResetPath();
        Log("State: " + state);
    }

    private Vector2Int CurrentCell()
    {
        return new Vector2Int(
            Mathf.RoundToInt(agent.nextPosition.x / maze.cellSize),
            Mathf.RoundToInt(agent.nextPosition.z / maze.cellSize));
    }

    private bool InMaze(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 &&
            cell.x < maze.width && cell.y < maze.height;
    }

    private bool ChooseCorridor()
    {
        if (maze == null || maze.cellWalls == null || maze.cellSize <= 0f)
            return false;

        Vector2Int cell = CurrentCell();
        if (!InMaze(cell)) return false;

        int randomStart = Random.Range(0, 4);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < 4; i++)
            {
                int direction = (randomStart + i) % 4;
                if (maze.cellWalls[cell.x, cell.y, direction]) continue;

                Vector2Int next = cell + Directions[direction];
                if (!InMaze(next)) continue;
                if (pass == 0 && next == previousCell) continue;

                Vector3 destination = new Vector3(
                    next.x * maze.cellSize, agent.nextPosition.y,
                    next.y * maze.cellSize);

                if (!MoveTo(destination, patrolSpeed, 1.5f)) continue;

                previousCell = cell;
                hasPatrolTarget = true;
                patrolStartTime = Time.time;
                Log("Patrol: " + cell + " -> " + next);
                return true;
            }
        }
        return false;
    }

    private void Patrol()
    {
        if (hasPatrolTarget)
        {
            bool arrived = !agent.pathPending &&
                HorizontalDistance(patrolTarget) <= 0.6f;
            bool failed = !agent.pathPending && !agent.hasPath &&
                Time.time - patrolStartTime > 0.5f;
            bool timedOut = Time.time - patrolStartTime > 8f;

            if (!arrived && !failed && !timedOut) return;

            hasPatrolTarget = false;
            nextPatrolTime = Time.time + 0.15f;
            if (!arrived) agent.ResetPath();
        }

        if (Time.time < nextPatrolTime) return;
        if (ChooseCorridor()) return;

        // Works even when the object is used outside MazeGenerator.
        for (int i = 0; i < 30; i++)
        {
            Vector2 offset = Random.insideUnitCircle * patrolRadius;
            Vector3 target = agent.nextPosition + new Vector3(offset.x, 0f, offset.y);
            if ((target - agent.nextPosition).sqrMagnitude < 4f) continue;
            if (!MoveTo(target, patrolSpeed, 2f)) continue;
            hasPatrolTarget = true;
            patrolStartTime = Time.time;
            Log("Patrol: fallback waypoint selected");
            return;
        }

        nextPatrolTime = Time.time + 0.5f;
        if (Time.time >= nextWarningTime)
        {
            Debug.LogWarning(name + ": Cannot find a reachable patrol point on NavMesh.");
            nextWarningTime = Time.time + 3f;
        }
    }

    private void Chase()
    {
        if (player == null) return;

        Vector3 target = CanSeePlayer() ? player.position : lastKnownPosition;
        float distance = HorizontalDistance(target);
        bool charging = distance <= chargeRange;
        float speed = charging ? chargeSpeed : chaseSpeed;
        agent.speed = speed;
        agent.acceleration = charging ? 45f : 25f;
        if (charging && !wasCharging) Log("FAST CHARGE started, speed " + chargeSpeed);
        wasCharging = charging;

        if (Time.time < nextPathTime) return;
        nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
        if (!MoveTo(target, speed, 2f) && Time.time >= nextWarningTime)
        {
            Debug.LogWarning(name + ": Could not calculate a path to Player.");
            nextWarningTime = Time.time + 3f;
        }
    }

    private void StartInvestigation(Vector3 position)
    {
        ChangeState(State.Investigate);
        lastKnownPosition = position;
        investigationEndTime = Time.time + investigateDuration;
        needsInvestigationPath = true;
        nextPathTime = 0f;
    }

    private void Investigate()
    {
        if (Time.time >= investigationEndTime)
        {
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
            return;
        }

        if (needsInvestigationPath && Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
            if (MoveTo(lastKnownPosition, investigateSpeed, 2f))
                needsInvestigationPath = false;
        }

        if (!needsInvestigationPath && !agent.pathPending &&
            HorizontalDistance(patrolTarget) <= 0.7f)
        {
            agent.isStopped = true;
            agent.updateRotation = false;
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        }
    }

    private void HearFootstep(Vector3 position, float radius)
    {
        if (!ready || exploded || CanSeePlayer()) return;
        if (HorizontalDistance(position) > radius) return;

        if (state != State.Investigate)
        {
            StartInvestigation(position);
            Log("heard footsteps; investigating");
        }
        else
        {
            if ((lastKnownPosition - position).sqrMagnitude > 1f)
                needsInvestigationPath = true;
            lastKnownPosition = position;
            investigationEndTime = Time.time + investigateDuration;
        }
    }

    private float HorizontalDistance(Vector3 position)
    {
        Vector3 offset = position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool CanSeePlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        if (offset.magnitude > detectionRange) return false;
        if (offset.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, offset) > fieldOfView * 0.5f)
            return false;
        return !WallBetweenPlayer();
    }

    private bool WallBetweenPlayer()
    {
        return player != null && WallBetween(
            transform.position + Vector3.up * 0.7f,
            player.position + Vector3.up * 0.7f);
    }

    private bool WallBetween(Vector3 start, Vector3 end)
    {
        if (mazeWalls == null)
        {
            GameObject walls = GameObject.Find("MazeWalls");
            if (walls != null) mazeWalls = walls.transform;
        }
        if (mazeWalls == null) return false;

        Vector3 direction = end - start;
        if (direction.sqrMagnitude < 0.0001f) return false;

        RaycastHit[] hits = Physics.RaycastAll(start, direction.normalized,
            direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null &&
                hit.collider.transform.IsChildOf(mazeWalls)) return true;
        }
        return false;
    }

    private void Explode()
    {
        if (exploded) return;
        exploded = true;
        Debug.Log("BOOM! Boomer Exploded!");

        if (player != null && HorizontalDistance(player.position) <= explosionRadius
            && !WallBetweenPlayer())
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health == null) health = player.GetComponentInParent<PlayerHealth>();
            if (health == null) health = player.GetComponentInChildren<PlayerHealth>();
            if (health != null) health.TakeDamage(explosionDamage);
            else Debug.LogWarning("Boomer: PlayerHealth component not found.");
        }

        if (explosionEffect != null)
        {
            GameObject effect = Instantiate(explosionEffect,
                transform.position + Vector3.up * 0.7f, Quaternion.identity);
            effect.SetActive(true);
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                particles.Play(true);
            Destroy(effect, 3f);
        }
        else Debug.LogWarning("Boomer: Explosion Effect prefab is not assigned.");

        Destroy(gameObject);
    }

    private void Log(string message)
    {
        if (showLogs) Debug.Log(name + ": " + message);
    }
}
