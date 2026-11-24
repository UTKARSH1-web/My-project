using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class Minimap : MonoBehaviour
{
    public MazeGenerator maze;
    public Transform player;
    public RectTransform playerMarker;

    public GameObject cellPrefab;
    public GameObject wallPrefab;

    private List<GameObject> cells = new List<GameObject>();

    private bool[,] exploredCells;
    private int lastPlayerX = -1;
    private int lastPlayerZ = -1;
    private Image[,] cellImages;
    private float cellWidth;
    private float cellHeight;
    private bool[,] wallsDrawn; 

    void DrawMaze()
    {
        cellWidth = GetComponent<RectTransform>().rect.width / maze.width;
        cellHeight = GetComponent<RectTransform>().rect.height / maze.height;

        for (int x = 0; x < maze.width; x++)
        {
            for (int z = 0; z < maze.height; z++)
            {
                GameObject cell = Instantiate(cellPrefab, transform);
                cellImages[x, z] = cell.GetComponent<Image>();

                RectTransform rect = cell.GetComponent<RectTransform>();

                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = Vector2.zero;

                rect.sizeDelta = new Vector2(cellWidth - 2f, cellHeight - 2f);

                rect.anchoredPosition = new Vector2(
                    x * cellWidth,
                    z * cellHeight
                );

                cells.Add(cell);
            }
        }
        playerMarker.SetAsLastSibling();
    }


    void DrawCellWalls(int x, int z)
    {
        if (wallsDrawn[x, z])
            return;

        Vector2 center = new Vector2(
            x * cellWidth + cellWidth / 2f,
            z * cellHeight + cellHeight / 2f
        );

        // North
        if (maze.cellWalls[x, z, 0])
            CreateWall(center + new Vector2(0, cellHeight / 2f),
                new Vector2(cellWidth, 2));

        // East
        if (maze.cellWalls[x, z, 1])
            CreateWall(center + new Vector2(cellWidth / 2f, 0),
                new Vector2(2, cellHeight));

        // South
        if (maze.cellWalls[x, z, 2])
            CreateWall(center - new Vector2(0, cellHeight / 2f),
                new Vector2(cellWidth, 2));

        // West
        if (maze.cellWalls[x, z, 3])
            CreateWall(center - new Vector2(cellWidth / 2f, 0),
                new Vector2(2, cellHeight));

        wallsDrawn[x, z] = true;
    }

    void CreateWall(Vector2 position, Vector2 size)
    {
        GameObject wall = Instantiate(wallPrefab, transform);

        RectTransform rect = wall.GetComponent<RectTransform>();

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);

        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
    void Update()
    {
        int x = Mathf.RoundToInt(player.position.x / maze.cellSize);
        int z = Mathf.RoundToInt(player.position.z / maze.cellSize);

        x = Mathf.Clamp(x, 0, maze.width - 1);
        z = Mathf.Clamp(z, 0, maze.height - 1);

        if (x != lastPlayerX || z != lastPlayerZ)
        {
            exploredCells[x, z] = true;
            cellImages[x, z].color = new Color(0.35f, 0.40f, 0.46f, 1f);

            DrawCellWalls(x, z);

            lastPlayerX = x;
            lastPlayerZ = z;

            Debug.Log("Explored cell: " + x + ", " + z);
        }
        float cellWidth = GetComponent<RectTransform>().rect.width / maze.width;
        float cellHeight = GetComponent<RectTransform>().rect.height / maze.height;

        playerMarker.anchoredPosition = new Vector2(
            (player.position.x / maze.cellSize + 0.5f) * cellWidth,
            (player.position.z / maze.cellSize + 0.5f) * cellHeight
        );
        playerMarker.localEulerAngles = new Vector3(
            0f,
            0f,
            -player.eulerAngles.y
        );
    }

    void Start()
    {
        exploredCells = new bool[maze.width, maze.height];
        cellImages = new Image[maze.width, maze.height];
        wallsDrawn = new bool[maze.width, maze.height];

        DrawMaze();
    }
}
