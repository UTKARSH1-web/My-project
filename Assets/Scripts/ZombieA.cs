using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class ZombieMovement : MonoBehaviour
{
    private NavMeshAgent agent;
    public Vector3 targetPosition;
    public MazeGenerator maze;
    private List<Vector2Int> currentPath;
    private int currentWaypoint;
    public float waypointTolerance = 0.2f;
    public int patrolRadius = 3;
    public enum ZombieState
    {
        Patrol,
        Investigate,
        Chase,
        Intercept,
        Search
    }

    public ZombieState currentState = ZombieState.Patrol;
    private Vector3 lastKnownPlayerPosition;
    private Vector3 lastHeardPosition;
    private Vector3 predictedPlayerPosition;
    private Transform player;
    public float sightRange = 12f;
    public float fieldOfView = 100f;
    public LayerMask obstacleMask;
    public float searchDuration = 5f;
    private float searchTimer;
    private bool isInitialized;

    Vector2Int GetRandomNearbyCell(Vector2Int currentCell)
    {
        List<Vector2Int> nearbyCells = new List<Vector2Int>();

        for (int x = 0; x < maze.width; x++)
        {
            for (int z = 0; z < maze.height; z++)
            {
                int distance = Mathf.Abs(x - currentCell.x) +
                               Mathf.Abs(z - currentCell.y);

                if (distance > 0 && distance <= patrolRadius)
                {
                    nearbyCells.Add(new Vector2Int(x, z));
                }
            }
        }

        if (nearbyCells.Count == 0)
            return currentCell;

        return nearbyCells[Random.Range(0, nearbyCells.Count)];
    }

    bool CanSeePlayer()
    {
        if (player == null)
            return false;
        Vector3 direction = player.position - transform.position;

        if (direction.magnitude > sightRange)
            return false;

        float angle = Vector3.Angle(transform.forward, direction);

        if (angle > fieldOfView / 2f)
            return false;

        if (Physics.Raycast(
            transform.position + Vector3.up,
            direction.normalized,
            out RaycastHit hit,
            sightRange,
            obstacleMask))
        {
            return false;
        }

        return true;
    }

    List<Vector2Int> FindPath(Vector2Int start, Vector2Int target)
    {
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom =
            new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        cameFrom[start] = start;

        Vector2Int[] directions =
        {
            new Vector2Int(0, 1),  // North
            new Vector2Int(1, 0),  // East
            new Vector2Int(0, -1), // South
            new Vector2Int(-1, 0)  // West
        };


        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();

            if (current == target)
                break;

            for (int i = 0; i < directions.Length; i++)
            {
                Vector2Int next = current + directions[i];

                if (next.x < 0 || next.x >= maze.width ||
                    next.y < 0 || next.y >= maze.height)
                    continue;

                if (maze.cellWalls[current.x, current.y, i])
                    continue;

                if (cameFrom.ContainsKey(next))
                    continue;

                queue.Enqueue(next);
                cameFrom[next] = current;
            }
        }

        if (!cameFrom.ContainsKey(target))
            return null;

        List<Vector2Int> result = new List<Vector2Int>();
        Vector2Int cell = target;

        while (cell != start)
        {
            result.Add(cell);
            cell = cameFrom[cell];
        }

        result.Add(start);
        result.Reverse();

        return result;
    }

    void FollowPath(Vector2Int start, Vector2Int target)
    {
        currentPath = FindPath(start, target);
        Debug.Log("Following path to: " + target);

        if (currentPath == null)
        {
            Debug.LogWarning("No path found!");
            return;
        }

        currentWaypoint = 0;
        MoveToNextWaypoint();
    }

    void MoveToNextWaypoint()
    {
        if (currentWaypoint >= currentPath.Count)
            return;

        Vector2Int cell = currentPath[currentWaypoint];

        Vector3 destination = new Vector3(
            cell.x * maze.cellSize,
            transform.position.y,
            cell.y * maze.cellSize
        );

        agent.SetDestination(destination);
    }

    Vector2Int GetRandomCell()
    {
        return new Vector2Int(
            Random.Range(0, maze.width),
            Random.Range(0, maze.height)
        );
    }

    Vector3 GetCellCenter(int x, int z)
    {
        return new Vector3(
            x * maze.cellSize,
            transform.position.y,
            z * maze.cellSize
        );
    }

    void UpdateState()
    {
        if (CanSeePlayer())
        {
            lastKnownPlayerPosition = player.position;
            currentState = ZombieState.Chase;
        }
        else if (currentState == ZombieState.Chase)
        {
            currentState = ZombieState.Intercept;
            searchTimer = searchDuration;
        }
    }

    void Update()
    {
        if (!isInitialized)
            return;

        UpdateState();

        // CHASE
        if (currentState == ZombieState.Chase)
        {
            agent.SetDestination(player.position);
            return;
        }

        // INTERCEPT
        if (currentState == ZombieState.Intercept)
        {
            if (searchTimer > 0)
            {
                searchTimer -= Time.deltaTime;
                agent.SetDestination(lastKnownPlayerPosition);
            }
            else
            {
                currentState = ZombieState.Patrol;
                currentPath = null;
            }

            return;
        }

        // INVESTIGATE
        if (currentState == ZombieState.Investigate)
        {
            if (currentPath == null)
            {
                Vector2Int currentCell = new Vector2Int(
                    Mathf.RoundToInt(transform.position.x / maze.cellSize),
                    Mathf.RoundToInt(transform.position.z / maze.cellSize)
                );

                Vector2Int soundCell = new Vector2Int(
                    Mathf.RoundToInt(lastHeardPosition.x / maze.cellSize),
                    Mathf.RoundToInt(lastHeardPosition.z / maze.cellSize)
                );

                soundCell.x = Mathf.Clamp(soundCell.x, 0, maze.width - 1);
                soundCell.y = Mathf.Clamp(soundCell.y, 0, maze.height - 1);

                FollowPath(currentCell, soundCell);
                return;
            }

            if (currentWaypoint >= currentPath.Count)
            {
                currentState = ZombieState.Search;
                searchTimer = searchDuration;
                currentPath = null;

                Debug.Log("Zombie reached sound location.");
                return;
            }

            if (!agent.pathPending &&
                agent.remainingDistance <= waypointTolerance)
            {
                currentWaypoint++;
                MoveToNextWaypoint();
            }

            return;
        }

        // SEARCH
        if (currentState == ZombieState.Search)
        {
            searchTimer -= Time.deltaTime;

            if (searchTimer <= 0)
            {
                currentState = ZombieState.Patrol;
                currentPath = null;
            }

            return;
        }

        // PATROL
        if (currentState == ZombieState.Patrol)
        {
            if (currentPath == null)
            {
                Vector2Int currentCell = new Vector2Int(
                    Mathf.RoundToInt(transform.position.x / maze.cellSize),
                    Mathf.RoundToInt(transform.position.z / maze.cellSize)
                );

                FollowPath(currentCell, GetRandomNearbyCell(currentCell));
                return;
            }

            if (currentWaypoint >= currentPath.Count)
            {
                Vector2Int currentCell = currentPath[currentPath.Count - 1];

                FollowPath(currentCell, GetRandomNearbyCell(currentCell));
                return;
            }

            if (!agent.pathPending &&
                agent.remainingDistance <= waypointTolerance)
            {
                currentWaypoint++;
                MoveToNextWaypoint();
            }
        }
    }
    IEnumerator Start()
    {

        player = GameObject.FindGameObjectWithTag("Player")?.transform;

        if (player == null)
        {
            Debug.LogError("Player not found! Check the Player tag.");
            yield break;
        }
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = true;

        maze = FindFirstObjectByType<MazeGenerator>(FindObjectsInactive.Include);

        if (maze == null)
        {
            Debug.LogError("No MazeGenerator found!");
            yield break;
        }

        Debug.Log("Maze found: " + maze.name);

        Vector3 startPosition = transform.position;
        Vector2Int startCell = new Vector2Int(
            Mathf.RoundToInt(transform.position.x / maze.cellSize),
            Mathf.RoundToInt(transform.position.z / maze.cellSize)
        );

        NavMeshHit hit;

        while (!NavMesh.SamplePosition(startPosition, out hit, 2f, NavMesh.AllAreas))
        {
            yield return null;
        }

        transform.position = hit.position;

        agent.enabled = true;
        agent.Warp(hit.position);

        FollowPath(startCell, GetRandomNearbyCell(startCell));
        isInitialized = true;
    }
    void OnEnable()
    {
        SoundManager.OnSoundHeard += HearSound;
    }

    void OnDisable()
    {
        SoundManager.OnSoundHeard -= HearSound;
    }

    void HearSound(Vector3 soundPosition, float soundRadius)
    {
        float distance = Vector3.Distance(transform.position, soundPosition);

        if (distance <= soundRadius && currentState == ZombieState.Patrol)
        {
            lastHeardPosition = soundPosition;
            currentState = ZombieState.Investigate;
            currentPath = null;

            Debug.Log("Zombie heard a sound!");
        }
    }
}
