using System;
using Unity.Netcode;
using UnityEngine;

public interface ISkill
{
    event Action OnSkillFinished;
    void UseSkill(int skillIndex);
    PlayerInputType GetCurrentSkillConstraints();
    SkillCameraInfluenceAxes GetCurrentCameraInfluenceAxes();
    SO_Skill CurrentSkill { get; }
}

[RequireComponent(typeof(EnergyTracker))]
[RequireComponent(typeof(PlayerAnimation))]
[RequireComponent(typeof(PlayerSkillMotion))]
public class PlayerSkill : NetworkBehaviour, ISkill, IPlayerSkillStatus, ISkillEffectRequester, ITimedSkillController
{
    [SerializeField]
    private SO_SkillSet _skillSet;

    private EnergyTracker _energyTracker;
    private PlayerAnimation _playerAnimation;
    private PlayerSkillMotion _playerSkillMotion;
    private PlayerStateMachine _playerStateMachine;
    private PlayerCamera _playerCamera;
    private PlayerTimedSkillEffects _timedEffects;
    private readonly NetworkList<NetworkTimedSkillInfo> _networkTimedSkills = new();
    private readonly NetworkVariable<float> _externalSilencePower = new();
    private readonly NetworkVariable<float> _externalSilenceImmunity = new();
    private float _localSilencePower;
    private float _localSilenceImmunity;
    private SO_Skill _pendingTimedSkill;
    private int _pendingTimedSkillSlot = -1;
    private int _approvedTimedSkillId;
    private double _approvedTimedSkillExpiresAt;
    private int[] _serverEquippedSkillIds;

    public float SilencePower => Mathf.Max(
        IsSpawned ? _externalSilencePower.Value : _localSilencePower,
        PlayerSilenceSources.GetPower(transform)
    );
    public float SilenceImmunity =>
        (IsSpawned ? _externalSilenceImmunity.Value : _localSilenceImmunity)
        + PlayerSilenceSources.GetImmunity(transform);

    // 침묵 수치가 0이면 영향이 없고, 같은 수치에서는 침묵이 적용된다.
    public bool IsSilenced
    {
        get
        {
            float power = SilencePower;
            return power > 0f && SilenceImmunity <= power;
        }
    }
    public bool IsSilenceImmune => SilenceImmunity > SilencePower;

    private SO_Skill _curCastingSkill;
    public SO_Skill CurrentSkill => _curCastingSkill;
    private readonly PlayerActionStateMachine _actionStateMachine = new();
    private bool _hasExecutedCurrentSkill;
    private SkillCastIndicatorData _preparedCastIndicator;
    private bool _hasPreparedCastIndicator;
    private GameObject _activeCastIndicator;

    private void Awake()
    {
        _energyTracker = GetComponent<EnergyTracker>();
        _playerAnimation = GetComponent<PlayerAnimation>();
        _playerSkillMotion = GetComponent<PlayerSkillMotion>();
        _playerCamera = GetComponent<PlayerCamera>();
        _timedEffects = new PlayerTimedSkillEffects(this, _skillSet, _networkTimedSkills);
        _playerStateMachine = GetComponent<Player>().StateMachine;
        _playerStateMachine.StateChanged += HandleStateChanged;
        _actionStateMachine.Started += HandleSkillStarted;
        _actionStateMachine.ExecutionPointReached += SkillExecute;
        _actionStateMachine.Completed += HandleSkillCompleted;
        _actionStateMachine.Cancelled += HandleSkillCancelled;
    }

    private void Update()
    {
        bool silenced = IsSilenced;
        if (silenced)
        {
            // 서버가 종료 시간을 지워 효과가 영역 밖에서 다시 켜지지 않게 한다.
            // 로컬 플레이는 같은 필드에서 로컬 타이머를 정리한다.
            if (!IsSpawned || IsServer)
                _timedEffects.CancelActiveEffects();

            // Execute 시점까지 기다리지 않고 진행 중인 시전도 즉시 종료한다.
            if (!IsSpawned || IsOwner)
                _actionStateMachine.Cancel();
        }
        else
        {
            // 침묵 중에는 지속 스킬의 공격/회복 콜백을 먼저 실행하지 않는다.
            _timedEffects.Update();
        }

        if (IsSpawned && !IsOwner)
            return;

        _actionStateMachine.Tick(Time.deltaTime);
    }

    public override void OnDestroy()
    {
        HideCastIndicator();
        _timedEffects?.Clear();
        base.OnDestroy();
        _playerStateMachine.StateChanged -= HandleStateChanged;
        _actionStateMachine.Started -= HandleSkillStarted;
        _actionStateMachine.ExecutionPointReached -= SkillExecute;
        _actionStateMachine.Completed -= HandleSkillCompleted;
        _actionStateMachine.Cancelled -= HandleSkillCancelled;
    }

    private void OnEnable() => _timedEffects?.OnEnable();

    public override void OnNetworkSpawn()
    {
        _timedEffects?.OnNetworkSpawn();
        base.OnNetworkSpawn();
        if (IsOwner && _skillSet != null)
        {
            // 메뉴에서 선택한 장착 슬롯을 플레이어 생성 시 한 번만 서버에 등록한다.
            PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
            loadout.InitializeSkills(_skillSet);
            int[] equippedIds = new int[loadout.SkillSlotCount];
            for (int i = 0; i < equippedIds.Length; i++)
                equippedIds[i] = _skillSet.GetSkillNetworkId(loadout.GetSkill(i, _skillSet));
            RegisterSkillLoadoutRpc(equippedIds);
        }
    }

    private void OnDisable()
    {
        _pendingTimedSkill = null;
        _pendingTimedSkillSlot = -1;
        _timedEffects?.OnDisable();
    }

    public override void OnNetworkDespawn()
    {
        _pendingTimedSkill = null;
        _approvedTimedSkillId = 0;
        _serverEquippedSkillIds = null;
        _timedEffects?.OnNetworkDespawn();
        base.OnNetworkDespawn();
    }

    // 다른 기믹은 서버에서 자신의 침묵 수치와 면역 수치를 독립적으로 설정한다.
    // 영역 침묵과 영역 시전자 버프는 이 기본 수치 위에 계산되므로 서로 덮어쓰지 않는다.
    public bool SetExternalSilencePower(float power)
    {
        if (!float.IsFinite(power) || power < 0f || (IsSpawned && !IsServer))
            return false;

        if (IsSpawned)
            _externalSilencePower.Value = power;
        else
            _localSilencePower = power;

        return true;
    }

    public bool SetExternalSilenceImmunity(float immunity)
    {
        if (!float.IsFinite(immunity) || immunity < 0f || (IsSpawned && !IsServer))
            return false;

        if (IsSpawned)
            _externalSilenceImmunity.Value = immunity;
        else
            _localSilenceImmunity = immunity;

        return true;
    }

    public bool IsTimedSkillActive(SO_Skill skill) => _timedEffects.IsActive(skill);

    // 실행 시점에 등록한다. 스킬별 지속 시간과 재사용 규칙은 해당 스킬이 제공한다.
    public void ActivateTimedSkill(SO_Skill skill)
    {
        if (!isActiveAndEnabled || IsSilenced || (IsSpawned && !IsOwner)
            || skill is not ITimedSkill timedSkill || !float.IsFinite(timedSkill.Duration)
            || timedSkill.Duration <= 0f)
            return;

        if (IsSpawned)
            ActivateTimedSkillRpc(skill.NetworkId);
        else
            _timedEffects.ActivateLocal(skill, timedSkill);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void ActivateTimedSkillRpc(int networkId)
    {
        // 클라이언트가 임의의 ID를 보내도 서버가 승인한 시전 한 건만 소비한다.
        if (!isActiveAndEnabled || IsSilenced || networkId != _approvedTimedSkillId
            || NetworkManager.ServerTime.Time > _approvedTimedSkillExpiresAt)
            return;

        _approvedTimedSkillId = 0;
        _timedEffects.ActivateNetwork(networkId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestTimedSkillCastRpc(int networkId)
    {
        SO_Skill skill = _skillSet.GetSkillByNetworkId(networkId);
        if (!isActiveAndEnabled || IsSilenced || skill is not ITimedSkill
            || !IsEquippedOnServer(networkId)
            || !float.IsFinite(skill.EnergyCost) || skill.EnergyCost < 0f
            || _energyTracker.Energy < skill.EnergyCost
            || (skill is ITimedSkill timedSkill && !timedSkill.CanReactivateWhileActive
                && _timedEffects.IsActive(skill))
            || (_approvedTimedSkillId != 0
                && NetworkManager.ServerTime.Time <= _approvedTimedSkillExpiresAt))
        {
            RejectTimedSkillCastRpc(networkId);
            return;
        }

        // 승인 기간은 시전 애니메이션과 왕복 지연만 포함한다. 효과 자체의 지속 시간은 포함하지 않는다.
        _approvedTimedSkillId = networkId;
        _approvedTimedSkillExpiresAt = NetworkManager.ServerTime.Time
            + Math.Max(1d, skill.ActionDuration + 1d);
        ApproveTimedSkillCastRpc(networkId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RegisterSkillLoadoutRpc(int[] equippedIds)
    {
        if (_serverEquippedSkillIds != null || _skillSet == null || equippedIds == null
            || equippedIds.Length != _skillSet.DefaultSkillCount)
            return;

        // 클라이언트가 카탈로그 밖 ID나 중복 장착을 제출하면 전체 스냅샷을 거부한다.
        for (int i = 0; i < equippedIds.Length; i++)
        {
            if (_skillSet.GetSkillByNetworkId(equippedIds[i]) == null)
                return;
            for (int j = 0; j < i; j++)
            {
                if (equippedIds[j] == equippedIds[i])
                    return;
            }
        }

        _serverEquippedSkillIds = equippedIds;
    }

    private bool IsEquippedOnServer(int networkId)
    {
        if (_serverEquippedSkillIds == null)
            return false;
        foreach (int equippedId in _serverEquippedSkillIds)
        {
            if (equippedId == networkId)
                return true;
        }
        return false;
    }

    [Rpc(SendTo.Owner)]
    private void RejectTimedSkillCastRpc(int networkId)
    {
        if (_pendingTimedSkill == null || _pendingTimedSkill.NetworkId != networkId)
            return;

        _pendingTimedSkill = null;
        _pendingTimedSkillSlot = -1;
        ShowSkillFailure("The server rejected this skill cast.");
    }

    [Rpc(SendTo.Owner)]
    private void ApproveTimedSkillCastRpc(int networkId)
    {
        SO_Skill skill = _pendingTimedSkill;
        int slot = _pendingTimedSkillSlot;
        _pendingTimedSkill = null;
        _pendingTimedSkillSlot = -1;
        if (skill == null || skill.NetworkId != networkId || !isActiveAndEnabled
            || IsSilenced || _actionStateMachine.IsRunning
            || PlayerLoadoutState.Instance.GetSkill(slot, _skillSet) != skill
            || _energyTracker.Energy < skill.EnergyCost
            || !skill.CanExecute(CreateExecutionContext(skill, false), out _))
        {
            CancelTimedSkillCastRpc(networkId);
            return;
        }

        StartPreparedSkill(skill);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void CancelTimedSkillCastRpc(int networkId)
    {
        if (_approvedTimedSkillId == networkId)
            _approvedTimedSkillId = 0;
    }

    private void HandleStateChanged(PlayerState previousState, PlayerState currentState)
    {
        if (currentState is not PlayerUsingSkillState)
            _actionStateMachine.Cancel();
    }

    /// <summary>
    /// 스킬셋에 들어있는 스킬을 사용하는 메서드
    /// </summary>
    /// <param name="skillIndex">스킬셋 내에서의 스킬 번호</param>
    public void UseSkill(int skillIndex)
    {
        if (Time.timeScale <= 0f || (IsSpawned && !IsOwner))
            return;

        PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
        loadout.InitializeSkills(_skillSet);

        if (skillIndex < 0 || skillIndex >= loadout.SkillSlotCount)
            return;

        SO_Skill skill = loadout.GetSkill(skillIndex, _skillSet);
        if (skill == null)
            return;

        if (_actionStateMachine.IsRunning)
            return;

        if (_pendingTimedSkill != null)
            return;

        if (IsSilenced)
        {
            ShowSkillFailure("Skills are sealed in the silence field.");
            return;
        }

        ResetPreparedCastIndicator();

        if (_energyTracker.Energy < skill.EnergyCost)
        {
            ShowSkillFailure("Not enough energy.");
            return;
        }

        SkillExecutionContext context = CreateExecutionContext(skill, false);
        if (!skill.CanExecute(context, out string failureMessage))
        {
            ShowSkillFailure(failureMessage);
            return;
        }

        if (skill is ISkillCastIndicator castIndicator)
        {
            if (!castIndicator.TryGetCastIndicator(context, out _preparedCastIndicator))
            {
                ShowSkillFailure("Unable to determine the skill's target location.");
                return;
            }

            _hasPreparedCastIndicator = true;
        }

        if (IsSpawned && skill is ITimedSkill)
        {
            // 서버가 기력과 스킬 ID를 확인한 뒤에만 소유자가 비용을 지불하고 시전을 시작한다.
            _pendingTimedSkill = skill;
            _pendingTimedSkillSlot = skillIndex;
            RequestTimedSkillCastRpc(skill.NetworkId);
            return;
        }

        StartPreparedSkill(skill);
    }

    private void StartPreparedSkill(SO_Skill skill)
    {
        if (!_energyTracker.TryConsumeEnergy(skill.EnergyCost))
        {
            if (IsSpawned && skill is ITimedSkill)
                CancelTimedSkillCastRpc(skill.NetworkId);
            ShowSkillFailure("Not enough energy.");
        }
        else
        {
            _curCastingSkill = skill;
            _hasExecutedCurrentSkill = false;
            _actionStateMachine.Start(skill.ActionDuration, skill.ExecuteTime);
        }
    }

    public event Action OnSkillFinished;

    public PlayerInputType GetCurrentSkillConstraints()
    {
        return _curCastingSkill != null ? _curCastingSkill.ConstrainedInputs : PlayerInputType.None;
    }

    public SkillCameraInfluenceAxes GetCurrentCameraInfluenceAxes()
    {
        return _curCastingSkill != null
            ? _curCastingSkill.GetCameraInfluenceAxes(_hasExecutedCurrentSkill)
            : SkillCameraInfluenceAxes.None;
    }

    private void HandleSkillStarted()
    {
        if (!_playerStateMachine.TryChangeState<PlayerUsingSkillState>())
        {
            _actionStateMachine.Cancel();
            return;
        }

        if (_curCastingSkill != null)
        {
            _playerCamera?.SetSkillCameraView(
                _curCastingSkill.CameraViewMode,
                _curCastingSkill.CameraBlendDuration
            );
        }

        bool setKinematic = _curCastingSkill != null && _curCastingSkill.IsKinematicDuringSkill;
        bool translateMotion =
            _curCastingSkill == null || _curCastingSkill.TranslateMotionToRigidbody;
        if (_playerSkillMotion != null)
        {
            _playerSkillMotion.StartMotion(
                transform.position,
                transform.rotation,
                setKinematic,
                translateMotion
            );
        }

        int networkId = _skillSet.GetSkillNetworkId(_curCastingSkill);
        if (networkId <= 0)
            return;

        if (IsSpawned)
        {
            PlaySkillPresentationRpc(
                networkId,
                NetworkManager.ServerTime.Time,
                _hasPreparedCastIndicator,
                _preparedCastIndicator.Position,
                _preparedCastIndicator.Rotation,
                _preparedCastIndicator.Radius
            );
        }
        else
        {
            PlaySkillPresentation(
                _curCastingSkill,
                0d,
                _hasPreparedCastIndicator,
                _preparedCastIndicator.Position,
                _preparedCastIndicator.Rotation,
                _preparedCastIndicator.Radius
            );
        }
    }

    private void HandleSkillCompleted()
    {
        RestoreDefaultCameraView();
        StopSkillPresentation();
        FinishSkill();
        _curCastingSkill = null;
        _hasExecutedCurrentSkill = false;
        ResetPreparedCastIndicator();
    }

    private void HandleSkillCancelled()
    {
        if (IsSpawned && IsOwner && !_hasExecutedCurrentSkill
            && _curCastingSkill is ITimedSkill)
            CancelTimedSkillCastRpc(_curCastingSkill.NetworkId);
        RestoreDefaultCameraView();
        StopSkillPresentation();
        OnSkillFinished?.Invoke();
        _curCastingSkill = null;
        _hasExecutedCurrentSkill = false;
        ResetPreparedCastIndicator();
    }

    private void FinishSkill()
    {
        if (_playerStateMachine.IsInState<PlayerUsingSkillState>())
            _playerStateMachine.TryChangeState<PlayerIdleState>();

        OnSkillFinished?.Invoke();
    }

    private void RestoreDefaultCameraView()
    {
        float blendDuration = _curCastingSkill != null ? _curCastingSkill.CameraBlendDuration : 0f;
        _playerCamera?.SetSkillCameraView(SkillCameraViewMode.ThirdPerson, blendDuration);
    }

    private void SkillExecute()
    {
        if ((IsSpawned && !IsOwner) || _curCastingSkill == null)
            return;

        // 시전 도중 영역에 진입한 경우에도 실행을 허용하지 않는다.
        if (IsSilenced)
        {
            _actionStateMachine.Cancel();
            return;
        }

        StopCastIndicator();

        _hasExecutedCurrentSkill = true;

        SkillExecutionContext context = CreateExecutionContext(_curCastingSkill, _hasPreparedCastIndicator);
        _curCastingSkill.ExecuteSkill(context);
    }

    private SkillExecutionContext CreateExecutionContext(SO_Skill skill, bool includePreparedTarget)
    {
        return new SkillExecutionContext(
            gameObject,
            _energyTracker,
            skill,
            this,
            this,
            includePreparedTarget,
            _preparedCastIndicator.Position
        );
    }

    public void RequestSpawnEffect(
        SO_Skill skill,
        int effectId,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (IsSpawned && !IsOwner)
            return;

        int networkId = _skillSet.GetSkillNetworkId(skill);
        if (networkId <= 0 || !IsFinite(position) || !IsFinite(rotation))
            return;

        if (IsSpawned)
        {
            PlayEffectRpc(networkId, effectId, position, rotation);
        }
        else
        {
            PlayEffect(networkId, effectId, position, rotation);
        }
    }

    private static void ShowSkillFailure(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            message = "This skill cannot be used right now.";

        MessageOnUI.ShowMessage(message, MessageType.Warning);
    }

    public void RequestSpawnTargetEffect(
        SO_Skill skill,
        int effectId,
        GameObject target,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (IsSpawned && !IsOwner)
            return;

        int networkId = _skillSet.GetSkillNetworkId(skill);
        if (networkId <= 0 || target == null || !IsFinite(position) || !IsFinite(rotation))
        {
            return;
        }

        if (IsSpawned)
        {
            if (
                !target.TryGetComponent(out NetworkObject targetNetworkObject)
                || !targetNetworkObject.IsSpawned
            )
            {
                return;
            }

            PlayTargetEffectRpc(
                networkId,
                effectId,
                new NetworkObjectReference(targetNetworkObject),
                position,
                rotation
            );
        }
        else
        {
            PlayTargetEffect(networkId, effectId, target, position, rotation);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlaySkillPresentationRpc(
        int networkId,
        double serverStartTime,
        bool hasCastIndicator,
        Vector3 indicatorPosition,
        Quaternion indicatorRotation,
        float indicatorRadius
    )
    {
        SO_Skill skill = _skillSet.GetSkillByNetworkId(networkId);
        if (skill == null || _playerAnimation == null)
            return;

        double elapsedTime = Math.Max(0d, NetworkManager.ServerTime.Time - serverStartTime);
        PlaySkillPresentation(
            skill,
            elapsedTime,
            hasCastIndicator
                && IsFinite(indicatorPosition)
                && IsFinite(indicatorRotation)
                && IsFinite(indicatorRadius)
                && indicatorRadius >= 0f,
            indicatorPosition,
            indicatorRotation,
            indicatorRadius
        );
    }

    private void PlaySkillPresentation(
        SO_Skill skill,
        double elapsedTime,
        bool hasCastIndicator,
        Vector3 indicatorPosition,
        Quaternion indicatorRotation,
        float indicatorRadius
    )
    {
        if (skill is ISkillCastAudioSettings audioSettings)
        {
            if (audioSettings.PlayCastAudioOnSkillStart)
            {
                AudioManager.SfxPlayAtPoint(
                    skill.CastAudio,
                    transform.position,
                    audioSettings.CastAudioVolume,
                    audioSettings.CastAudioMinDistance,
                    audioSettings.CastAudioMaxDistance
                );
            }
        }
        else
        {
            AudioManager.SfxPlayAtPoint(skill.CastAudio, transform.position);
        }

        if (_playerSkillMotion != null)
            _playerSkillMotion.StartPresentationMotion(skill.TranslateMotionToRigidbody);
        PlaySkillAnimation(skill, elapsedTime);
        HideCastIndicator();

        if (
            !hasCastIndicator
            || skill is not ISkillCastIndicator castIndicator
            || castIndicator.CastIndicatorPrefab == null
        )
        {
            return;
        }

        _activeCastIndicator = Instantiate(
            castIndicator.CastIndicatorPrefab,
            indicatorPosition,
            indicatorRotation
        );

        if (_activeCastIndicator.TryGetComponent(out SkillRangeDecal rangeDecal))
            rangeDecal.Initialize(indicatorRadius, (float)elapsedTime);
    }

    private void PlaySkillAnimation(SO_Skill skill, double elapsedTime)
    {
        if (_playerAnimation == null)
            return;

        switch (skill.AnimationKind)
        {
            case SkillAnimationKind.AnimationClip:
                _playerAnimation.PlaySkillClip(
                    skill.SkillClip,
                    elapsedTime,
                    skill.ActionDuration,
                    skill.TransitionDuration
                );
                break;
            case SkillAnimationKind.TimelineAsset:
                _playerAnimation.PlaySkillTimeline(
                    skill.SkillTimeline,
                    elapsedTime,
                    skill.ActionDuration
                );
                break;
        }
    }

    private void StopSkillPresentation()
    {
        StopLocalSkillPresentation();

        if (IsSpawned)
            StopSkillPresentationRpc();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void StopSkillPresentationRpc()
    {
        StopLocalSkillPresentation();
    }

    private void StopLocalSkillPresentation()
    {
        if (_playerSkillMotion != null)
            _playerSkillMotion.StopMotion();
        if (_playerAnimation != null)
            _playerAnimation.StopSkillPresentation();
        HideCastIndicator();
    }

    private void StopCastIndicator()
    {
        if (IsSpawned)
            StopCastIndicatorRpc();
        else
            HideCastIndicator();
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void StopCastIndicatorRpc()
    {
        HideCastIndicator();
    }

    private void HideCastIndicator()
    {
        if (_activeCastIndicator == null)
            return;

        Destroy(_activeCastIndicator);
        _activeCastIndicator = null;
    }

    private void ResetPreparedCastIndicator()
    {
        _preparedCastIndicator = default;
        _hasPreparedCastIndicator = false;
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayEffectRpc(int networkId, int effectId, Vector3 position, Quaternion rotation)
    {
        if (!IsFinite(position) || !IsFinite(rotation))
            return;

        PlayEffect(networkId, effectId, position, rotation);
    }

    private void PlayEffect(int networkId, int effectId, Vector3 position, Quaternion rotation)
    {
        if (IsSilenced)
            return;

        SO_Skill skill = _skillSet.GetSkillByNetworkId(networkId);
        if (
            skill is ISkillNetworkEffect networkEffect
            && networkEffect.IsNetworkEffectRequestValid(gameObject, effectId, position, rotation)
        )
        {
            networkEffect.PlayNetworkEffect(gameObject, effectId, position, rotation);
        }
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayTargetEffectRpc(
        int networkId,
        int effectId,
        NetworkObjectReference targetReference,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (
            !IsFinite(position)
            || !IsFinite(rotation)
            || !targetReference.TryGet(out NetworkObject targetNetworkObject)
        )
        {
            return;
        }

        PlayTargetEffect(networkId, effectId, targetNetworkObject.gameObject, position, rotation);
    }

    private void PlayTargetEffect(
        int networkId,
        int effectId,
        GameObject target,
        Vector3 position,
        Quaternion rotation
    )
    {
        if (IsSilenced)
            return;

        SO_Skill skill = _skillSet.GetSkillByNetworkId(networkId);
        if (
            skill is ISkillNetworkTargetEffect targetEffect
            && targetEffect.IsNetworkTargetEffectRequestValid(
                gameObject,
                target,
                effectId,
                position,
                rotation
            )
        )
        {
            targetEffect.PlayNetworkTargetEffect(gameObject, target, effectId, position, rotation);
        }
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }

    private static bool IsFinite(Quaternion value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void OnValidate()
    {
        if (!_skillSet)
        {
            EditorLog.LogError("SO_SkillSet이 할당되지 않았습니다!!!", this);
        }
    }
}
