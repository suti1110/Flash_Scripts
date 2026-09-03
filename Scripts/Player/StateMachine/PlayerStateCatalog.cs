using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 상태머신이 사용할 State 설정 원본을 보관한다.
/// 원본은 직접 실행하지 않으며 플레이어마다 복제되어 독립적인 런타임 상태가 된다.
/// </summary>
[CreateAssetMenu(fileName = "PlayerStateCatalog", menuName = "Flash/Player/State Catalog")]
public sealed class PlayerStateCatalog : ScriptableObject
{
    [SerializeField]
    private PlayerState[] _statePrototypes;

    public IReadOnlyList<PlayerState> StatePrototypes => _statePrototypes;

    public void ValidateOrThrow()
    {
        if (!TryValidate(out string errorMessage))
            throw new InvalidOperationException(errorMessage);
    }

    private bool TryValidate(out string errorMessage)
    {
        if (_statePrototypes == null || _statePrototypes.Length == 0)
        {
            errorMessage = "PlayerStateCatalog에 State 원본이 하나도 등록되지 않았습니다.";
            return false;
        }

        HashSet<Type> registeredTypes = new();
        bool hasIdleState = false;

        for (int i = 0; i < _statePrototypes.Length; i++)
        {
            PlayerState prototype = _statePrototypes[i];
            if (prototype == null)
            {
                errorMessage = $"PlayerStateCatalog의 {i}번 State 원본이 비어 있습니다.";
                return false;
            }

            Type stateType = prototype.GetType();
            if (!registeredTypes.Add(stateType))
            {
                errorMessage = $"PlayerStateCatalog에 {stateType.Name} 원본이 중복 등록되었습니다.";
                return false;
            }

            hasIdleState |= prototype is PlayerIdleState;
        }

        if (!hasIdleState)
        {
            errorMessage = "PlayerStateCatalog에 초기 상태인 PlayerIdleState가 없습니다.";
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
