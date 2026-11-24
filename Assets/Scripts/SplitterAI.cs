using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SplitterAI : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public SplitterAI scoutPrefab;

    [Header("Main Splitter")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;
    public float patrolRadius = 12f;
    public float directionChangeTime = 3f;
    public float detectionRange = 8f;
    [Range(0f, 360f)] public float fieldOfView = 90f;

    [Header("Scouts")]
    public float scoutSpeed = 2.5f;
    public float scoutLifetime = 10f;
    public float scoutDetectionRange = 5f;
    public float stunDuration = 3f;
    public float abilityCooldown = 25f;
    public float firstDeploymentDelay = 3f;

    [Header("Searching")]
    public float investigateDuration = 5f;
    public float scoutPursuitDuration = 12f;
    public float branchSearchDuration = 12f;
    public float junctionTriggerDistance = 1.2f;
    public float arrivalDistance = 0.55f;
    public float repathInterval = 0.3f;
    public float navMeshSpawnSearch = 2.5f;
    public bool logDecisions = true;

    private enum State { Patrol, Chase, Investigate, Junction, Branch, ScoutPursuit }
    private State state = State.Patrol;

    private static readonly Vector2Int[] Directions =
    {
        Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left
    };

    private NavMeshAgent agent;
    private NavMeshPath path;
    private MazeGenerator maze;
    private Transform mazeWalls;
    private bool ready;
    private bool isScout;
    private bool hasPlayerClue;
    private bool needsInvestigationPath;

    private Vector3 homePosition;
    private Vector3 lastKnownPosition;
    private Vector3 junctionDestination;
    private Vector2Int selectedJunction;
    private Vector2Int forbiddenJunction;
    private Vector2Int previousCell;

    private float abilityTimer;
    private float nextPathTime;
    private float nextPatrolTime;
    private float nextJunctionScan;
    private float investigationTimer;
    private float branchTimer;
    private float scoutTimer;
    private float pursuitTimer;
    private float nextSoundPathTime;

    private Action<Vector3> reportToMain;
    private readonly List<GameObject> activeScouts = new List<GameObject>();
    private readonly HashSet<Vector2Int> usedJunctions = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> failedJunctions = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> mainVisited = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> branchVisited = new HashSet<Vector2Int>();

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
        if (agent != null) agent.enabled = false;
        else Debug.LogError(name + ": NavMeshAgent component is missing.");
    }

    private void OnEnable() { Movement.OnPlayerFootstep += HearFootstep; }
    private void OnDisable() { Movement.OnPlayerFootstep -= HearFootstep; }
    private void Start() { abilityTimer = firstDeploymentDelay; }

    private void Update()
    {
        if (!ready)
        {
            InitializeNavMesh();
            return;
        }
        if (!agent.enabled || !agent.isOnNavMesh)
        {
            ready = false;
            return;
        }

        FindPlayer();
        if (isScout)
        {
            UpdateScout();
            return;
        }

        abilityTimer -= Time.deltaTime;
        activeScouts.RemoveAll(scout => scout == null);
        if (maze != null && maze.cellWalls != null && InMaze(CurrentCell()))
            mainVisited.Add(CurrentCell());

        if (CanSeePlayer())
        {
            lastKnownPosition = player.position;
            hasPlayerClue = true;
            ChangeState(State.Chase);
            MoveTo(player.position, chaseSpeed, 1.5f, true);
            return;
        }

        if (state == State.Chase) BeginInvestigation(lastKnownPosition);
        if (state == State.Investigate) Investigate();
        else if (state == State.ScoutPursuit) PursueScoutReport();
        else if (state == State.Junction) GoToJunction();
        else if (state == State.Branch) SearchBranch();
        else if (abilityTimer <= 0f && TryUseCurrentJunction()) return;
        else if (abilityTimer <= 0f && Time.time >= nextJunctionScan && ChooseJunction())
            GoToJunction();
        else Patrol();
    }

    private void FindPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy) return;
        GameObject target = GameObject.FindGameObjectWithTag("Player");
        if (target == null) target = GameObject.Find("Player");
        if (target != null) player = target.transform;
    }

    private void InitializeNavMesh()
    {
        if (agent == null) return;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(transform.position, out hit,
                navMeshSpawnSearch, NavMesh.AllAreas)) return;

        if (agent.enabled) agent.enabled = false;
        transform.position = hit.position + Vector3.up * agent.baseOffset;
        agent.enabled = true;
        if (!agent.isOnNavMesh)
        {
            agent.enabled = false;
            return;
        }

        agent.autoRepath = true;
        agent.autoBraking = true;
        agent.acceleration = 12f;
        agent.angularSpeed = 360f;
        agent.stoppingDistance = isScout ? 0.35f : 0.25f;
        agent.speed = isScout ? scoutSpeed : patrolSpeed;
        maze = FindFirstObjectByType<MazeGenerator>();
        homePosition = agent.nextPosition;
        ready = true;
    }

    private NavMeshQueryFilter AgentFilter()
    {
        return new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
    }

    private bool MoveTo(Vector3 target, float speed, float radius = 1.2f,
        bool throttle = false)
    {
        if (throttle && Time.time < nextPathTime) return true;
        if (throttle) nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
        if (!agent.enabled || !agent.isOnNavMesh) return false;

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(target, out hit, radius, AgentFilter())) return false;
        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;

        agent.speed = speed;
        agent.isStopped = false;
        agent.updateRotation = true;
        return agent.SetDestination(hit.position);
    }

    private bool Arrived()
    {
        if (agent.pathPending) return false;
        return !agent.hasPath ||
            (!float.IsInfinity(agent.remainingDistance) &&
             agent.remainingDistance <= Mathf.Max(arrivalDistance, agent.stoppingDistance + 0.1f));
    }

    private void ChangeState(State newState)
    {
        if (state == newState) return;
        state = newState;
        nextPathTime = 0f;
        if (!ready || !agent.isOnNavMesh) return;
        agent.isStopped = false;
        agent.updateRotation = true;
        agent.ResetPath();
    }

    private void Patrol()
    {
        if (!Arrived() || Time.time < nextPatrolTime) return;
        for (int i = 0; i < 25; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 destination = homePosition + new Vector3(offset.x, 0f, offset.y);
            if ((destination - agent.nextPosition).sqrMagnitude < 4f) continue;
            if (!MoveTo(destination, patrolSpeed)) continue;
            nextPatrolTime = Time.time + Mathf.Max(0.2f, directionChangeTime);
            return;
        }
        nextPatrolTime = Time.time + 1f;
    }

    private void BeginInvestigation(Vector3 position)
    {
        lastKnownPosition = position;
        investigationTimer = investigateDuration;
        needsInvestigationPath = true;
        ChangeState(State.Investigate);
    }

    private void Investigate()
    {
        investigationTimer -= Time.deltaTime;
        if (investigationTimer <= 0f)
        {
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
            return;
        }
        if (needsInvestigationPath && Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
            if (MoveTo(lastKnownPosition, chaseSpeed, 2.5f)) needsInvestigationPath = false;
        }
        if (!needsInvestigationPath && Arrived())
        {
            agent.isStopped = true;
            agent.updateRotation = false;
            transform.Rotate(Vector3.up, 90f * Time.deltaTime);
        }
    }

    private void HearFootstep(Vector3 position, float radius)
    {
        if (isScout || !ready || player == null || state == State.ScoutPursuit) return;
        if (Vector3.Distance(transform.position, position) > radius || CanSeePlayer())
            return;

        hasPlayerClue = true;
        bool changed = Vector3.Distance(lastKnownPosition, position) > 0.9f;
        bool newInvestigation = state != State.Investigate;
        if (newInvestigation) BeginInvestigation(position);
        else
        {
            lastKnownPosition = position;
            investigationTimer = investigateDuration;
        }
        if ((changed && Time.time >= nextSoundPathTime) || needsInvestigationPath)
        {
            nextSoundPathTime = Time.time + 0.35f;
            needsInvestigationPath = !MoveTo(position, chaseSpeed, 2.5f);
        }
        if (logDecisions && newInvestigation)
            Debug.Log(name + " heard footsteps. Investigating.");
    }

    private void PursueScoutReport()
    {
        pursuitTimer -= Time.deltaTime;
        if (pursuitTimer <= 0f ||
            (Vector3.Distance(agent.nextPosition, lastKnownPosition) <= 1f && Arrived()))
        {
            BeginInvestigation(lastKnownPosition);
            return;
        }
        MoveTo(lastKnownPosition, chaseSpeed, 2.5f, true);
    }

    private bool WallBetween(Vector3 from, Vector3 to)
    {
        if (mazeWalls == null)
        {
            GameObject walls = GameObject.Find("MazeWalls");
            if (walls != null) mazeWalls = walls.transform;
        }
        if (mazeWalls == null) return false;

        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 0.001f) return false;
        foreach (RaycastHit hit in Physics.RaycastAll(from, direction / distance,
                     distance, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && hit.collider.transform.IsChildOf(mazeWalls))
                return true;
        }
        return false;
    }

    private bool CanSeePlayer()
    {
        if (player == null) return false;
        Vector3 from = transform.position + Vector3.up * 0.5f;
        Vector3 to = player.position + Vector3.up * 0.5f;
        Vector3 direction = to - from;
        if (direction.magnitude > detectionRange) return false;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f &&
            Vector3.Angle(transform.forward, direction) > fieldOfView * 0.5f)
            return false;
        return !WallBetween(from, to);
    }

    private bool ScoutSeesPlayer()
    {
        if (player == null) return false;
        Vector3 from = transform.position + Vector3.up * 0.65f;
        Vector3 to = player.position + Vector3.up * 0.65f;
        if (!WallBetween(from, to)) return true;
        return !WallBetween(from + Vector3.up * 0.4f, to + Vector3.up * 0.5f);
    }

    private bool InMaze(Vector2Int cell)
    {
        return maze != null && cell.x >= 0 && cell.y >= 0 &&
            cell.x < maze.width && cell.y < maze.height;
    }

    private Vector2Int CurrentCell()
    {
        Vector3 p = agent.nextPosition;
        return new Vector2Int(Mathf.RoundToInt(p.x / maze.cellSize),
            Mathf.RoundToInt(p.z / maze.cellSize));
    }

    private Vector3 CellCenter(Vector2Int cell)
    {
        return new Vector3(cell.x * maze.cellSize, agent.nextPosition.y,
            cell.y * maze.cellSize);
    }

    private List<int> OpenDirections(Vector2Int cell)
    {
        List<int> result = new List<int>();
        if (maze == null || maze.cellWalls == null || !InMaze(cell)) return result;
        for (int d = 0; d < 4; d++)
        {
            if (InMaze(cell + Directions[d]) && !maze.cellWalls[cell.x, cell.y, d])
                result.Add(d);
        }
        return result;
    }

    private bool SampleCell(Vector2Int cell, out Vector3 point)
    {
        point = Vector3.zero;
        if (!InMaze(cell)) return false;
        Vector3 center = CellCenter(cell);
        float offset = Mathf.Min(maze.cellSize * 0.18f, 0.8f);
        float radius = Mathf.Clamp(maze.cellSize * 0.44f, 0.75f, 2f);
        float limit = maze.cellSize * 0.48f;
        Vector3[] probes =
        {
            center, center + Vector3.right * offset, center + Vector3.left * offset,
            center + Vector3.forward * offset, center + Vector3.back * offset
        };
        foreach (Vector3 probe in probes)
        {
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(probe, out hit, radius, AgentFilter())) continue;
            if (Mathf.Abs(hit.position.x - center.x) > limit ||
                Mathf.Abs(hit.position.z - center.z) > limit) continue;
            point = hit.position;
            return true;
        }
        return false;
    }

    private bool TryUseCurrentJunction()
    {
        if (maze == null || maze.cellWalls == null || scoutPrefab == null) return false;
        Vector2Int cell = CurrentCell();
        if (!InMaze(cell) || usedJunctions.Contains(cell) || failedJunctions.Contains(cell))
            return false;
        int routes = OpenDirections(cell).Count;
        if (routes < 2 || routes - 1 > 3 - activeScouts.Count) return false;

        Vector3 center;
        if (!SampleCell(cell, out center)) return false;
        if (Vector2.Distance(new Vector2(agent.nextPosition.x, agent.nextPosition.z),
            new Vector2(center.x, center.z)) > Mathf.Max(1.2f, junctionTriggerDistance))
            return false;

        selectedJunction = cell;
        junctionDestination = center;
        if (DeployScouts()) return true;
        failedJunctions.Add(cell);
        return false;
    }

    private bool ChooseJunction()
    {
        nextJunctionScan = Time.time + 3f;
        if (maze == null || maze.cellWalls == null || scoutPrefab == null) return false;
        int freeSlots = 3 - activeScouts.Count;
        if (freeSlots <= 0) return false;
        NavMeshAgent scoutAgent = scoutPrefab.GetComponent<NavMeshAgent>();
        if (scoutAgent == null || scoutAgent.agentTypeID != agent.agentTypeID)
            return false;

        float bestScore = float.NegativeInfinity;
        Vector2Int bestCell = Vector2Int.zero;
        Vector3 bestPoint = Vector3.zero;
        bool found = false;

        for (int x = 0; x < maze.width; x++)
        for (int z = 0; z < maze.height; z++)
        {
            Vector2Int cell = new Vector2Int(x, z);
            if (usedJunctions.Contains(cell) || failedJunctions.Contains(cell)) continue;
            int routes = OpenDirections(cell).Count;
            if (routes < 2 || routes - 1 > freeSlots) continue;

            Vector3 point;
            if (!SampleCell(cell, out point)) continue;
            NavMeshPath candidatePath = new NavMeshPath();
            if (!agent.CalculatePath(point, candidatePath) ||
                candidatePath.status != NavMeshPathStatus.PathComplete) continue;

            float distance = 0f;
            for (int i = 1; i < candidatePath.corners.Length; i++)
                distance += Vector3.Distance(candidatePath.corners[i - 1],
                    candidatePath.corners[i]);
            float score = -distance + routes * 0.3f * maze.cellSize;
            if (mainVisited.Contains(cell)) score -= maze.cellSize;
            if (score <= bestScore) continue;
            bestScore = score;
            bestCell = cell;
            bestPoint = point;
            found = true;
        }

        if (!found) return false;
        selectedJunction = bestCell;
        junctionDestination = bestPoint;
        ChangeState(State.Junction);
        if (MoveTo(bestPoint, patrolSpeed, 0.8f)) return true;
        failedJunctions.Add(bestCell);
        ChangeState(State.Patrol);
        return false;
    }

    private void GoToJunction()
    {
        float distance = Vector2.Distance(new Vector2(agent.nextPosition.x, agent.nextPosition.z),
            new Vector2(junctionDestination.x, junctionDestination.z));
        if (distance <= Mathf.Max(1.3f, junctionTriggerDistance) || Arrived())
        {
            if (DeployScouts()) return;
            failedJunctions.Add(selectedJunction);
            abilityTimer = 3f;
            ChangeState(State.Patrol);
            return;
        }
        if (Time.time >= nextPathTime && !MoveTo(junctionDestination, patrolSpeed, 0.8f, true))
        {
            failedJunctions.Add(selectedJunction);
            ChangeState(State.Patrol);
        }
    }

    private bool BranchSpawnPosition(int direction, out Vector3 position)
    {
        position = Vector3.zero;
        Vector2Int cell = selectedJunction + Directions[direction];
        if (!SampleCell(cell, out position)) return false;
        if (WallBetween(junctionDestination + Vector3.up * 0.6f,
            position + Vector3.up * 0.6f)) return false;
        return agent.CalculatePath(position, path) &&
            path.status == NavMeshPathStatus.PathComplete;
    }

    private int MainBranch(List<int> routes)
    {
        int best = routes[0];
        float bestScore = float.NegativeInfinity;
        Vector3 clue = lastKnownPosition - junctionDestination;
        clue.y = 0f;
        if (clue.sqrMagnitude > 0.001f) clue.Normalize();
        foreach (int direction in routes)
        {
            Vector3 vector = new Vector3(Directions[direction].x, 0f, Directions[direction].y);
            Vector2Int next = selectedJunction + Directions[direction];
            float score = Vector3.Dot(transform.forward, vector) * 1.4f;
            if (hasPlayerClue) score += Vector3.Dot(clue, vector) * 2.5f;
            if (!mainVisited.Contains(next)) score += 1f;
            score += UnityEngine.Random.Range(0f, 0.15f);
            if (score > bestScore) { bestScore = score; best = direction; }
        }
        return best;
    }

    private bool DeployScouts()
    {
        if (maze == null || scoutPrefab == null) return false;
        List<int> routes = OpenDirections(selectedJunction);
        int count = routes.Count - 1;
        if (count < 1 || count > 3 || count > 3 - activeScouts.Count) return false;
        NavMeshAgent scoutAgent = scoutPrefab.GetComponent<NavMeshAgent>();
        if (scoutAgent == null || scoutAgent.agentTypeID != agent.agentTypeID)
            return false;

        Vector3[] positions = new Vector3[4];
        foreach (int direction in routes)
            if (!BranchSpawnPosition(direction, out positions[direction])) return false;

        int main = MainBranch(routes);
        ChangeState(State.Branch);
        if (!MoveTo(positions[main], patrolSpeed, 0.7f))
        {
            ChangeState(State.Patrol);
            return false;
        }

        forbiddenJunction = selectedJunction;
        previousCell = selectedJunction;
        branchVisited.Clear();
        branchVisited.Add(selectedJunction);
        branchTimer = branchSearchDuration;
        abilityTimer = abilityCooldown;
        usedJunctions.Add(selectedJunction);

        foreach (int direction in routes)
        {
            if (direction == main) continue;
            Vector3 forward = new Vector3(Directions[direction].x, 0f, Directions[direction].y);
            SplitterAI scout = Instantiate(scoutPrefab,
                positions[direction] + Vector3.up * scoutAgent.baseOffset,
                Quaternion.LookRotation(forward));
            scout.SetupScout(player, OnScoutFound, selectedJunction);
            if (!scout.gameObject.activeSelf) scout.gameObject.SetActive(true);
            activeScouts.Add(scout.gameObject);
        }
        if (logDecisions)
            Debug.Log(name + " deployed " + count + " scout(s) at " + selectedJunction);
        return true;
    }

    private bool ExploreBranch(float speed)
    {
        if (maze == null || maze.cellWalls == null) return false;
        Vector2Int current = CurrentCell();
        if (!InMaze(current)) return false;
        branchVisited.Add(current);
        List<Vector2Int> choices = new List<Vector2Int>();
        foreach (int direction in OpenDirections(current))
        {
            Vector2Int next = current + Directions[direction];
            if (next != forbiddenJunction) choices.Add(next);
        }
        choices.Sort((a, b) =>
        {
            int aScore = (branchVisited.Contains(a) ? 2 : 0) + (a == previousCell ? 1 : 0);
            int bScore = (branchVisited.Contains(b) ? 2 : 0) + (b == previousCell ? 1 : 0);
            return aScore.CompareTo(bScore);
        });
        foreach (Vector2Int next in choices)
        {
            Vector3 point;
            if (!SampleCell(next, out point)) continue;
            if (NavMesh.Raycast(agent.nextPosition, point, out _, AgentFilter())) continue;
            if (!MoveTo(point, speed, 0.5f)) continue;
            previousCell = current;
            return true;
        }
        return false;
    }

    private void SearchBranch()
    {
        branchTimer -= Time.deltaTime;
        if (branchTimer <= 0f)
        {
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
            return;
        }
        if (!Arrived() || Time.time < nextPatrolTime) return;
        if (!ExploreBranch(patrolSpeed))
        {
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
        }
        else nextPatrolTime = Time.time + 0.3f;
    }

    private void SetupScout(Transform target, Action<Vector3> callback, Vector2Int junction)
    {
        isScout = true;
        player = target;
        reportToMain = callback;
        scoutTimer = 0f;
        forbiddenJunction = junction;
        previousCell = junction;
        branchVisited.Clear();
        branchVisited.Add(junction);
    }

    private void OnScoutFound(Vector3 position)
    {
        if (isScout) return;
        hasPlayerClue = true;
        lastKnownPosition = position;
        pursuitTimer = Mathf.Max(1f, scoutPursuitDuration);
        ChangeState(State.ScoutPursuit);
        MoveTo(position, chaseSpeed, 2.5f);
        if (logDecisions) Debug.Log(name + " received scout's player location.");
    }

    private Movement GetPlayerMovement()
    {
        if (player == null) return null;
        Movement movement = player.GetComponent<Movement>();
        if (movement == null) movement = player.GetComponentInParent<Movement>();
        if (movement == null) movement = player.GetComponentInChildren<Movement>(true);
        if (movement != null) return movement;
        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged == null ? null : tagged.GetComponentInChildren<Movement>(true);
    }

    private void UpdateScout()
    {
        scoutTimer += Time.deltaTime;
        if (scoutTimer >= scoutLifetime) { Destroy(gameObject); return; }
        if (player != null)
        {
            Vector3 delta = player.position - transform.position;
            delta.y = 0f;
            if (delta.magnitude <= scoutDetectionRange && ScoutSeesPlayer())
            {
                Movement movement = GetPlayerMovement();
                if (movement != null)
                {
                    Vector3 reportedPosition = player.position;
                    movement.ApplyStun(stunDuration);
                    reportToMain?.Invoke(reportedPosition);
                    if (logDecisions) Debug.Log(name + " stunned player for " + stunDuration + "s.");
                    Destroy(gameObject);
                    return;
                }
                if (logDecisions) Debug.LogWarning(name + ": Player Movement script not found.");
            }
        }
        if (!Arrived() || Time.time < nextPatrolTime) return;
        if (!ExploreBranch(scoutSpeed))
        {
            agent.isStopped = true;
            agent.updateRotation = false;
            transform.Rotate(Vector3.up, 90f * Time.deltaTime);
            nextPatrolTime = Time.time + 1f;
        }
        else nextPatrolTime = Time.time + 0.3f;
    }
}
