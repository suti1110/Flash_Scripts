using UnityEngine;

[CreateAssetMenu(fileName = "SFXContainer", menuName = "Sound/SFXContainer")]
public class SO_SFXContainer : ScriptableObject
{
    [field: SerializeField]
    public AudioClip MouseClick { get; private set; }

    [field: SerializeField]
    public AudioClip Attack { get; private set; }

    [field: SerializeField]
    public AudioClip Hit { get; private set; }

    [field: SerializeField]
    public AudioClip Roulette { get; private set; }

    [field: SerializeField]
    public AudioClip Select { get; private set; }
}
