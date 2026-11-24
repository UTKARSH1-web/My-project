using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;


public class MainMenu : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public TMP_InputField seedInput;
    public Button randomSeedButton;
    public Button startButton;
    public Button easyButton;
    public Button mediumButton;
    public Button hardButton;

    private int selectedDifficulty = 0;
    
    void Start()
    {
        randomSeedButton.onClick.AddListener(GenerateRandomSeed);
        startButton.onClick.AddListener(StartGame);
        easyButton.onClick.AddListener(() => SelectDifficulty(0));
        mediumButton.onClick.AddListener(() => SelectDifficulty(1));
        hardButton.onClick.AddListener(() => SelectDifficulty(2));

        SelectDifficulty(0);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    void SelectDifficulty(int difficulty)
    {
        selectedDifficulty = difficulty;

        easyButton.image.color = difficulty == 0
            ? new Color32(113, 51, 51, 255)
            : new Color32(48, 49, 56, 255);

        mediumButton.image.color = difficulty == 1
            ? new Color32(113, 51, 51, 255)
            : new Color32(48, 49, 56, 255);

        hardButton.image.color = difficulty == 2
            ? new Color32(113, 51, 51, 255)
            : new Color32(48, 49, 56, 255);
    }

    void GenerateRandomSeed()
    {
        int randomSeed = Random.Range(0, 1000000);
        seedInput.text = randomSeed.ToString();
    }
    void StartGame()
    {
        int width = 6;
        int height = 6;

        switch (selectedDifficulty)
        {
            case 0:
                width = 6;
                height = 6;
                break;

            case 1:
                width = 10;
                height = 10;
                break;

            case 2:
                width = 15;
                height = 15;
                break;
        }

        if (!int.TryParse(seedInput.text, out int seed))
        {
            seed = Random.Range(0, 1000000);
        }

        PlayerPrefs.SetInt("MazeWidth", width);
        PlayerPrefs.SetInt("MazeHeight", height);
        PlayerPrefs.SetInt("MazeSeed", seed);

        SceneManager.LoadScene("Level");
    }
}
