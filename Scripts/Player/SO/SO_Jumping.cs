using UnityEngine;

[CreateAssetMenu(fileName = "Jumping_Stat", menuName = "Player/Stat/Jumping")]
public class SO_Jumping : ScriptableObject
{
    [field: SerializeField]
    public float JumpForce { get; private set; } = 30f;
}
