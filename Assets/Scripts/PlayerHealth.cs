
using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    private bool isDefeated = false;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDefeated)
            return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);

        Debug.Log("Player Health: " + currentHealth);

        if (currentHealth <= 0)
        {
            isDefeated = true;

            // Disable player movement
            Movement movement = GetComponent<Movement>();

            if (movement != null)
            {
                // Stop footstep sounds
                if (movement.footstepAudioSource != null)
                {
                    movement.footstepAudioSource.Stop();
                }

                // Disable movement and input
                movement.enabled = false;
            }

            // Stop other audio sources attached to player
            AudioSource[] audioSources =
                GetComponents<AudioSource>();

            foreach (AudioSource source in audioSources)
            {
                if (source != null)
                    source.Stop();
            }

            // Disable character controller
            CharacterController controller =
                GetComponent<CharacterController>();

            if (controller != null)
                controller.enabled = false;

            Debug.Log("Player has been defeated!");
        }
    }

    void OnGUI()
    {
        Color originalColor = GUI.color;

        // HEALTH DISPLAY
        float barWidth = Mathf.Min(280f, Screen.width * 0.4f);
        float barHeight = 24f;

        float healthPercentage =
            Mathf.Clamp01(
                (float)currentHealth / Mathf.Max(1, maxHealth)
            );

        GUIStyle healthStyle = new GUIStyle(GUI.skin.label);

        healthStyle.fontSize = 18;
        healthStyle.fontStyle = FontStyle.Bold;
        healthStyle.normal.textColor = Color.white;

        GUI.color = Color.white;

        GUI.Label(
            new Rect(25, 15, barWidth, 30),
            "HEALTH: " + currentHealth + " / " + maxHealth,
            healthStyle
        );

        // HEALTH BAR BACKGROUND
        GUI.color = Color.gray;

        GUI.DrawTexture(
            new Rect(25, 48, barWidth, barHeight),
            Texture2D.whiteTexture
        );

        // HEALTH BAR FILL
        GUI.color = Color.Lerp(
            Color.red,
            Color.green,
            healthPercentage
        );

        GUI.DrawTexture(
            new Rect(
                25,
                48,
                barWidth * healthPercentage,
                barHeight
            ),
            Texture2D.whiteTexture
        );

        // GAME OVER SCREEN
        if (isDefeated)
        {
            GUI.color = new Color(0, 0, 0, 0.85f);

            GUI.DrawTexture(
                new Rect(0, 0, Screen.width, Screen.height),
                Texture2D.whiteTexture
            );

            GUIStyle gameOverStyle =
                new GUIStyle(GUI.skin.label);

            gameOverStyle.alignment =
                TextAnchor.MiddleCenter;

            gameOverStyle.fontSize =
                Mathf.Max(30, Screen.height / 12);

            gameOverStyle.fontStyle =
                FontStyle.Bold;

            gameOverStyle.normal.textColor =
                Color.white;

            GUI.color = Color.white;

            GUI.Label(
                new Rect(
                    0,
                    0,
                    Screen.width,
                    Screen.height
                ),
                "GAME OVER\n\nYou Have Been Defeated",
                gameOverStyle
            );
        }

        GUI.color = originalColor;
    }
}
