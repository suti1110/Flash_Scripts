using UnityEngine;

[CreateAssetMenu(fileName = "Jumping_Stat", menuName = "Player/Stat/Jumping")]
public class SO_Jumping : ScriptableObject
{
    [field: SerializeField]
    public float JumpForce { get; private set; } = 30f;
}
// SO_Jumping은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
