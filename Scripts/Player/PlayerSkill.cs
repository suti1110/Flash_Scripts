using System;
using Unity.Netcode;
using UnityEngine;

public interface ISkill
{
    event Action OnSkillFinished;
    void UseSkill(int skillIndex);
    PlayerInputType GetCurrentSkillConstraints();
    SO_Skill CurrentSkill { get; }
}

[RequireComponent(typeof(EnergyTracker))]
[RequireComponent(typeof(PlayerAnimation))]
[RequireComponent(typeof(PlayerSkillMotion))]
public class PlayerSkill : NetworkBehaviour, ISkill
{
    [SerializeField]
    private SO_SkillSet _skillSet;

    private EnergyTracker _energyTracker;
    private PlayerAnimation _playerAnimation;
    private PlayerSkillMotion _playerSkillMotion;
    private PlayerStateMachine _playerStateMachine;
    private Action<int, Vector3, Quaternion> _networkEffectRequester;
    private Action<int, GameObject, Vector3, Quaternion> _networkTargetEffectRequester;

    private SO_Skill _curCastingSkill;
    public SO_Skill CurrentSkill => _curCastingSkill;
    private readonly PlayerActionStateMachine _actionStateMachine = new();
    private SkillCastIndicatorData _preparedCastIndicator;
    private bool _hasPreparedCastIndicator;
    private GameObject _activeCastIndicator;

    private void Awake()
    {
        _energyTracker = GetComponent<EnergyTracker>();
        _playerAnimation = GetComponent<PlayerAnimation>();
        _playerSkillMotion = GetComponent<PlayerSkillMotion>();
        _playerStateMachine = GetComponent<Player>().StateMachine;
        _networkEffectRequester = RequestSpawnEffect;
        _networkTargetEffectRequester = RequestSpawnTargetEffect;
        _playerStateMachine.StateChanged += HandleStateChanged;
        _actionStateMachine.Started += HandleSkillStarted;
        _actionStateMachine.ExecutionPointReached += SkillExecute;
        _actionStateMachine.Completed += HandleSkillCompleted;
        _actionStateMachine.Cancelled += HandleSkillCancelled;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner)
            return;

        _actionStateMachine.Tick(Time.deltaTime);
    }

    public override void OnDestroy()
    {
        HideCastIndicator();
        base.OnDestroy();
        _playerStateMachine.StateChanged -= HandleStateChanged;
        _actionStateMachine.Started -= HandleSkillStarted;
        _actionStateMachine.ExecutionPointReached -= SkillExecute;
        _actionStateMachine.Completed -= HandleSkillCompleted;
        _actionStateMachine.Cancelled -= HandleSkillCancelled;
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

        ResetPreparedCastIndicator();

        if (_energyTracker.Energy < skill.EnergyCost)
        {
            ShowSkillFailure("Not enough energy.");
            return;
        }

        SkillExecutionContext context = CreateExecutionContext(false);
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

        if (!_energyTracker.TryConsumeEnergy(skill.EnergyCost))
        {
            ShowSkillFailure("Not enough energy.");
        }
        else
        {
            _curCastingSkill = skill;
            _actionStateMachine.Start(skill.ActionDuration, skill.ExecuteTime);
        }
    }

    public event Action OnSkillFinished;

    public PlayerInputType GetCurrentSkillConstraints()
    {
        return _curCastingSkill != null ? _curCastingSkill.ConstrainedInputs : PlayerInputType.None;
    }

    private void HandleSkillStarted()
    {
        if (!_playerStateMachine.TryChangeState<PlayerUsingSkillState>())
        {
            _actionStateMachine.Cancel();
            return;
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
        StopSkillPresentation();
        FinishSkill();
        _curCastingSkill = null;
        ResetPreparedCastIndicator();
    }

    private void HandleSkillCancelled()
    {
        StopSkillPresentation();
        OnSkillFinished?.Invoke();
        _curCastingSkill = null;
        ResetPreparedCastIndicator();
    }

    private void FinishSkill()
    {
        if (_playerStateMachine.IsInState<PlayerUsingSkillState>())
            _playerStateMachine.TryChangeState<PlayerIdleState>();

        OnSkillFinished?.Invoke();
    }

    private void SkillExecute()
    {
        if ((IsSpawned && !IsOwner) || _curCastingSkill == null)
            return;

        StopCastIndicator();

        SkillExecutionContext context = CreateExecutionContext(_hasPreparedCastIndicator);
        _curCastingSkill.ExecuteSkill(context);
    }

    private SkillExecutionContext CreateExecutionContext(bool includePreparedTarget)
    {
        return new SkillExecutionContext(
            gameObject,
            _energyTracker,
            _networkEffectRequester,
            _networkTargetEffectRequester,
            includePreparedTarget,
            _preparedCastIndicator.Position
        );
    }

    public void RequestSpawnEffect(int effectId, Vector3 position, Quaternion rotation)
    {
        RequestSpawnEffect(_curCastingSkill, effectId, position, rotation);
    }

    internal void RequestSpawnEffect(
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
        int effectId,
        GameObject target,
        Vector3 position,
        Quaternion rotation
    )
    {
        RequestSpawnTargetEffect(_curCastingSkill, effectId, target, position, rotation);
    }

    internal void RequestSpawnTargetEffect(
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
        if (
            networkId <= 0
            || target == null
            || !IsFinite(position)
            || !IsFinite(rotation)
        )
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
        AudioManager.SfxPlayAtPoint(skill.CastAudio, transform.position);

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

        PlayTargetEffect(
            networkId,
            effectId,
            targetNetworkObject.gameObject,
            position,
            rotation
        );
    }

    private void PlayTargetEffect(
        int networkId,
        int effectId,
        GameObject target,
        Vector3 position,
        Quaternion rotation
    )
    {
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
            targetEffect.PlayNetworkTargetEffect(
                gameObject,
                target,
                effectId,
                position,
                rotation
            );
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
