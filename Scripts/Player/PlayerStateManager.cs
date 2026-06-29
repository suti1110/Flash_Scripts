using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerState
{
    Idle,
    Attacking,
    TakingDamage,
    UsingSkill,
    Dead,
}

public class PlayerStateLifetime : MonoBehaviour
{
    private void OnDestroy() => PlayerStateManager.Instance.Remove(gameObject);
}

public class PlayerStateData
{
    private PlayerState _state;
    public PlayerState State
    {
        get => _state;
        set
        {
            if (_state == value)
                return;

            if (IsLocked && _priorityEachState[_state] > _priorityEachState[value])
                return;

            _state = value;

            OnStateChanged?.Invoke(_state);
        }
    }

    public bool IsLocked { get; set; }

    private static readonly Dictionary<PlayerState, int> _priorityEachState = new()
    {
        { PlayerState.Idle, 0 },
        { PlayerState.Attacking, 0 },
        { PlayerState.TakingDamage, 0 },
        { PlayerState.UsingSkill, 100 },
        { PlayerState.Dead, 10000 },
    };

    public event Action<PlayerState> OnStateChanged;
}

public class PlayerStateManager
{
    public static PlayerStateManager Instance { get; } = new();

    private readonly Dictionary<GameObject, PlayerStateData> _playerStates = new();

    public PlayerStateData this[GameObject go]
    {
        get
        {
            if (!_playerStates.TryGetValue(go, out PlayerStateData state))
            {
                go.AddComponent<PlayerStateLifetime>();
                _playerStates[go] = new PlayerStateData();
                state = _playerStates[go];
            }
            return state;
        }
    }

    public void Remove(GameObject go)
    {
        if (_playerStates.ContainsKey(go))
        {
            _playerStates.Remove(go);
        }
    }
}
