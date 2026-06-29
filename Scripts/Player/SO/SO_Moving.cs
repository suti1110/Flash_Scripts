using UnityEngine;

[CreateAssetMenu(fileName = "Moving_Stat", menuName = "Player/Stat/Moving")]
public class SO_Moving : ScriptableObject
{
    [field: SerializeField]
    public float MoveSpeed { get; private set; } = 30f;

    [field: SerializeField]
    public float LightSpeed { get; private set; } = 1000f;
}
