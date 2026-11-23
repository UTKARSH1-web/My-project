using UnityEngine;
using UnityEngine.AI;
using System;
using System.Collections.Generic;

public class SplitterAI : MonoBehaviour
{
    [Header("References")]
    public Transform point1;
    public Transform point2;
    public Transform player;
    public SplitterAI scoutPrefab;

    [Header("Main Zombie Movement")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;
    public float detectionRange = 8f;
    public float patrolRadius = 12f;
    public float directionChangeTime = 3f;

    [Header("Vision")]
    [Range(0f, 360f)] public float fieldOfView = 90f;

    [Header("Scout Ability")]
    public float scoutSpeed = 2.5f;
    public float scoutLifetime = 10f;
    public float scoutDetectionRange = 5f;
    public float scoutStunRange = 1.5f;
    public float stunDuration = 3f;
    public float abilityCooldown = 25f;
    public float firstDeploymentDelay = 3f;

    [Header("Investigation")]
    public float investigateDuration = 5f;

    [Header("Pathfinding")]
    public float repathInterval = 0.3f;
    public float arrivalDistance = 0.55f;
    public float navMeshSpawnSearch = 2.5f;

    [Header("Adaptive Branch Search")]
    public float junctionTriggerDistance = 1.2f;
    public float junctionScanDistance = 5f;
    public float branchSearchDuration = 12f;
    public bool logDecisions = true;

    private enum State { Patrol, Chase, Investigate, GoingToJunction, BranchSearch }
    private State state = State.Patrol;

    // MazeGenerator directions: 0 North, 1 East, 2 South, 3 West
    private static readonly Vector2Int[] CellDirections =
    {
        new Vector2Int(0, 1),
        new Vector2Int(1, 0),
        new Vector2Int(0, -1),
        new Vector2Int(-1, 0)
    };

    private NavMeshAgent agent;
    private NavMeshPath testPath;
    private MazeGenerator maze;
    private Transform mazeWalls;
    private bool ready;
    private bool isScout;
    private bool scoutFoundPlayer;
    private bool scoutHasStunned;
    private bool investigationNeedsPath;

    private Vector3 homePosition;
    private Vector3 lastKnownPosition;
    private float abilityTimer;
    private float investigationTimer;
    private float scoutTimer;
    private float nextPathTime;
    private float nextPatrolTime;
    private float nextInitializationAttempt;
    private float nextJunctionSearchTime;
    private float branchTimer;
    private bool warnedNoJunction;
    private bool hasPlayerClue;
    private readonly HashSet<Vector2Int> usedJunctions =
        new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> failedJunctions =
        new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> mainVisitedCells =
        new HashSet<Vector2Int>();

    private Vector2Int selectedJunction;
    private Vector3 junctionDestination;
    private Vector2Int forbiddenJunction;
    private Vector2Int previousCell;
    private readonly HashSet<Vector2Int> visitedBranchCells =
        new HashSet<Vector2Int>();
    private Action<Vector3> reportToMain;
    private readonly List<GameObject> activeScouts =
        new List<GameObject>();

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        testPath = new NavMeshPath();
        if (agent != null) agent.enabled = false; // Wait for runtime NavMesh
        else Debug.LogError(name + ": Add a NavMeshAgent component.");
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
        abilityTimer = firstDeploymentDelay;
    }

    private void Update()
    {
        if (!ready)
        {
            TryInitializeAgent();
            return;
        }
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            ready = false;
            return;
        }

        if (isScout)
        {
            UpdateScout();
            return;
        }

        abilityTimer -= Time.deltaTime;
        activeScouts.RemoveAll(s => s == null);

        if (maze != null && maze.cellWalls != null && maze.cellSize > 0f)
        {
            Vector2Int cell = CurrentCell();
            if (CellExists(cell)) mainVisitedCells.Add(cell);
        }

        if (abilityTimer <= 0f &&
            state != State.GoingToJunction &&
            state != State.BranchSearch &&
            TryDeployAtCurrentJunction())
        {
  
        }

        if (CanSeePlayer())
        {
            lastKnownPosition = player.position;
            hasPlayerClue = true;
            investigationTimer = investigateDuration;
            ChangeState(State.Chase);
            ChasePlayer();
        }
        else if (state == State.Chase)
        {
            BeginInvestigation(lastKnownPosition);
            Investigate();
        }
        else if (state == State.Investigate)
        {
            Investigate();
        }
        else if (state == State.GoingToJunction)
        {
            GoToJunction();
        }
        else if (state == State.BranchSearch)
        {
            SearchOwnBranch();
        }
        else
        {
            
            if (abilityTimer <= 0f &&
                Time.time >= nextJunctionSearchTime &&
                TryChooseUsefulJunction())
            {
                GoToJunction();
            }
            else
            {
                Patrol();
            }
        }
    }

    private void TryInitializeAgent()
    {
        if (agent == null || Time.time < nextInitializationAttempt) return;
        nextInitializationAttempt = Time.time + 0.5f;

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

        agent.autoBraking = true;
        agent.autoRepath = true;
        agent.angularSpeed = 360f;
        agent.acceleration = 12f;
        agent.stoppingDistance = isScout ? 0.35f : 0.25f;
        agent.speed = isScout ? scoutSpeed : patrolSpeed;
        homePosition = agent.nextPosition;
        nextPatrolTime = 0f;
        nextPathTime = 0f;
        ready = true;

        if (maze == null) maze = FindObjectOfType<MazeGenerator>();
        Debug.Log(name + " connected to NavMesh.");
        if (!isScout && logDecisions && maze != null && maze.cellWalls != null)
        {
            int two = 0, three = 0, four = 0;
            for (int x = 0; x < maze.width; x++)
                for (int z = 0; z < maze.height; z++)
                {
                    int routes = OpenDirections(new Vector2Int(x, z)).Count;
                    if (routes == 2) two++;
                    else if (routes == 3) three++;
                    else if (routes == 4) four++;
                }
            Debug.Log("Splitter maze junction count: 2-route=" + two +
                ", 3-route=" + three + ", 4-route=" + four);
        }
    }

    private void ChangeState(State nextState)
    {
        if (state == nextState) return;
        state = nextState;
        nextPathTime = 0f;
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.updateRotation = true;
            agent.ResetPath();
        }
    }

    private bool SetReachableDestination(Vector3 position, float speed,
        float sampleRadius = 1.2f)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(position, out hit,
                sampleRadius, agent.areaMask)) return false;
        if (!agent.CalculatePath(hit.position, testPath) ||
            testPath.status != NavMeshPathStatus.PathComplete) return false;

        agent.speed = speed;
        agent.isStopped = false;
        agent.updateRotation = true;
        return agent.SetDestination(hit.position);
    }

    private bool Arrived()
    {
        if (agent.pathPending) return false;
        if (!agent.hasPath) return true;
        return !float.IsInfinity(agent.remainingDistance) &&
            agent.remainingDistance <= Mathf.Max(
                arrivalDistance, agent.stoppingDistance + 0.1f);
    }

    private void Patrol()
    {
        agent.speed = patrolSpeed;
        agent.isStopped = false;
        agent.updateRotation = true;
        if (!Arrived() || Time.time < nextPatrolTime) return;

        for (int i = 0; i < 25; i++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * patrolRadius;
            Vector3 candidate = homePosition + new Vector3(offset.x, 0f, offset.y);
            if ((candidate - agent.nextPosition).sqrMagnitude < 4f) continue;
            if (SetReachableDestination(candidate, patrolSpeed))
            {
                nextPatrolTime = Time.time +
                    Mathf.Max(0.2f, directionChangeTime);
                return;
            }
        }
        nextPatrolTime = Time.time + 1f;
    }

    private void ChasePlayer()
    {
        if (player == null || Time.time < nextPathTime) return;
        nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
        SetReachableDestination(player.position, chaseSpeed, 1.5f);
    }

    private void BeginInvestigation(Vector3 position)
    {
        lastKnownPosition = position;
        investigationTimer = investigateDuration;
        investigationNeedsPath = true;
        ChangeState(State.Investigate);
    }

    private void Investigate()
    {
        investigationTimer -= Time.deltaTime;
        if (investigationTimer <= 0f)
        {
            investigationNeedsPath = false;
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
            return;
        }
        if (investigationNeedsPath && Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
            if (SetReachableDestination(lastKnownPosition, chaseSpeed, 1.5f))
                investigationNeedsPath = false;
        }
        if (!investigationNeedsPath && Arrived())
        {
            agent.isStopped = true;
            agent.updateRotation = false;
            transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        }
    }

    private void HearFootstep(Vector3 position, float soundRadius)
    {
        if (isScout || player == null || !ready || CanSeePlayer()) return;
        if (Vector3.Distance(transform.position, position) > soundRadius) return;
        hasPlayerClue = true;
        BeginInvestigation(position);
        Debug.Log("Splitter heard footsteps. Investigating!");
    }

    private bool WallBetween(Vector3 start, Vector3 end)
    {
        if (mazeWalls == null)
        {
            GameObject walls = GameObject.Find("MazeWalls");
            if (walls != null) mazeWalls = walls.transform;
        }
        if (mazeWalls == null) return false;

        Vector3 difference = end - start;
        float distance = difference.magnitude;
        if (distance < 0.001f) return false;
        RaycastHit[] hits = Physics.RaycastAll(start, difference / distance,
            distance, ~0, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null &&
                hit.collider.transform.IsChildOf(mazeWalls)) return true;
        }
        return false;
    }

    private bool CanSeePlayer()
    {
        if (player == null) return false;
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 target = player.position + Vector3.up * 0.5f;
        Vector3 toPlayer = target - origin;
        if (toPlayer.magnitude > detectionRange) return false;
        Vector3 horizontal = toPlayer;
        horizontal.y = 0f;
        if (horizontal.sqrMagnitude > 0.0001f &&
            Vector3.Angle(transform.forward, horizontal) > fieldOfView * 0.5f)
            return false;
        return !WallBetween(origin, target);
    }

 

    private bool CellExists(Vector2Int cell)
    {
        return maze != null && cell.x >= 0 && cell.x < maze.width &&
            cell.y >= 0 && cell.y < maze.height;
    }

    private bool IsOpen(Vector2Int cell, int direction)
    {
        if (maze == null || maze.cellWalls == null || !CellExists(cell))
            return false;
        Vector2Int neighbor = cell + CellDirections[direction];
        return CellExists(neighbor) &&
            !maze.cellWalls[cell.x, cell.y, direction];
    }

    private List<int> OpenDirections(Vector2Int cell)
    {
        List<int> result = new List<int>();
        for (int d = 0; d < 4; d++)
            if (IsOpen(cell, d)) result.Add(d);
        return result;
    }

    private Vector3 CellWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * maze.cellSize,
            agent.nextPosition.y, cell.y * maze.cellSize);
    }

    private Vector2Int CurrentCell()
    {
        Vector3 p = agent.nextPosition;
        return new Vector2Int(Mathf.RoundToInt(p.x / maze.cellSize),
            Mathf.RoundToInt(p.z / maze.cellSize));
    }

    private NavMeshQueryFilter FilterForAgent()
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter();
        filter.agentTypeID = agent.agentTypeID;
        filter.areaMask = agent.areaMask;
        return filter;
    }


    private bool TryDeployAtCurrentJunction()
    {
        if (maze == null) maze = FindObjectOfType<MazeGenerator>();
        if (maze == null || maze.cellWalls == null || maze.cellSize <= 0f)
            return false;

        Vector2Int cell = CurrentCell();
        if (!CellExists(cell) || usedJunctions.Contains(cell) ||
            failedJunctions.Contains(cell)) return false;

        int available = 3 - activeScouts.Count;
        int exits = OpenDirections(cell).Count;
        if (exits < 2 || exits - 1 > available) return false;

        NavMeshQueryFilter filter = FilterForAgent();
        NavMeshHit ownSurface;
        if (!NavMesh.SamplePosition(agent.nextPosition, out ownSurface, 2f, filter))
            return false;

        Vector3 desired = new Vector3(cell.x * maze.cellSize,
            ownSurface.position.y, cell.y * maze.cellSize);
        NavMeshHit junctionHit;
        if (!NavMesh.SamplePosition(desired, out junctionHit,
            Mathf.Max(0.6f, maze.cellSize * 0.3f), filter)) return false;

        Vector2 delta = new Vector2(agent.nextPosition.x - junctionHit.position.x,
            agent.nextPosition.z - junctionHit.position.z);
        if (delta.magnitude > Mathf.Max(junctionTriggerDistance, 1.2f))
            return false;

        selectedJunction = cell;
        junctionDestination = junctionHit.position;
        if (DeployAdaptiveScouts()) return true;

       
        failedJunctions.Add(cell);
        return false;
    }

    private bool TryChooseUsefulJunction()
    {
        if (maze == null) maze = FindObjectOfType<MazeGenerator>();
        if (maze == null || maze.cellWalls == null || maze.cellSize <= 0f ||
            scoutPrefab == null)
        {
            if (logDecisions && Time.time >= nextJunctionSearchTime)
                Debug.LogWarning("Splitter: MazeGenerator or Scout Prefab not ready.");
            nextJunctionSearchTime = Time.time + 3f;
            return false;
        }

        NavMeshAgent prefabAgent = scoutPrefab.GetComponent<NavMeshAgent>();
        if (prefabAgent == null || prefabAgent.agentTypeID != agent.agentTypeID)
        {
            if (logDecisions) Debug.LogWarning(
                "Splitter: Scout prefab NavMeshAgent missing / Agent Type mismatch.");
            nextJunctionSearchTime = Time.time + 3f;
            return false;
        }

        int freeSlots = 3 - activeScouts.Count;
        if (freeSlots == 0)
        {
            nextJunctionSearchTime = Time.time + 1f;
            return false;
        }

        NavMeshQueryFilter filter = FilterForAgent();
        NavMeshHit ownSurface;
        if (!NavMesh.SamplePosition(agent.nextPosition, out ownSurface, 3f, filter))
        {
            nextJunctionSearchTime = Time.time + 2f;
            return false;
        }

  
        float bestScore = float.NegativeInfinity;
        Vector2Int bestCell = Vector2Int.zero;
        Vector3 bestPoint = Vector3.zero;
        bool found = false;
        int openJunctionCount = 0;
        int navigableJunctionCount = 0;

        for (int x = 0; x < maze.width; x++)
        {
            for (int z = 0; z < maze.height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (usedJunctions.Contains(cell) || failedJunctions.Contains(cell))
                    continue;

                int exits = OpenDirections(cell).Count;
                if (exits < 2 || exits - 1 > freeSlots) continue;
                openJunctionCount++;

                Vector3 cellCenter = new Vector3(x * maze.cellSize,
                    ownSurface.position.y, z * maze.cellSize);
                NavMeshHit hit;
                // Use the actual floor height, avoiding the Base Offset issue.
                if (!NavMesh.SamplePosition(cellCenter, out hit,
                    Mathf.Max(0.7f, maze.cellSize * 0.25f), filter))
                    continue;

                // Don't accidentally snap a different corridor's NavMesh point.
                Vector2 deltaFromCell = new Vector2(
                    hit.position.x - cellCenter.x, hit.position.z - cellCenter.z);
                if (deltaFromCell.magnitude > maze.cellSize * 0.32f)
                    continue;

                NavMeshPath path = new NavMeshPath();
                if (!agent.CalculatePath(hit.position, path) ||
                    path.status != NavMeshPathStatus.PathComplete)
                    continue;
                navigableJunctionCount++;

         
                float travelDistance = 0f;
                for (int i = 1; i < path.corners.Length; i++)
                    travelDistance += Vector3.Distance(path.corners[i-1], path.corners[i]);

                float score = -travelDistance + exits * 0.3f * maze.cellSize;
                if (mainVisitedCells.Contains(cell)) score -= maze.cellSize;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCell = cell;
                    bestPoint = hit.position;
                    found = true;
                }
            }
        }

        if (!found)
        {
            nextJunctionSearchTime = Time.time + 3f;
            if (logDecisions) Debug.Log(
                "Splitter junction scan: openCandidates=" + openJunctionCount +
                ", navigable=" + navigableJunctionCount +
                ". Check NavMesh coverage if both are not positive.");
            return false;
        }

        selectedJunction = bestCell;
        junctionDestination = bestPoint;
        ChangeState(State.GoingToJunction);

        if (!SetReachableDestination(junctionDestination, patrolSpeed, 0.6f))
        {
            ChangeState(State.Patrol);
            nextJunctionSearchTime = Time.time + 2f;
            return false;
        }

        if (logDecisions) Debug.Log("Splitter heading to " +
            OpenDirections(selectedJunction).Count + "-route junction " +
            selectedJunction + " (NavMesh path found).");
        return true;
    }

    private void GoToJunction()
    {
        float distance = Vector2.Distance(
            new Vector2(agent.nextPosition.x, agent.nextPosition.z),
            new Vector2(junctionDestination.x, junctionDestination.z));

        if (distance <= Mathf.Max(junctionTriggerDistance, 1.3f) || Arrived())
        {
            if (DeployAdaptiveScouts()) return;

         
            failedJunctions.Add(selectedJunction);
            abilityTimer = 3f;
            nextJunctionSearchTime = Time.time + 3f;
            ChangeState(State.Patrol);
            return;
        }

        if (Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + Mathf.Max(0.2f, repathInterval);
            if (!SetReachableDestination(junctionDestination, patrolSpeed, 0.8f))
            {
                failedJunctions.Add(selectedJunction);
                ChangeState(State.Patrol);
                nextJunctionSearchTime = Time.time + 3f;
            }
        }
    }

    private bool TryBranchSpawn(Vector2Int junction, int branch,
        out Vector3 navMeshPosition)
    {
        navMeshPosition = Vector3.zero;
        if (!IsOpen(junction, branch) || scoutPrefab == null) return false;

        NavMeshAgent prefabAgent = scoutPrefab.GetComponent<NavMeshAgent>();
        if (prefabAgent == null || prefabAgent.agentTypeID != agent.agentTypeID)
            return false;


        Vector2Int neighbor = junction + CellDirections[branch];
        Vector3 desired = new Vector3(neighbor.x * maze.cellSize,
            junctionDestination.y, neighbor.y * maze.cellSize);
        NavMeshQueryFilter filter = FilterForAgent();
        NavMeshHit hit;
        float radius = Mathf.Clamp(maze.cellSize * 0.25f, 0.4f, 1.2f);

        if (!NavMesh.SamplePosition(desired, out hit, radius, filter))
            return false;

        // Ensure it wasn't sampled into another cell through a wall.
        Vector2 offset = new Vector2(hit.position.x - desired.x,
            hit.position.z - desired.z);
        if (offset.magnitude > radius) return false;

        if (WallBetween(junctionDestination + Vector3.up * 0.6f,
            hit.position + Vector3.up * 0.6f)) return false;

        NavMeshPath path = new NavMeshPath();
        if (!agent.CalculatePath(hit.position, path) ||
            path.status != NavMeshPathStatus.PathComplete) return false;

        
        navMeshPosition = hit.position;
        return true;
    }

    private int SelectMainBranch(List<int> directions)
    {
      
        int best = directions[0];
        float bestScore = float.NegativeInfinity;
        Vector3 clue = lastKnownPosition - junctionDestination;
        clue.y = 0f;
        if (clue.sqrMagnitude > 0.001f) clue.Normalize();

        foreach (int d in directions)
        {
            Vector3 dir = new Vector3(CellDirections[d].x,
                0f, CellDirections[d].y);
            Vector2Int neighbor = selectedJunction + CellDirections[d];
            float score = Vector3.Dot(transform.forward, dir) * 1.4f;
            if (hasPlayerClue)
                score += Vector3.Dot(clue, dir) * 2.5f;
            if (!mainVisitedCells.Contains(neighbor)) score += 1.0f;
            score += UnityEngine.Random.Range(0f, 0.15f); // Tie breaker
            if (score > bestScore)
            {
                bestScore = score;
                best = d;
            }
        }
        return best;
    }

    private bool DeployAdaptiveScouts()
    {
        if (maze == null || scoutPrefab == null) return false;
        List<int> directions = OpenDirections(selectedJunction);
        int requiredScouts = directions.Count - 1;
        if (requiredScouts < 1 || requiredScouts > 3 ||
            activeScouts.Count + requiredScouts > 3) return false;

        NavMeshAgent prefabAgent = scoutPrefab.GetComponent<NavMeshAgent>();
        if (prefabAgent == null || prefabAgent.agentTypeID != agent.agentTypeID)
            return false;

        // One valid path per route. The main takes one, each Scout a different one.
        Vector3[] branchPositions = new Vector3[4];
        int failedBranch = -1;
        foreach (int d in directions)
        {
            if (!TryBranchSpawn(selectedJunction, d, out branchPositions[d]))
            {
                failedBranch = d;
                break;
            }
        }
        if (failedBranch != -1)
        {
            if (logDecisions) Debug.LogWarning(
                "Splitter junction " + selectedJunction +
                " has " + directions.Count + " routes but branch " +
                failedBranch + " has no usable NavMesh. Trying another junction.");
            return false;
        }

        int mainBranch = SelectMainBranch(directions);

    
        ChangeState(State.BranchSearch);
        if (!SetReachableDestination(branchPositions[mainBranch],
                patrolSpeed, 0.7f))
        {
            ChangeState(State.Patrol);
            return false;
        }

        
        forbiddenJunction = selectedJunction;
        previousCell = selectedJunction;
        visitedBranchCells.Clear();
        visitedBranchCells.Add(selectedJunction);
        branchTimer = branchSearchDuration;
        abilityTimer = abilityCooldown;
        usedJunctions.Add(selectedJunction);

        int spawned = 0;
        foreach (int d in directions)
        {
            if (d == mainBranch) continue;
            Vector3 dir = new Vector3(CellDirections[d].x,
                0f, CellDirections[d].y);

            // Spawn just inside each assigned corridor.
            Vector3 worldPosition = branchPositions[d] +
                Vector3.up * prefabAgent.baseOffset;
            SplitterAI scout = Instantiate(scoutPrefab, worldPosition,
                Quaternion.LookRotation(dir));
            scout.InitializeScoutForBranch(player, dir,
                OnScoutFound, selectedJunction);
            if (!scout.gameObject.activeSelf)
                scout.gameObject.SetActive(true);
            activeScouts.Add(scout.gameObject);
            spawned++;
        }

        if (logDecisions) Debug.Log("Splitter decision: " + directions.Count +
            " open routes at " + selectedJunction + " => main branch " +
            mainBranch + ", deployed " + spawned + " Scouts to other branches.");
        return true;
    }



    private bool ExploreNextBranchCell(float speed)
    {
        if (maze == null || maze.cellWalls == null) return false;
        Vector2Int current = CurrentCell();
        if (!CellExists(current)) return false;
        visitedBranchCells.Add(current);

        List<Vector2Int> choices = new List<Vector2Int>();
        for (int i = 0; i < 4; i++)
        {
            if (!IsOpen(current, i)) continue;
            Vector2Int neighbor = current + CellDirections[i];
            if (neighbor == forbiddenJunction) continue;
            choices.Add(neighbor);
        }
        if (choices.Count == 0) return false;

  
        choices.Sort((a, b) =>
        {
            int scoreA = (visitedBranchCells.Contains(a) ? 2 : 0) +
                (a == previousCell ? 1 : 0);
            int scoreB = (visitedBranchCells.Contains(b) ? 2 : 0) +
                (b == previousCell ? 1 : 0);
            return scoreA.CompareTo(scoreB);
        });

        foreach (Vector2Int neighbor in choices)
        {
            Vector3 dest = CellWorld(neighbor);
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(dest, out hit, 1.0f,
                    agent.areaMask)) continue;
            // A neighboring cell must have a direct navigable corridor.
            if (NavMesh.Raycast(agent.nextPosition, hit.position,
                    out _, agent.areaMask)) continue;
            if (SetReachableDestination(hit.position, speed, 0.6f))
            {
                previousCell = current;
                return true;
            }
        }
        return false;
    }

    private void SearchOwnBranch()
    {
        branchTimer -= Time.deltaTime;
        if (branchTimer <= 0f)
        {
            ChangeState(State.Patrol);
            nextPatrolTime = 0f;
            return;
        }
        agent.speed = patrolSpeed;
        if (Arrived() && Time.time >= nextPatrolTime)
        {
            if (!ExploreNextBranchCell(patrolSpeed))
            {
                agent.isStopped = true;
                agent.updateRotation = false;
                transform.Rotate(0f, 90f * Time.deltaTime, 0f);
                nextPatrolTime = Time.time + 1f;
            }
            else nextPatrolTime = Time.time + 0.3f;
        }
    }

    public void InitializeScoutForBranch(Transform targetPlayer,
        Vector3 direction, Action<Vector3> callback, Vector2Int junctionCell)
    {
        isScout = true;
        player = targetPlayer;
        reportToMain = callback;
        scoutTimer = 0f;
        scoutFoundPlayer = false;
        scoutHasStunned = false;
        forbiddenJunction = junctionCell;
        previousCell = junctionCell;
        visitedBranchCells.Clear();
        visitedBranchCells.Add(junctionCell);
        // Each scout spawns INSIDE its own branch and explores from there.
    }

    public void InitializeScout(Transform targetPlayer, Vector3 direction,
        Action<Vector3> callback)
    {
        InitializeScoutForBranch(targetPlayer, direction, callback,
            new Vector2Int(int.MinValue, int.MinValue));
    }

    private void OnScoutFound(Vector3 position)
    {
        hasPlayerClue = true;
        BeginInvestigation(position);
        Debug.Log("Main Splitter received Player location from Scout!");
    }

    private void UpdateScout()
    {
        scoutTimer += Time.deltaTime;
        if (scoutTimer >= scoutLifetime)
        {
            Destroy(gameObject);
            return;
        }
        if (player == null) return;

        Vector3 eye = Vector3.up * 0.5f;
        float distance = Vector3.Distance(transform.position, player.position);
        if (!scoutFoundPlayer && distance <= scoutDetectionRange &&
            !WallBetween(transform.position + eye, player.position + eye))
        {
            scoutFoundPlayer = true;
            reportToMain?.Invoke(player.position);
            nextPathTime = 0f;
            Debug.Log("Scout found Player!");
        }

        if (scoutFoundPlayer) ChaseAndStunPlayer();
        else if (Arrived() && Time.time >= nextPatrolTime)
        {
            if (!ExploreNextBranchCell(scoutSpeed))
            {
                agent.isStopped = true;
                agent.updateRotation = false;
                transform.Rotate(0f, 90f * Time.deltaTime, 0f);
                nextPatrolTime = Time.time + 1f;
            }
            else nextPatrolTime = Time.time + 0.3f;
        }
    }

    private void ChaseAndStunPlayer()
    {
        if (player == null) return;
        float distance = Vector3.Distance(transform.position, player.position);
        Vector3 eye = Vector3.up * 0.5f;
        if (distance <= scoutStunRange &&
            !WallBetween(transform.position + eye, player.position + eye))
        {
            Movement movement = player.GetComponent<Movement>();
            if (movement == null) movement = player.GetComponentInParent<Movement>();
            if (movement == null) movement = player.GetComponentInChildren<Movement>();
            if (movement != null && !scoutHasStunned)
            {
                movement.ApplyStun(stunDuration);
                scoutHasStunned = true;
                Debug.Log("Scout stunned Player for " + stunDuration + " seconds!");
            }
            Destroy(gameObject);
            return;
        }
        if (Time.time >= nextPathTime)
        {
            nextPathTime = Time.time + Mathf.Max(0.1f, repathInterval);
            SetReachableDestination(player.position, scoutSpeed, 1.5f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) return;
        forward.Normalize();
        Gizmos.color = Color.yellow;
        float half = fieldOfView * 0.5f;
        Vector3 previous = origin +
            Quaternion.Euler(0f, -half, 0f) * forward * detectionRange;
        for (int i = 1; i <= 24; i++)
        {
            float angle = -half + fieldOfView * i / 24f;
            Vector3 next = origin +
                Quaternion.Euler(0f, angle, 0f) * forward * detectionRange;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + forward * detectionRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            Application.isPlaying ? homePosition : transform.position,
            patrolRadius);
        if (Application.isPlaying && state == State.Investigate)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(lastKnownPosition, 0.25f);
        }
    }
}
