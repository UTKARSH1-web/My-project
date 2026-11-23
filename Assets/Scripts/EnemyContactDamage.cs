
using UnityEngine;

public class EnemyContactDamage : MonoBehaviour
{
    [Header("Damage Settings")]
    public int damagePerHit = 10;
    public float damageInterval = 1f;

    private float nextDamageTime = 0f;

    private void OnTriggerEnter(Collider other)
    {
        DealDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.time >= nextDamageTime)
        {
            DealDamage(other);
        }
    }

    private void DealDamage(Collider other)
    {
        PlayerHealth playerHealth =
            other.GetComponentInParent<PlayerHealth>();

        if (playerHealth != null)
        {
            playerHealth.TakeDamage(damagePerHit);

            nextDamageTime = Time.time + damageInterval;
        }
    }
}
