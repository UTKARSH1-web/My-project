using Unity.AI.Navigation;
using System.Collections.Generic;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{
    public int width = 6;
    public int height = 6;
    public float cellSize = 4f;
    public int seed = 1234;

    private float wallThickness = 0.2f;
    private float wallHeight = 3f;
    public bool[,,] cellWalls;
    public bool[,] cellVisit;

    private NavMeshSurface navMeshSurface;

    public GameObject[,,] wallObjects;
    public Transform startPoint;
    public Transform exitPoint;
    Stack<Vector2Int> path;
    public GameObject player;
    public GameObject keyPrefab;
    private List<Vector2Int> occupiedCells = new List<Vector2Int>();
    
    [Header("Enemy Prefabs")]
    public GameObject zombiePrefab;
    public GameObject witchPrefab;
    public GameObject boomerPrefab;

    [Header("Enemy Spawning")]
    public int cellsPerZombie = 8;
    public int specialEnemyDistance = 5;
    List<Vector2Int> GetUnvisitedNeighbours(int x, int z)
    {
        List<Vector2Int> neighbours = new List<Vector2Int>();

        if (z < height - 1)
        {
            if (!cellVisit[x, z + 1])
                neighbours.Add(new Vector2Int(x, z + 1)); // North
        }
        if (x < width - 1)
        {
            if (!cellVisit[x + 1, z])
                neighbours.Add(new Vector2Int(x + 1, z)); // East
        }
        if (z > 0)
        {
            if (!cellVisit[x, z - 1])
                neighbours.Add(new Vector2Int(x, z - 1)); // South
        }
        if (x > 0)
        {
            if (!cellVisit[x - 1, z])
                neighbours.Add(new Vector2Int(x - 1, z)); // West
        }

        return neighbours;
    }


    void Start()
    {
        GameObject mazeWalls = new GameObject("MazeWalls");
        wallObjects = new GameObject[width, height, 4];
        cellWalls = new bool[width, height, 4];
        cellVisit = new bool[width, height];
        path = new Stack<Vector2Int>();
        Random.InitState(seed);
        navMeshSurface = GetComponentInChildren<NavMeshSurface>();

        CreateMaze(mazeWalls);
        GenerateMaze();
        VerifyMaze();
        OpenWalls();
        CreateStartAndExit();
        RemoveWall(0, 0, new Vector2Int(0, -1));
        RemoveWall(width - 1, height - 1, new Vector2Int(0, 1));
        navMeshSurface.BuildNavMesh();
        SpawnKey();
        SpawnZombies();
        SpawnEnemy(witchPrefab, specialEnemyDistance);
        SpawnEnemy(boomerPrefab, specialEnemyDistance);
        player.SetActive(true);
    }


    void OpenWalls()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                for (int w = 0; w < 2; w++)
                {
                    Vector2Int wall;
                    if (w == 0)
                        wall = new Vector2Int(0, 1);
                    else
                        wall = new Vector2Int(1, 0);
                    if (Random.Range(0, 10) == 0 && cellWalls[x, z, w])
                    {
                        if (w == 0 && z < height - 1 || w == 1 && x < width - 1)
                            RemoveWall(x, z, wall);
                    }
                }
            }
        }
    }
    void GenerateMaze()
    {
        List<Vector2Int> neighbours;
        Vector2Int nextCell;
        Vector2Int currentCell;
        Vector2Int direction;
        int randomIndex;
        cellVisit[0, 0] = true;
        path.Push(new Vector2Int(0, 0));

        while (path.Count != 0)
        {
            neighbours = GetUnvisitedNeighbours(path.Peek().x, path.Peek().y);
            if (neighbours.Count > 0)
            {
                randomIndex = Random.Range(0, neighbours.Count);
                nextCell = neighbours[randomIndex];
                currentCell = path.Peek();
                direction = nextCell - currentCell;
                RemoveWall(currentCell.x, currentCell.y, direction);

                cellVisit[nextCell.x, nextCell.y] = true;
                path.Push(nextCell);
            }
            else
            {
                path.Pop();
            }
        }
    }

    void VerifyMaze()
    {
        int count = 0;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (cellVisit[x, z])
                    count++;
            }
        }

        Debug.Log("Visited cells: " + count);
    }

    void RemoveWall(int x, int z, Vector2Int wall)
    {
        if (wall == new Vector2Int(0, 1)) // North
        {
            cellWalls[x, z, 0] = false;

            if (z < height - 1)
            {
                cellWalls[x, z + 1, 2] = false;
                wallObjects[x, z + 1, 2].SetActive(false);
            }
            else
                wallObjects[x, z, 0].SetActive(false);
        }
        else if (wall == new Vector2Int(1, 0)) // East
        {
            cellWalls[x, z, 1] = false;

            if (x < width - 1)
            {
                cellWalls[x + 1, z, 3] = false;
                wallObjects[x + 1, z, 3].SetActive(false);
            }
            else
                wallObjects[x, z, 1].SetActive(false);
        }
        else if (wall == new Vector2Int(0, -1)) // South
        {
            cellWalls[x, z, 2] = false;
            wallObjects[x, z, 2].SetActive(false);

            if (z > 0)
                cellWalls[x, z - 1, 0] = false;
        }
        else if (wall == new Vector2Int(-1, 0)) // West
        {
            cellWalls[x, z, 3] = false;
            wallObjects[x, z, 3].SetActive(false);

            if (x > 0)
                cellWalls[x - 1, z, 1] = false;
        }
    }

    void InitializeWalls()
    {
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                for (int k = 0; k < 4; k++)
                    cellWalls[i, j, k] = true;
            }
        }
    }

    void CreateMaze(GameObject mazeWalls)
    {
        InitializeWalls();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector3 center = new Vector3(
                    x * cellSize, 0, z * cellSize
                );

                // South wall
                CreateWall(center + new Vector3(0, wallHeight / 2, -cellSize / 2),
                    new Vector3(cellSize, wallHeight, wallThickness),
                    mazeWalls.transform, x, z, 2);

                // West wall
                CreateWall(center + new Vector3(-cellSize / 2, wallHeight / 2, 0),
                    new Vector3(wallThickness, wallHeight, cellSize),
                    mazeWalls.transform, x, z, 3);

                if (x == width - 1)
                {
                    // East wall
                    CreateWall(center + new Vector3(cellSize / 2, wallHeight / 2, 0),
                        new Vector3(wallThickness, wallHeight, cellSize),
                        mazeWalls.transform, x, z, 1);
                }
                if (z == height - 1)
                {
                    // North wall
                    CreateWall(center + new Vector3(0, wallHeight / 2, cellSize / 2),
                        new Vector3(cellSize, wallHeight, wallThickness),
                        mazeWalls.transform, x, z, 0);
                }
            }
        }
    }

    void CreateStartAndExit()
    {
        GameObject start = new GameObject("StartPoint");
        start.transform.SetParent(transform);


        start.transform.position = new Vector3(
            0,
            0,
            -cellSize / 2f
        );
        startPoint = start.transform;

        GameObject exit = new GameObject("ExitPoint");
        exit.transform.SetParent(transform);
        exit.transform.position = new Vector3(
            (width - 1) * cellSize,
            0,
            height * cellSize - cellSize / 2f
        );
        exitPoint = exit.transform;

        GameObject exitMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        exitMarker.AddComponent<ExitTrigger>();
        exitMarker.name = "ExitMarker";
        exitMarker.transform.SetParent(exitPoint);
        exitMarker.transform.localPosition = Vector3.zero;
        exitMarker.transform.localScale = new Vector3(1f, 0.2f, 1f);
        Collider exitCollider = exitMarker.GetComponent<Collider>();
        exitCollider.isTrigger = true;

        if (player != null)
        {
            player.transform.position = startPoint.position + Vector3.up;
        }
    }

    void SpawnKey()
    {
        Vector2Int keyCell;

        do
        {
            keyCell = new Vector2Int(
                Random.Range(0, width),
                Random.Range(0, height)
            );
            
        }
        while (keyCell == new Vector2Int(0, 0) ||
            keyCell == new Vector2Int(width - 1, height - 1));

        Vector3 spawnPosition = new Vector3(
            keyCell.x * cellSize,
            0.5f,
            keyCell.y * cellSize
        );
        MarkCellOccupied(keyCell);

        GameObject key = Instantiate(keyPrefab, spawnPosition, Quaternion.identity, transform);
        key.AddComponent<KeyPickup>();
    }
    void SpawnZombies()
    {
        int totalCells = width * height;
        int zombieCount = Mathf.CeilToInt((float)totalCells / cellsPerZombie);

        for (int i = 0; i < zombieCount; i++)
        {
            SpawnEnemy(zombiePrefab, 2);
        }
    }
    void SpawnEnemy(GameObject prefab, int minDistanceFromStart)
    {
        if (prefab == null)
            return;

        Vector2Int cell = GetRandomSpawnCell(minDistanceFromStart);

        if (cell.x < 0)
        {
            Debug.LogWarning("No available spawn cell for " + prefab.name);
            return;
        }

        MarkCellOccupied(cell);

        Vector3 spawnPosition = new Vector3(
            cell.x * cellSize,
            1f,
            cell.y * cellSize
        );

        Instantiate(prefab, spawnPosition, Quaternion.identity);
    }

    void CreateWall(Vector3 position, Vector3 scale, Transform parent, int x, int z, int loc)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = position;
        wall.transform.localScale = scale;

        wall.transform.SetParent(parent);

        wallObjects[x, z, loc] = wall;
    }
    void MarkCellOccupied(Vector2Int cell)
    {
        if (!occupiedCells.Contains(cell))
            occupiedCells.Add(cell);
    }

    Vector2Int GetRandomSpawnCell(int minDistanceFromStart)
    {
        List<Vector2Int> availableCells = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);

                int distance = Mathf.Abs(x) + Mathf.Abs(z);

                if (distance < minDistanceFromStart)
                    continue;

                if (cell == new Vector2Int(width - 1, height - 1))
                    continue;

                if (occupiedCells.Contains(cell))
                    continue;

                availableCells.Add(cell);
            }
        }

        if (availableCells.Count == 0)
            return new Vector2Int(-1, -1);

        return availableCells[Random.Range(0, availableCells.Count)];
    }


}