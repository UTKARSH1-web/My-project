using UnityEngine;

public class ExitTrigger : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Movement movement = other.GetComponent<Movement>();

            if (movement != null && movement.hasKey)
            {
                Debug.Log("You escaped the maze!");
            }
            else
            {
                Debug.Log("The exit is locked. Find the key!");
            }
        }
    }
}