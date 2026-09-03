using Unity.Netcode;
using UnityEngine;

public sealed class PlayerMapInteraction : NetworkBehaviour
{
    // 클라이언트는 우클릭 의도만 전송하고, 탐색·거리·방향·소유 상태 검증은 서버가 수행한다.
    [SerializeField]
    private SO_Throwing _throwing;

    [SerializeField, Min(0.5f)]
    private float _interactionRange = 2.5f;

    [SerializeField, Min(0.1f)]
    private float _interactionRadius = 0.75f;

    [SerializeField]
    private Transform _holdParent;

    [SerializeField]
    private Vector3 _holdOffset = new(0f, 1.2f, 1.25f);

    private GrabbableObject _heldObject;
    private Player _player;
    private readonly PlayerActionStateMachine _throwActionStateMachine = new();
    private bool _isThrowPending;
    private bool _isLocalInteractionCancelled;

    private void Awake()
    {
        _player = GetComponent<Player>();
        _player.StateMachine.StateChanged += HandlePlayerStateChanged;
        _throwActionStateMachine.Started += HandleThrowStarted;
        _throwActionStateMachine.ExecutionPointReached += HandleThrowExecutionPointReached;
        _throwActionStateMachine.Completed += HandleThrowCompleted;
        _throwActionStateMachine.Cancelled += HandleThrowCancelled;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        _throwActionStateMachine.Tick(Time.deltaTime);
    }

    // 잡은 물체의 실제 부모와 배치 기준을 한 곳에서 제공한다.
    // 프리팹 이전 버전이나 테스트 객체에서 참조가 비어 있으면 플레이어 루트를 안전한 기본값으로 사용한다.
    public Transform HoldParent => _holdParent != null ? _holdParent : transform;
    public Vector3 HoldPosition => HoldParent.TransformPoint(_holdOffset);
    public Quaternion HoldRotation => HoldParent.rotation;
    public bool IsHoldingObject => _heldObject != null;

    public void Interact()
    {
        if (Time.timeScale <= 0f || (IsSpawned && !IsOwner))
            return;

        // 이 입력 이후에 다른 State가 먼저 성립하는지 추적하여 지연 도착한 투척 시작 RPC를 폐기한다.
        _isLocalInteractionCancelled = false;

        if (IsSpawned)
            InteractRpc();
        else
            InteractOnAuthority();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void InteractRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        InteractOnAuthority();
    }

    private void InteractOnAuthority()
    {
        // 서버가 이미 들고 있는 물체를 기억하므로 같은 입력을 잡기와 던지기 사이에서 전환할 수 있다.
        if (_heldObject != null)
        {
            BeginThrowOnAuthority();
            return;
        }

        Vector3 origin = transform.position + Vector3.up;
        Collider[] hits = Physics.OverlapSphere(
            origin + transform.forward * (_interactionRange * 0.5f),
            _interactionRange * 0.5f + _interactionRadius,
            Physics.AllLayers,
            QueryTriggerInteraction.Collide
        );

        GrabbableObject nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            // 콜라이더가 자식에 있어도 하나의 GrabbableObject 루트로 정규화한다.
            GrabbableObject candidate = hits[i].GetComponentInParent<GrabbableObject>();
            if (candidate == null || !candidate.CanBeGrabbedBy(this))
                continue;

            Vector3 offset = candidate.transform.position - origin;
            float distance = offset.magnitude;
            if (distance > _interactionRange || distance >= nearestDistance)
                continue;

            float facing = distance > 0.001f
                ? Vector3.Dot(transform.forward, offset / distance)
                : 1f;
            if (facing < 0.15f)
                continue;

            nearest = candidate;
            nearestDistance = distance;
        }

        nearest?.Grab(this);
    }

    internal void SetHeldObject(GrabbableObject grabbable)
    {
        _heldObject = grabbable;
    }

    internal void ClearHeldObject(GrabbableObject grabbable)
    {
        if (_heldObject == grabbable)
        {
            _heldObject = null;
            _isThrowPending = false;
        }
    }

    internal void DropHeldObject()
    {
        _isThrowPending = false;
        _heldObject?.Drop();
    }

    // PlayerActionStateMachine의 발동 지점에서만 호출된다. 클라이언트는 실행 의도만 보내고 실제 투척은 서버가 검증한다.
    private void ExecutePendingThrow()
    {
        if (IsSpawned)
            ExecutePendingThrowRpc();
        else
            ExecutePendingThrowOnAuthority();
    }

    // 발동 전에 다른 State가 투척을 끊었을 때 서버가 보관 중인 대기를 해제한다.
    private void CancelPendingThrow()
    {
        if (!IsSpawned || IsServer)
        {
            _isThrowPending = false;
            return;
        }

        CancelPendingThrowRpc();
    }

    private void BeginThrowOnAuthority()
    {
        if (_isThrowPending || _heldObject == null)
            return;

        _isThrowPending = true;
        EnterThrowingStateOnOwner();
    }

    private void EnterThrowingStateOnOwner()
    {
        // 투척 성립은 서버가 검증하고, 입력·애니메이션 State를 소유한 클라이언트에 상태 진입만 요청한다.
        if (IsSpawned)
            EnterThrowingStateRpc();
        else
            TryStartThrowAction();
    }

    [Rpc(SendTo.Owner)]
    private void EnterThrowingStateRpc()
    {
        TryStartThrowAction();
    }

    private void TryStartThrowAction()
    {
        if (
            _throwing == null
            || _isLocalInteractionCancelled
            || _throwActionStateMachine.IsRunning
        )
        {
            CancelPendingThrow();
            return;
        }

        _throwActionStateMachine.Start(_throwing.ActionDuration, _throwing.ExecuteTime);
    }

    private void HandleThrowStarted()
    {
        if (
            _player == null
            || !_player.StateMachine.IsInitialized
            || !_player.StateMachine.TryChangeState<PlayerThrowingState>()
        )
        {
            _throwActionStateMachine.Cancel();
        }
    }

    private void HandleThrowExecutionPointReached()
    {
        ExecutePendingThrow();
    }

    private void HandleThrowCompleted()
    {
        if (
            _player != null
            && _player.StateMachine.IsInitialized
            && _player.StateMachine.IsInState<PlayerThrowingState>()
        )
        {
            _player.StateMachine.TryChangeState<PlayerIdleState>();
        }
    }

    private void HandleThrowCancelled()
    {
        CancelPendingThrow();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ExecutePendingThrowRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
            return;

        ExecutePendingThrowOnAuthority();
    }

    private void ExecutePendingThrowOnAuthority()
    {
        if (!_isThrowPending || _heldObject == null || _throwing == null)
            return;

        // 일반 모드는 SO 원본을 사용하고 Custom Room에서는 서버가 동기화한 방 설정을 실제 투척 속도로 사용한다.
        float throwSpeed =
            RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom
                ? RelayManager.Instance.CurrentRoomSettings.ThrowPower
                : _throwing.ThrowSpeed;

        // 각도는 방 설정에 포함하지 않고 SO의 조작감 설정을 항상 사용한다.
        // 플레이어 기울기의 영향을 제거한 수평 전방을 기준으로 월드 위쪽 발사각을 만든다.
        Vector3 horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (horizontalForward.sqrMagnitude <= Mathf.Epsilon)
            horizontalForward = transform.forward.normalized;

        float throwAngleRadians = _throwing.ThrowAngle * Mathf.Deg2Rad;
        Vector3 throwDirection =
            horizontalForward * Mathf.Cos(throwAngleRadians)
            + Vector3.up * Mathf.Sin(throwAngleRadians);

        // Throw가 성공하면 GrabbableObject가 ClearHeldObject를 호출하여 대기 상태도 원자적으로 끝낸다.
        if (!_heldObject.Throw(throwDirection, throwSpeed))
            _isThrowPending = false;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelPendingThrowRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == OwnerClientId)
            _isThrowPending = false;
    }

    private void HandlePlayerStateChanged(PlayerState previousState, PlayerState currentState)
    {
        if (currentState is not PlayerThrowingState)
            _throwActionStateMachine.Cancel();

        // Throw State에 진입하기 전에 공격·피격 등 다른 State가 먼저 성립하면 지연 도착한 투척 시작도 폐기한다.
        if (
            currentState is PlayerIdleState
            || currentState is PlayerThrowingState
            || (IsSpawned && !IsOwner && !IsServer)
        )
        {
            return;
        }

        _isLocalInteractionCancelled = true;
        CancelPendingThrow();
    }

    public override void OnNetworkDespawn()
    {
        // 플레이어가 물체를 든 채 이탈해도 서버 Rigidbody가 Kinematic으로 남지 않게 한다.
        if (MapPropAuthority.CanSimulate)
            DropHeldObject();

        _isThrowPending = false;

        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        if (_player != null)
            _player.StateMachine.StateChanged -= HandlePlayerStateChanged;

        _throwActionStateMachine.Started -= HandleThrowStarted;
        _throwActionStateMachine.ExecutionPointReached -= HandleThrowExecutionPointReached;
        _throwActionStateMachine.Completed -= HandleThrowCompleted;
        _throwActionStateMachine.Cancelled -= HandleThrowCancelled;

        base.OnDestroy();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(
            transform.position + Vector3.up + transform.forward * (_interactionRange * 0.5f),
            _interactionRange * 0.5f + _interactionRadius
        );
    }

    private void OnValidate()
    {
        if (_throwing == null)
            EditorLog.LogError("PlayerMapInteraction에 SO_Throwing이 설정되지 않았습니다.", this);

        if (_holdParent == null)
            _holdParent = transform;

        if (GetComponent<Player>() == null)
            EditorLog.LogError("PlayerMapInteraction과 같은 오브젝트에 Player가 필요합니다.", this);
    }
}
// PlayerMapInteraction은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
