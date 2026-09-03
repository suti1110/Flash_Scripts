using UnityEngine;

[CreateAssetMenu(fileName = "GameModeFilter", menuName = "ScriptableObjects/게임/게임 모드 필터")]
public class SO_GameModeFilter : ScriptableObject
{
    [Tooltip("이 로직이 활성화될 게임 모드들을 선택합니다. (복수 선택 가능)")]
    [InspectorName("허용 게임 모드")]
    public GameKind AllowedModes;

    public bool IsAllowed(GameKind currentMode)
    {
        if (currentMode == GameKind.None)
            return false;

        return (AllowedModes & currentMode) != 0;
    }
}
// SO_GameModeFilter은 인스펙터에서 조정하는 게임 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
