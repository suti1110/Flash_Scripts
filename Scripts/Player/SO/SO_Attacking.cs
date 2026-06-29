using UnityEngine;

[CreateAssetMenu(fileName = "Attacking_Stat", menuName = "Player/Stat/Attacking")]
public class SO_Attacking : ScriptableObject
{
    [field: SerializeField]
    public int Damage { get; private set; } = 1;

    [field: SerializeField]
    public float Range { get; private set; }

    [field: SerializeField]
    public float KnockbackForce { get; private set; }

    // Forward Attack Range(Unit: Degree)
    [SerializeField, Range(10, 360)]
    private float _rangeDegree = 45;

    // Euler Angle To Dot
    public float RangeDot
    {
        get { return Mathf.Cos(_rangeDegree / 2f * Mathf.Deg2Rad); }
    }

    [field: SerializeField]
    public LayerMask TargetLayer { get; private set; }
}
