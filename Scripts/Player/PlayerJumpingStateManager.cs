using System.Collections.Generic;
using UnityEngine;

public enum PlayerJumpingState
{
    Idle,
    Jumping,
    Falling,
    UsingSkill,
    Dead,
}

public class PlayerJumpingStateLifetime : MonoBehaviour
{
    private void OnDestroy() => PlayerJumpingStateManager.Instance.Remove(gameObject);
}

public class PlayerJumpingStateData
{
    private PlayerJumpingState _jumpingState;
    public PlayerJumpingState JumpingState
    {
        get => _jumpingState;
        set
        {
            if (_jumpingState == value)
                return;

            if (IsLocked && _priorityEachState[_jumpingState] > _priorityEachState[value])
                return;

            _jumpingState = value;
            OnJumpingStateChanged?.Invoke(_jumpingState);
        }
    }

    public bool IsLocked { get; set; }

    private static readonly Dictionary<PlayerJumpingState, int> _priorityEachState = new()
    {
        { PlayerJumpingState.Idle, 0 },
        { PlayerJumpingState.Jumping, 0 },
        { PlayerJumpingState.Falling, 0 },
        { PlayerJumpingState.UsingSkill, 100 },
        { PlayerJumpingState.Dead, 10000 },
    };

    public event System.Action<PlayerJumpingState> OnJumpingStateChanged;
}

public class PlayerJumpingStateManager
{
    public static PlayerJumpingStateManager Instance { get; } = new();

    private readonly Dictionary<GameObject, PlayerJumpingStateData> _playerJumpingStates = new();

    public PlayerJumpingStateData this[GameObject go]
    {
        get
        {
            if (!_playerJumpingStates.TryGetValue(go, out PlayerJumpingStateData state))
            {
                go.AddComponent<PlayerJumpingStateLifetime>();
                _playerJumpingStates[go] = new PlayerJumpingStateData();
                state = _playerJumpingStates[go];
            }
            return state;
        }
    }

    public void Remove(GameObject go)
    {
        if (_playerJumpingStates.ContainsKey(go))
        {
            _playerJumpingStates.Remove(go);
        }
    }
}
