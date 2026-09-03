// SO_Attacking은 일반 공격의 동작 시간, 실행 시점, 피해량과 판정 범위를 에셋으로 제공한다.
// 실행 시점은 정규화된 값으로 저장하여 애니메이션 길이가 달라도 동일한 공격 흐름을 재사용할 수 있게 한다.

using UnityEngine;

[CreateAssetMenu(fileName = "Attacking_Stat", menuName = "Player/Stat/Attacking")]
public class SO_Attacking : ScriptableObject
{
    [field: SerializeField, Min(0f)]
    public float ActionDuration { get; private set; } = 0.3f;

    [field: SerializeField, Range(0f, 1f)]
    public float ExecuteTime { get; private set; } = 0.33333334f;

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
