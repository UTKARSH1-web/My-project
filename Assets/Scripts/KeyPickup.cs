using UnityEngine;

public class KeyPickup : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            other.GetComponent<Movement>().hasKey = true;
            Destroy(gameObject);
            Debug.Log("Key collected!");
        }
    }
}
