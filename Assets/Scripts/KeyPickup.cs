
using UnityEngine;
using TMPro;

public class KeyPickup : MonoBehaviour
{
    public GameObject keyFoundUI;

    void Start()
    {
        TMP_Text[] allTexts = Resources.FindObjectsOfTypeAll<TMP_Text>();

        foreach (TMP_Text text in allTexts)
        {
            if (text.gameObject.name == "KeyFound" &&
                text.gameObject.scene.IsValid())
            {
                keyFoundUI = text.gameObject;
                break;
            }
        }

        if (keyFoundUI == null)
            Debug.LogError("KeyFound UI could not be found!");
        else
            Debug.Log("KeyFound UI successfully connected!");

        Debug.Log("KeyPickup attached to: " + gameObject.name);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            Movement movement = other.GetComponent<Movement>();

            if (movement != null)
                movement.hasKey = true;

            if (keyFoundUI != null)
                keyFoundUI.SetActive(true);

            Destroy(gameObject);

            Debug.Log("Key collected!");
        }
    }
}
