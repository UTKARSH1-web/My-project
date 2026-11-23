using UnityEngine;

public class SoundTester : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("Sound made");
            SoundManager.MakeSound(transform.position, 35f);
        }
    }
}
