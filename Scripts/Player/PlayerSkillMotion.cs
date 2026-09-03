using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// 스킬 애니메이션의 Humanoid Root Motion 및 3D 이동 오프셋을 추출하여 Rigidbody 물리 이동으로 치환합니다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerSkillMotion : NetworkBehaviour
{
    [SerializeField]
    private Rigidbody _rb;

    [SerializeField]
    private NetworkRigidbody _netRb;

    [SerializeField]
    private Animator _anim;

    private NetworkObject _networkObject;

    private bool _isMotionActive;
    private bool _isAuthorityMotionActive;
    private bool _translateMotionToPhysics;
    private Vector3 _originPosition;
    private Quaternion _originRotation;
    private Vector3 _targetLocalOffset;
    private Quaternion _targetLocalRotation = Quaternion.identity;

    private bool _wasSetKinematicByMotion;
    private AnimatorRootMotionRelay _rootMotionRelay;
    private Vector3 _animInitialLocalPosition;
    private Quaternion _animInitialLocalRotation;

    private bool IsLocallyControlled =>
        _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_netRb == null)
            _netRb = GetComponent<NetworkRigidbody>();
        if (_anim == null)
            _anim = GetComponentInChildren<Animator>();

        if (_anim != null)
        {
            _animInitialLocalPosition = _anim.transform.localPosition;
            _animInitialLocalRotation = _anim.transform.localRotation;

            _rootMotionRelay = _anim.GetComponent<AnimatorRootMotionRelay>();
            if (_rootMotionRelay == null)
                _rootMotionRelay = _anim.gameObject.AddComponent<AnimatorRootMotionRelay>();

            _rootMotionRelay.Bind(this);
        }
    }

    /// <summary>
    /// 스킬 이동을 시작하고 물리 이동 기준 위치와 회전을 저장합니다.
    /// </summary>
    public void StartMotion(
        Vector3 originPosition,
        Quaternion originRotation,
        bool setKinematic = true,
        bool translateMotionToPhysics = true
    )
    {
        _originPosition = originPosition;
        _originRotation = originRotation;
        _targetLocalOffset = Vector3.zero;
        _isAuthorityMotionActive = IsLocallyControlled;
        StartPresentationMotion(translateMotionToPhysics);

        // 플래그가 켜져 있고 Owner 권한 플레이어인 경우에만 외부 Force/중력을 무시하기 위해 Kinematic 활성화
        if (setKinematic && _isAuthorityMotionActive && _rb != null && !_rb.isKinematic)
        {
            if (_netRb != null)
                _netRb.SetIsKinematic(true);
            else
                _rb.isKinematic = true;

            _wasSetKinematicByMotion = true;
        }
    }

    /// <summary>
    /// 모든 피어에서 모델 모션이 네트워크 루트 이동과 중복되지 않도록 표시 상태를 시작합니다.
    /// 실제 Rigidbody 이동과 Kinematic 제어는 Owner에서만 수행됩니다.
    /// </summary>
    public void StartPresentationMotion(bool translateMotionToPhysics)
    {
        if (!_isMotionActive)
        {
            _targetLocalOffset = Vector3.zero;
            _targetLocalRotation = Quaternion.identity;
        }

        _translateMotionToPhysics = translateMotionToPhysics;
        _isMotionActive = true;
    }

    /// <summary>
    /// Timeline 또는 외부에서 계산된 목표 로컬 오프셋을 직접 전달받습니다.
    /// </summary>
    public void SetTargetLocalOffset(Vector3 localOffset)
    {
        _targetLocalOffset = localOffset;
    }

    /// <summary>
    /// <summary>
    /// Timeline 애니메이션 클립의 Root Motion 절대 변위(deltaPosition)를 수신하여 물리 목표 위치로 대입합니다.
    /// (Timeline 환경에서는 deltaPosition이 시작점 대비 전체 누적 변위를 나타내므로 += 대신 = 대입을 수행하여 46프레임 착지 시 정확히 정지합니다)
    /// </summary>
    internal void TryConsumeRootMotionDelta(Vector3 worldDeltaPosition, Quaternion localDeltaRotation)
    {
        if (!_isMotionActive || !_translateMotionToPhysics)
            return;

        // Unity Animator의 deltaRotation은 'Local 공간' 기준의 델타 회전입니다! (deltaPosition은 World 공간)
        // 엉뚱하게 World 변환(Inverse(_originRotation) 등)을 거치면 축이 꼬여서 누워버리는(Pitch/Roll) 현상이 발생합니다.
        _targetLocalRotation = localDeltaRotation;

        // 물리적 이동 오프셋 계산은 서버/오너 권한에서만 수행하여 동기화
        if (_isAuthorityMotionActive)
        {
            _targetLocalOffset = Quaternion.Inverse(_originRotation) * worldDeltaPosition;
        }
    }

    private void Update()
    {
        if (!_isMotionActive || !_translateMotionToPhysics || _anim == null)
            return;

        // 타임라인/애니메이터 평가 전에 이전 프레임(LateUpdate)에서 적용했던 시각적 회전을 초기화
        // 이렇게 해야 타임라인이 회전 델타를 0 기준으로 정상 계산하여 피드백 루프(벌벌 떨리는 현상)가 발생하지 않음
        _anim.transform.localRotation = _animInitialLocalRotation;

        // Model 루트 Transform이 직접 애니메이팅된 경우의 오프셋 감지
        Vector3 curLocalPos = _anim.transform.localPosition;
        Vector3 offsetFromInitial = curLocalPos - _animInitialLocalPosition;
        if (offsetFromInitial.sqrMagnitude > 0.0001f)
        {
            _targetLocalOffset = offsetFromInitial;
        }
    }

    private void LateUpdate()
    {
        // 런타임 물리 이동 중 Model 루트 오브젝트가 이중으로 튀어나가는 것을 방지하기 위해 위치 고정
        // 단, 타임라인에서 넘어온 애니메이션 RootQ(절대 회전)는 Spine 등 하위 뼈대의 올바른 기준축(Yaw)이 되므로 적용
        if (_isMotionActive && _translateMotionToPhysics && _anim != null)
        {
            _anim.transform.localPosition = _animInitialLocalPosition;
            _anim.transform.localRotation = _animInitialLocalRotation * _targetLocalRotation;
        }
    }

    private void FixedUpdate()
    {
        if (
            !_isMotionActive
            || !_isAuthorityMotionActive
            || !_translateMotionToPhysics
            || _rb == null
        )
            return;

        Vector3 targetWorldPosition = _originPosition + (_originRotation * _targetLocalOffset);
        _rb.MovePosition(targetWorldPosition);
        _rb.MoveRotation(_originRotation);
    }

    /// <summary>
    /// 스킬 이동을 중단하고 물리 상태(Kinematic 등)를 정상 복구합니다.
    /// </summary>
    public void StopMotion()
    {
        if (!_isMotionActive && !_wasSetKinematicByMotion)
            return;

        _isMotionActive = false;
        _targetLocalOffset = Vector3.zero;

        if (_anim != null)
            RestoreAnimatorRootTransform();

        // Owner 권한 플레이어의 Kinematic 상태를 안전하게 해제
        if (_wasSetKinematicByMotion)
        {
            _wasSetKinematicByMotion = false;

            if (IsLocallyControlled)
            {
                if (_netRb != null)
                    _netRb.SetIsKinematic(false);
                else if (_rb != null)
                    _rb.isKinematic = false;

                if (_rb != null)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
            }
        }

        _isAuthorityMotionActive = false;
    }

    private void RestoreAnimatorRootTransform()
    {
        if (_anim != null)
        {
            _anim.transform.SetLocalPositionAndRotation(
                _animInitialLocalPosition,
                _animInitialLocalRotation
            );
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        StopMotion();
    }

    private void OnDisable()
    {
        StopMotion();
    }

    public override void OnDestroy()
    {
        StopMotion();
        if (_rootMotionRelay != null)
            _rootMotionRelay.Unbind(this);
        base.OnDestroy();
    }

    private void OnValidate()
    {
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();
        if (_netRb == null)
            _netRb = GetComponent<NetworkRigidbody>();
        if (_anim == null)
            _anim = GetComponentInChildren<Animator>();

        if (_rb == null)
            EditorLog.LogError("PlayerSkillMotion에 Rigidbody가 필요합니다.", this);
        if (_anim == null)
            EditorLog.LogError("PlayerSkillMotion에 Animator가 필요합니다.", this);
    }
}
