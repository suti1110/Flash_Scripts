using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerAttack : NetworkBehaviour, IAttackable
{
    [SerializeField]
    private SO_Attacking _attacking;

    private readonly PlayerActionStateMachine _actionStateMachine = new();
    private PlayerStateMachine _playerStateMachine;

    [SerializeField]
    private int _maxTarget = 100;
    private Collider[] _targets;
    private readonly Dictionary<Rigidbody, AttackTarget> _damageableTargets = new();
    private double _nextServerAttackTime;

    private Rigidbody _attackerBody;
    private PlayerCamera _playerCamera;
    private AudioSource _localHitConfirmAudioSource;
    private Coroutine _hitStopRoutine;
    private Camera _hitStopCamera;
    private bool _isHitStopApplied;
    private bool _cameraWasEnabled;
    private bool _listenerWasPaused;

    private readonly struct AttackTarget
    {
        public AttackTarget(PlayerDamage damageable, Vector3 hitPosition)
        {
            Damageable = damageable;
            HitPosition = hitPosition;
        }

        public PlayerDamage Damageable { get; }
        public Vector3 HitPosition { get; }
    }

    private void Awake()
    {
        _targets = new Collider[_maxTarget];
        _playerStateMachine = GetComponent<Player>().StateMachine;
        _attackerBody = GetComponent<Rigidbody>();
        _playerCamera = GetComponent<PlayerCamera>();

        _actionStateMachine.Started += HandleAttackStarted;
        _actionStateMachine.ExecutionPointReached += AttackHit;
        _actionStateMachine.Completed += HandleAttackCompleted;
        _playerStateMachine.StateChanged += HandleStateChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        _actionStateMachine.Tick(Time.deltaTime);
    }

    public void Attack()
    {
        if (
            Time.timeScale <= 0f
            || (IsSpawned && !IsOwner)
            || _actionStateMachine.IsRunning
        )
            return;

        _actionStateMachine.Start(_attacking.ActionDuration, _attacking.ExecuteTime);
    }

    private void HandleAttackStarted()
    {
        if (!_playerStateMachine.TryChangeState<PlayerAttackingState>())
        {
            _actionStateMachine.Cancel();
            return;
        }

        AudioManager audioManager = AudioManager.Instance;
        AudioManager.SfxPlay(audioManager != null ? audioManager.Container?.Attack : null);
    }

    private void AttackHit()
    {
        if (IsSpawned && !IsOwner)
            return;

        if (IsSpawned)
        {
            AttackHitRpc();
            return;
        }

        ApplyAttackHitOnAuthority();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void AttackHitRpc()
    {
        double now = Time.realtimeSinceStartupAsDouble;
        double minimumInterval = Mathf.Max(0.05f, _attacking.ActionDuration * 0.8f);
        if (now < _nextServerAttackTime)
            return;

        _nextServerAttackTime = now + minimumInterval;
        ApplyAttackHitOnAuthority();
    }

    private void ApplyAttackHitOnAuthority()
    {
        if (IsSpawned && !IsServer)
            return;

        if (TryGetComponent(out PlayerDamage attackerDamage) && !attackerDamage.IsAlive)
            return;

        // 서버에서는 호스트가 Player, 게스트가 OtherPlayer 레이어이므로
        // 공격자 관점과 무관하게 양쪽 플레이어 레이어를 모두 검색한 뒤 자신을 제외한다.
        int targetLayers =
            _attacking.TargetLayer | LayerMask.GetMask("Player", "OtherPlayer");

        Vector3 horizontalVelocity = _attackerBody != null
            ? Vector3.ProjectOnPlane(_attackerBody.linearVelocity, Vector3.up)
            : Vector3.zero;
        float effectiveRange = _attacking.GetEffectiveRange(horizontalVelocity.magnitude);

        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            effectiveRange,
            _targets,
            targetLayers
        );

        _damageableTargets.Clear();

        // 한 객체 내에 여러 콜라이더가 있을 수 있으므로, Rigidbody 기준으로 중복 제거
        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _targets[i].attachedRigidbody;

            if (!rb || _damageableTargets.ContainsKey(rb))
                continue;

            if (rb.transform == transform)
                continue;

            if (rb.TryGetComponent(out PlayerDamage damageable))
            {
                Vector3 attackerCenter = _attackerBody != null
                    ? _attackerBody.worldCenterOfMass
                    : transform.position + Vector3.up;
                Vector3 hitPosition = Vector3.Lerp(attackerCenter, rb.worldCenterOfMass, 0.5f);
                _damageableTargets[rb] = new AttackTarget(damageable, hitPosition);
            }
        }

        bool hitAnyTarget = false;

        // 공격 범위 내의 모든 IDamageable 객체에 데미지와 넉백 적용
        foreach (var target in _damageableTargets)
        {
            Vector3 direction = (target.Key.transform.position - transform.position).normalized;

            if (
                Vector3.Dot(transform.forward, direction) >= _attacking.RangeDot
                && target.Value.Damageable.IsAlive
            )
            {
                CustomRoomSettings roomSettings = RelayManager.Instance != null
                    ? RelayManager.Instance.CurrentRoomSettings
                    : CustomRoomSettings.Default;
                int attackDamage = RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom
                    ? roomSettings.AttackDamage
                    : _attacking.Damage;
                bool wasLethal = target.Value.Damageable.TakeDamageOnServer(
                    attackDamage,
                    direction * (_attacking.KnockbackForce * roomSettings.KnockbackMultiplier)
                );
                hitAnyTarget = true;

                if (IsSpawned)
                    PlayAttackHitEffectRpc(
                        new NetworkObjectReference(target.Value.Damageable.NetworkObject),
                        target.Value.HitPosition,
                        -direction,
                        !wasLethal
                    );
                else
                {
                    if (!wasLethal)
                    {
                        target.Value.Damageable
                            .GetComponent<PlayerAnimation>()
                            ?.PlayImmediateDamageReaction(-direction);
                    }
                    PlayAttackHitEffect(target.Value.HitPosition, -direction);
                }
            }
        }

        if (!hitAnyTarget)
            return;

        if (IsSpawned)
            PlayAttackerHitStopRpc();
        else
            PlayLocalHitStop();
    }

    [Rpc(SendTo.Everyone)]
    private void PlayAttackHitEffectRpc(
        NetworkObjectReference targetReference,
        Vector3 position,
        Vector3 direction,
        bool playDamageReaction
    )
    {
        if (!IsFinite(position) || !IsFinite(direction))
            return;

        if (
            playDamageReaction
            && targetReference.TryGet(out NetworkObject targetNetworkObject)
            && targetNetworkObject.TryGetComponent(out PlayerAnimation targetAnimation)
        )
        {
            targetAnimation.PlayImmediateDamageReaction(direction);
        }

        PlayAttackHitEffect(position, direction);
    }

    private void PlayAttackHitEffect(Vector3 position, Vector3 direction)
    {
        float delay = Mathf.Max(0f, _attacking.HitStopDelay);
        if (delay <= 0f)
        {
            SpawnAttackHitEffect(position, direction);
            return;
        }

        StartCoroutine(PlayAttackHitEffectAfterDelay(position, direction, delay));
    }

    private IEnumerator PlayAttackHitEffectAfterDelay(
        Vector3 position,
        Vector3 direction,
        float delay
    )
    {
        yield return new WaitForSecondsRealtime(delay);
        SpawnAttackHitEffect(position, direction);
    }

    private void SpawnAttackHitEffect(Vector3 position, Vector3 direction)
    {
        GameObject hitEffectPrefab = _attacking.HitEffectPrefab;
        if (hitEffectPrefab == null)
            return;

        Quaternion rotation = direction.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(direction.normalized)
            : Quaternion.identity;
        GameObject hitEffectObject = Instantiate(hitEffectPrefab, position, rotation);
        if (hitEffectObject.TryGetComponent(out AttackHitEffect hitEffect))
        {
            float holdDuration = !IsSpawned || IsOwner ? _attacking.HitStopDuration : 0f;
            hitEffect.Play(holdDuration);
        }
    }

    [Rpc(SendTo.Owner)]
    private void PlayAttackerHitStopRpc()
    {
        PlayLocalHitStop();
    }

    private void PlayLocalHitStop()
    {
        if (_hitStopRoutine != null)
        {
            StopCoroutine(_hitStopRoutine);
            RestoreLocalHitStop();
        }

        _hitStopCamera = _playerCamera != null ? _playerCamera.MainCamera : null;
        PlayLocalHitConfirmAudio();
        _hitStopRoutine = StartCoroutine(HitStopRoutine(_attacking.HitStopDuration));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        // 피격 트리거가 실제 리액션 자세로 전환될 시간을 준 뒤 그 화면을 유지한다.
        float delay = Mathf.Max(0f, _attacking.HitStopDelay);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        AttackHitEffect hitEffectPrefab = _attacking.HitEffectPrefab != null
            ? _attacking.HitEffectPrefab.GetComponent<AttackHitEffect>()
            : null;
        float effectMovementDuration = hitEffectPrefab != null
            ? hitEffectPrefab.MovementDuration
            : 0f;
        if (effectMovementDuration > 0f)
            yield return new WaitForSecondsRealtime(effectMovementDuration);

        // 전기가 0.05초 뻗어 정지한 프레임을 렌더한 뒤 그 충격 자세를 고정한다.
        yield return new WaitForEndOfFrame();

        _cameraWasEnabled = _hitStopCamera != null && _hitStopCamera.enabled;
        _listenerWasPaused = AudioListener.pause;
        _isHitStopApplied = true;

        // 3D 카메라 화면을 RenderTexture로 캡처하여 UI 뒤 배경 레이어에 고정합니다.
        if (_playerCamera != null)
            _playerCamera.BeginFreezeFrame();

        if (_hitStopCamera != null)
            _hitStopCamera.enabled = false;

        AudioManager.PauseSharedSfxForHitStop();
        AudioListener.pause = true;

        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, duration));

        RestoreLocalHitStop();
        _playerCamera?.PlayAttackHitFeedback(
            _attacking.HitShakeStrength,
            _attacking.HitShakeDuration
        );
        _hitStopRoutine = null;
    }

    private void RestoreLocalHitStop()
    {
        if (!_isHitStopApplied)
            return;

        if (_hitStopCamera != null)
            _hitStopCamera.enabled = _cameraWasEnabled;

        // 프리즈 프레임 표시를 종료하고 임시 텍스처를 반환합니다.
        if (_playerCamera != null)
            _playerCamera.EndFreezeFrame();

        AudioListener.pause = _listenerWasPaused;
        AudioManager.ResumeSharedSfxAfterHitStop();
        _hitStopCamera = null;
        _isHitStopApplied = false;
    }

    private void PlayLocalHitConfirmAudio()
    {
        AudioManager audioManager = AudioManager.Instance;
        AudioClip hitClip = audioManager != null ? audioManager.Container?.Hit : null;
        if (hitClip == null || _hitStopCamera == null)
            return;

        if (_localHitConfirmAudioSource == null)
        {
            _localHitConfirmAudioSource = _hitStopCamera.gameObject.AddComponent<AudioSource>();
            AudioManager.ConfigureListenerSfxSource(_localHitConfirmAudioSource);
            _localHitConfirmAudioSource.ignoreListenerPause = true;
        }

        _localHitConfirmAudioSource.Stop();
        _localHitConfirmAudioSource.clip = hitClip;
        _localHitConfirmAudioSource.volume = 1f;
        _localHitConfirmAudioSource.Play();
    }

    private static bool IsFinite(Vector3 value)
    {
        return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    private void HandleAttackCompleted()
    {
        if (_playerStateMachine.IsInState<PlayerAttackingState>())
            _playerStateMachine.TryChangeState<PlayerIdleState>();
    }

    private void HandleStateChanged(PlayerState previousState, PlayerState currentState)
    {
        if (currentState is not PlayerAttackingState)
            _actionStateMachine.Cancel();
    }

    private void OnValidate()
    {
        if (!_attacking)
        {
            EditorLog.LogError("SO_Attacking이 할당되지 않았습니다!", this);
        }
    }

    private void OnDisable()
    {
        if (_hitStopRoutine != null)
        {
            StopCoroutine(_hitStopRoutine);
            _hitStopRoutine = null;
        }

        RestoreLocalHitStop();
    }

    public override void OnDestroy()
    {
        if (_hitStopRoutine != null)
            StopCoroutine(_hitStopRoutine);
        RestoreLocalHitStop();

        _actionStateMachine.Started -= HandleAttackStarted;
        _actionStateMachine.ExecutionPointReached -= AttackHit;
        _actionStateMachine.Completed -= HandleAttackCompleted;

        if (_playerStateMachine != null)
            _playerStateMachine.StateChanged -= HandleStateChanged;

        base.OnDestroy();
    }
}
