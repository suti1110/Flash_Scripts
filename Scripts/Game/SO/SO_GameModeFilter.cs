using UnityEngine;

[CreateAssetMenu(fileName = "GameModeFilter", menuName = "ScriptableObjects/GameModeFilter")]
public class SO_GameModeFilter : ScriptableObject
{
    [Tooltip("이 로직이 활성화될 게임 모드들을 선택합니다. (복수 선택 가능)")]
    public GameKind AllowedModes;

    public bool IsAllowed(GameKind currentMode)
    {
        if (currentMode == GameKind.None)
            return false;

        return (AllowedModes & currentMode) != 0;
    }
}
