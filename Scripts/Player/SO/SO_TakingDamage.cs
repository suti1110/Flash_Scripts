using UnityEngine;

[CreateAssetMenu(fileName = "TakingDamage_Stat", menuName = "Player/Stat/TakingDamage")]
public class SO_TakingDamage : ScriptableObject
{
    [field: SerializeField, Min(0f)]
    public float ActionDuration { get; private set; } = 0.225f;

    [field: SerializeField]
    public int MaxHp { get; private set; } = 3;
}
// SO_TakingDamage은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
