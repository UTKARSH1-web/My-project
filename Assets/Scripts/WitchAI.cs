using UnityEngine;

public class WitchAI : MonoBehaviour
{
    [Header("Witch Scream")]
    public AudioClip screamClip;
    public AudioSource witchAudioSource;
    public float screamRange = 5f;

    private bool hasScreamed = false;
    public enum WitchState
    {
        Calm,
        Agitated,
        Rage
    }

    public WitchState currentState = WitchState.Calm;

    [Header("Player Detection")]
    public Transform player;
    public float detectionRange = 10f;
    public float escapeDistance = 15f;
    [Header("Movement")]
    public float chaseSpeed = 2f;
    public float rageSpeed = 4f;

    [Header("Attack")]
    public float attackRange = 1.5f;
    public float attackCooldown = 2f;

    private float attackTimer = 0f;

    [Header("Agitation")]
    public float agitation = 0f;
    public float maxAgitation = 100f;

    [Header("State Thresholds")]
    public float agitatedThreshold = 30f;
    public float rageThreshold = 70f;

    [Header("Agitation Changes")]
    public float agitationIncrease = 10f;
    public float agitationDecrease = 5f;
    public float walkingNoise = 5f;
    public float runningNoise = 15f;
    public float jumpingNoise = 30f;

    void CheckScream()
    {
        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= screamRange && !hasScreamed)
        {
            if (witchAudioSource != null && screamClip != null)
            {
                witchAudioSource.PlayOneShot(screamClip);
            }

            hasScreamed = true;
        }
        else if (distance > screamRange)
        {
            hasScreamed = false;
        }
    }

    void Start()
    {
        if (player != null)
        {
            float distance = Vector3.Distance(transform.position, player.position);

            if (distance <= screamRange)
            {
                hasScreamed = true;
            }
        }
    }

    void Update()
    {
        if (player == null)
            return;

        PlayerHealth health = player.GetComponent<PlayerHealth>();

        if (health == null || health.currentHealth <= 0)
            return;

        DetectPlayer();
        CheckScream();
        UpdateState();
        MoveTowardsPlayer();
        AttackPlayer();
        UpdateAppearance();
    }

    void DetectPlayer()
    {
        if (player == null)
            return;

        float distance = Vector3.Distance(transform.position, player.position);
        Debug.Log("Witch distance from player: " + distance);

        if (distance >= escapeDistance)
        {
            // Player escaped! Witch loses interest.
            agitation -= agitationDecrease * Time.deltaTime;
        }
        else if (distance <= detectionRange)
        {
            // Player is nearby.
            agitation += agitationIncrease * Time.deltaTime;
        }
        else
        {
            // Player is outside normal detection range.
            agitation -= agitationDecrease * Time.deltaTime;
        }

        agitation = Mathf.Clamp(agitation, 0f, maxAgitation);
    }

    void UpdateState()
    {
        WitchState newState;

        if (agitation >= rageThreshold)
        {
            newState = WitchState.Rage;
        }
        else if (agitation >= agitatedThreshold)
        {
            newState = WitchState.Agitated;
        }
        else
        {
            newState = WitchState.Calm;
        }

        if (newState != currentState)
        {
            currentState = newState;
            Debug.Log("Witch State: " + currentState);
        }
    }

    void UpdateAppearance()
    {
        Renderer witchRenderer = GetComponent<Renderer>();

        if (witchRenderer == null)
            return;

        switch (currentState)
        {
            case WitchState.Calm:
                witchRenderer.material.color = Color.green;
                break;

            case WitchState.Agitated:
                witchRenderer.material.color = Color.yellow;
                break;

            case WitchState.Rage:
                witchRenderer.material.color = Color.red;
                break;
        }
    }

    public void HearSound(float soundIntensity)
    {
        if (player == null)
            return;

        float distance = Vector3.Distance(transform.position, player.position);

        // Ignore sounds when the player has escaped.
        if (distance >= escapeDistance)
            return;

        agitation += soundIntensity;

        agitation = Mathf.Clamp(
            agitation,
            0f,
            maxAgitation
        );

        Debug.Log("Witch heard a sound! Agitation: " + agitation);
    }

    void MoveTowardsPlayer()
    {
        if (player == null)
            return;

        float distance = Vector3.Distance(transform.position, player.position);

        // Stop chasing when the player escapes.
        if (distance >= escapeDistance)
            return;

        if (currentState == WitchState.Calm)
            return;

        float speed = currentState == WitchState.Rage
            ? rageSpeed
            : chaseSpeed;

        Vector3 direction = (player.position - transform.position).normalized;
        direction.y = 0f;

        transform.position += direction * speed * Time.deltaTime;
    }

    void AttackPlayer()
    {
        if (player == null)
            return;

        if (currentState != WitchState.Rage)
            return;

        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= attackRange)
        {
            attackTimer -= Time.deltaTime;

            if (attackTimer <= 0f)
            {
                Debug.Log("Witch attacks the Player!");
                PlayerHealth health = player.GetComponent<PlayerHealth>();

                if (health != null)
                {
                    health.TakeDamage(20);
                }
                attackTimer = attackCooldown;
            }
        }
    }
}
