using UnityEngine;
using System;

public class SoundManager : MonoBehaviour
{
    public static event Action<Vector3, float> OnSoundHeard;

    public static void MakeSound(Vector3 position, float radius)
    {
        OnSoundHeard?.Invoke(position, radius);
    }
}
