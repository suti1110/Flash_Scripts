using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 소유자가 처리한 함정 상태를 다른 피어의 데칼·먼지 표현에 전달한다.
/// 위치·점프 횟수·피격 판정은 기존 PlayerTrappedState가 계속 담당한다.
/// </summary>
public sealed class PlayerPitPresentation : NetworkBehaviour
{
    // 위치를 먼저 동기화한 뒤 열림 상태를 적용한다. 늦게 접속한 피어도 현재 구덩이를 볼 수 있다.
    private readonly NetworkVariable<Vector3> _pitPosition = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> _trapped = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private EscapePit _currentPit;

    public override void OnNetworkSpawn()
    {
        _trapped.OnValueChanged += OnTrappedChanged;
        if (!IsOwner && _trapped.Value)
            ApplyRemoteState(false);
    }

    public override void OnNetworkDespawn()
    {
        _trapped.OnValueChanged -= OnTrappedChanged;
        ClearPresentation(false);
    }

    private void OnDisable() => ClearPresentation(false);

    private void Update()
    {
        // 플레이어 스폰이 맵 소품 활성화보다 빠른 경우에만 연결을 다시 시도한다.
        if (IsSpawned && !IsOwner && _trapped.Value && _currentPit == null)
            ApplyRemoteState(false);
    }

    internal void Begin(EscapePit pit)
    {
        if (IsSpawned && !IsOwner)
            return;
        ClearPresentation(false);
        _currentPit = pit;
        pit.SetOccupied(this, true);
        if (IsSpawned)
        {
            _pitPosition.Value = pit.TrapPosition;
            _trapped.Value = true;
        }
    }

    internal void Struggle()
    {
        if (_currentPit == null || (IsSpawned && !IsOwner))
            return;
        _currentPit.ShowStruggle();
        // 지속 상태와 달리 입력마다 발생하는 먼지는 신뢰성 있는 이벤트로 전달한다.
        if (IsSpawned)
            StruggleRpc(_currentPit.TrapPosition);
    }

    internal void End()
    {
        if (IsSpawned && !IsOwner)
            return;
        ClearPresentation(true);
        if (IsSpawned)
            _trapped.Value = false;
    }

    private void OnTrappedChanged(bool previous, bool current)
    {
        if (!IsOwner)
            ApplyRemoteState(true);
    }

    private void ApplyRemoteState(bool playEffect)
    {
        if (!_trapped.Value)
        {
            ClearPresentation(playEffect);
            return;
        }
        EscapePit pit = EscapePit.FindAt(_pitPosition.Value);
        if (pit == null || pit == _currentPit)
            return;
        ClearPresentation(false);
        _currentPit = pit;
        pit.SetOccupied(this, true, playEffect);
    }

    private void ClearPresentation(bool playEffect)
    {
        if (_currentPit != null)
            _currentPit.SetOccupied(this, false, playEffect);
        _currentPit = null;
    }

    [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
    private void StruggleRpc(Vector3 pitPosition)
    {
        // 표현 이벤트일 뿐 물리 상태를 바꾸지 않는다. 현재 맵에 존재하는 소품만 찾는다.
        EscapePit pit = EscapePit.FindAt(pitPosition);
        if (pit != null)
            pit.ShowStruggle();
    }
}
