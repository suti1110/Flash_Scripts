using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 점프 보조 상태머신이 사용할 State 설정 원본을 보관한다.
/// 메인 PlayerState와 동시에 동작하는 직교 상태이므로 별도의 Catalog로 관리한다.
/// </summary>
[CreateAssetMenu(
    fileName = "PlayerJumpingStateCatalog",
    menuName = "Flash/Player/Jumping State Catalog"
)]
public sealed class PlayerJumpingStateCatalog : ScriptableObject
{
    [SerializeField]
    private PlayerJumpingState[] _statePrototypes;

    public IReadOnlyList<PlayerJumpingState> StatePrototypes => _statePrototypes;

    public void ValidateOrThrow()
    {
        if (!TryValidate(out string errorMessage))
            throw new InvalidOperationException(errorMessage);
    }

    private bool TryValidate(out string errorMessage)
    {
        if (_statePrototypes == null || _statePrototypes.Length == 0)
        {
            errorMessage = "PlayerJumpingStateCatalog에 State 원본이 하나도 등록되지 않았습니다.";
            return false;
        }

        HashSet<Type> registeredTypes = new();
        bool hasIdleState = false;

        for (int i = 0; i < _statePrototypes.Length; i++)
        {
            PlayerJumpingState prototype = _statePrototypes[i];
            if (prototype == null)
            {
                errorMessage = $"PlayerJumpingStateCatalog의 {i}번 State 원본이 비어 있습니다.";
                return false;
            }

            Type stateType = prototype.GetType();
            if (!registeredTypes.Add(stateType))
            {
                errorMessage =
                    $"PlayerJumpingStateCatalog에 {stateType.Name} 원본이 중복 등록되었습니다.";
                return false;
            }

            hasIdleState |= prototype is PlayerJumpingIdleState;
        }

        if (!hasIdleState)
        {
            errorMessage =
                "PlayerJumpingStateCatalog에 초기 상태인 PlayerJumpingIdleState가 없습니다.";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private void OnValidate()
    {
        if (!TryValidate(out string errorMessage))
            Debug.LogError(errorMessage, this);
    }
}
