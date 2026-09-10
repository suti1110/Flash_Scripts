using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Timeline;

public enum SkillAnimationKind
{
    None,
    AnimationClip,
    TimelineAsset,
}

public enum SkillExitCondition
{
    AnimationExit,
    Custom,
}

public interface ISkillNetworkEffect
{
    bool IsNetworkEffectRequestValid(
        GameObject caster,
        int effectId,
        Vector3 position,
        Quaternion rotation
    );
    void PlayNetworkEffect(GameObject caster, int effectId, Vector3 position, Quaternion rotation);
}

public interface ISkillNetworkTargetEffect
{
    bool IsNetworkTargetEffectRequestValid(
        GameObject caster,
        GameObject target,
        int effectId,
        Vector3 position,
        Quaternion rotation
    );
    void PlayNetworkTargetEffect(
        GameObject caster,
        GameObject target,
        int effectId,
        Vector3 position,
        Quaternion rotation
    );
}

public interface ISkillCastIndicator
{
    GameObject CastIndicatorPrefab { get; }
    bool TryGetCastIndicator(
        in SkillExecutionContext context,
        out SkillCastIndicatorData indicator
    );
}

public interface ISkillCastAudioSettings
{
    bool PlayCastAudioOnSkillStart { get; }
    float CastAudioVolume { get; }
    float CastAudioMinDistance { get; }
    float CastAudioMaxDistance { get; }
}

public readonly struct SkillCastIndicatorData
{
    public SkillCastIndicatorData(Vector3 position, Quaternion rotation, float radius)
    {
        Position = position;
        Rotation = rotation;
        Radius = radius;
    }

    public Vector3 Position { get; }
    public Quaternion Rotation { get; }
    public float Radius { get; }
}

public readonly struct SkillExecutionContext
{
    private readonly GameObject _caster;
    private readonly Action<int, Vector3, Quaternion> _networkEffectRequester;
    private readonly Action<int, GameObject, Vector3, Quaternion> _networkTargetEffectRequester;
    private readonly bool _hasTargetPosition;
    private readonly Vector3 _targetPosition;

    public SkillExecutionContext(
        GameObject caster,
        IEnergyTracker energyTracker,
        Action<int, Vector3, Quaternion> networkEffectRequester,
        Action<int, GameObject, Vector3, Quaternion> networkTargetEffectRequester,
        bool hasTargetPosition = false,
        Vector3 targetPosition = default
    )
    {
        _caster = caster;
        EnergyTracker = energyTracker;
        _networkEffectRequester = networkEffectRequester;
        _networkTargetEffectRequester = networkTargetEffectRequester;
        _hasTargetPosition = hasTargetPosition;
        _targetPosition = targetPosition;
    }

    public Transform Transform => _caster != null ? _caster.transform : null;
    public IEnergyTracker EnergyTracker { get; }

    public bool TryGetComponent<T>(out T component)
        where T : Component
    {
        component = null;
        return _caster != null && _caster.TryGetComponent(out component);
    }

    public void RequestNetworkEffect(int effectId, Vector3 position, Quaternion rotation)
    {
        _networkEffectRequester?.Invoke(effectId, position, rotation);
    }

    public void RequestNetworkTargetEffect(
        int effectId,
        GameObject target,
        Vector3 position,
        Quaternion rotation
    )
    {
        _networkTargetEffectRequester?.Invoke(effectId, target, position, rotation);
    }

    public bool TryGetTargetPosition(out Vector3 position)
    {
        position = _targetPosition;
        return _hasTargetPosition;
    }
}

public abstract class SO_Skill : ScriptableObject
{
    [field: SerializeField, Min(1)]
    [field: Tooltip(
        "네트워크에서 이 스킬을 식별하는 고정 ID입니다. 다른 스킬과 중복되면 안 됩니다."
    )]
    public int NetworkId { get; private set; }

    [field: SerializeField]
    public float EnergyCost { get; private set; }

    [field: SerializeField]
    public Sprite SkillIcon { get; private set; }

    [field: SerializeField]
    public string SkillName { get; private set; }

    [field: SerializeField, TextArea]
    public string SkillDescription { get; private set; }

    [field: SerializeField, InspectorName("시전 효과음")]
    public AudioClip CastAudio { get; private set; }

    [field: SerializeField, InspectorName("애니메이션 종류")]
    public SkillAnimationKind AnimationKind { get; private set; }

    [field:
        SerializeField,
        InspectorName("애니메이션 클립"),
        NaughtyAttributes.ShowIf("AnimationKind", SkillAnimationKind.AnimationClip)
    ]
    public AnimationClip SkillClip { get; private set; }

    [field:
        SerializeField,
        Min(0f),
        InspectorName("전환 시간 (Transition Duration)"),
        Tooltip("스킬 시작 및 종료 시 기본 동작과 부드럽게 섞이는(CrossFade) 시간(초)입니다."),
        NaughtyAttributes.ShowIf("AnimationKind", SkillAnimationKind.AnimationClip)
    ]
    public float TransitionDuration { get; private set; } = 0.15f;

    [field:
        SerializeField,
        InspectorName("타임라인 에셋"),
        NaughtyAttributes.ShowIf("AnimationKind", SkillAnimationKind.TimelineAsset)
    ]
    public TimelineAsset SkillTimeline { get; private set; }

    [field: SerializeField, InspectorName("종료 조건")]
    [field: Tooltip(
        "AnimationExit은 선택한 애니메이션 에셋의 전체 길이를 사용하고, Custom은 직접 입력한 시간을 사용합니다."
    )]
    public SkillExitCondition ExitCondition { get; private set; } = SkillExitCondition.Custom;

    [SerializeField]
    [FormerlySerializedAs("<ActionDuration>k__BackingField")]
    [Min(0f)]
    [InspectorName("액션 지속 시간")]
    [Tooltip("스킬 액션의 전체 시간을 직접 지정합니다.")]
    [NaughtyAttributes.ShowIf("ExitCondition", SkillExitCondition.Custom)]
    private float _customActionDuration;

    [field:
        SerializeField,
        InspectorName("시전 중 물리 고정 (Kinematic)"),
        Tooltip(
            "스킬 애니메이션 재생 동안 외력과 중력을 무시하도록 Rigidbody를 Kinematic 상태로 전환합니다."
        ),
        NaughtyAttributes.HideIf("AnimationKind", SkillAnimationKind.None)
    ]
    public bool IsKinematicDuringSkill { get; private set; }

    [field:
        SerializeField,
        InspectorName("모델 이동을 물리 이동으로 치환"),
        Tooltip(
            "애니메이션에 포함된 모델의 3D 위치 이동을 Rigidbody 물리 이동(히트박스 이동)으로 자동 변환할지 결정합니다."
        ),
        NaughtyAttributes.HideIf("AnimationKind", SkillAnimationKind.None)
    ]
    public bool TranslateMotionToRigidbody { get; private set; } = true;

    [field:
        SerializeField,
        InspectorName("카메라 회전 분리 (Detach Camera Rotation)"),
        Tooltip("애니메이션 재생 중 플레이어의 회전이 카메라 회전을 따라가지 않도록 고정합니다."),
        NaughtyAttributes.HideIf("AnimationKind", SkillAnimationKind.None)
    ]
    public bool DetachCameraRotationDuringSkill { get; private set; } = true;

    public float ActionDuration =>
        ExitCondition == SkillExitCondition.AnimationExit
            ? GetSelectedAnimationDuration()
            : _customActionDuration;

    [field: SerializeField, Range(0f, 1f)]
    [field: Tooltip("액션 진행도의 어느 시점(0~1)에 스킬 로직을 발동할지 결정합니다.")]
    public float ExecuteTime { get; private set; } = 0.5f;

    /// <summary>
    /// 에너지를 소모하고 액션을 시작하기 전에 스킬을 사용할 수 있는지 검사합니다.
    /// </summary>
    public virtual bool CanExecute(in SkillExecutionContext context, out string failureMessage)
    {
        failureMessage = null;
        return true;
    }

    /// <summary>
    /// 발동할 스킬의 구현
    /// </summary>
    public abstract void ExecuteSkill(in SkillExecutionContext context);

    /// <summary>
    /// 스킬 발동 중 제한될 조작들 (비트마스크)
    /// </summary>
    [field: SerializeField]
    public PlayerInputType ConstrainedInputs { get; private set; } = PlayerInputType.All;

    protected virtual void OnValidate()
    {
        if (NetworkId <= 0)
        {
            EditorLog.LogError("스킬 NetworkId는 1 이상의 고유한 값이어야 합니다.", this);
        }

        bool hasSelectedAnimation = AnimationKind switch
        {
            SkillAnimationKind.AnimationClip => SkillClip != null,
            SkillAnimationKind.TimelineAsset => SkillTimeline != null,
            _ => false,
        };
        if (ExitCondition == SkillExitCondition.AnimationExit && !hasSelectedAnimation)
        {
            EditorLog.LogError(
                "AnimationExit 종료 조건에는 AnimationClip 또는 TimelineAsset이 필요합니다.",
                this
            );
        }
        else if (
            ExitCondition == SkillExitCondition.Custom
            && hasSelectedAnimation
            && _customActionDuration <= 0f
        )
        {
            EditorLog.LogError(
                "애니메이션 연출이 있는 Custom 스킬은 액션 지속 시간이 0보다 커야 합니다.",
                this
            );
        }

        if (!ConstrainedInputs.HasFlag(PlayerInputType.UsingSkill))
        {
            EditorLog.LogError("스킬은 반드시 UsingSkill을 제한해야 합니다!!!", this);
        }
    }

    private float GetSelectedAnimationDuration()
    {
        double duration = AnimationKind switch
        {
            SkillAnimationKind.AnimationClip when SkillClip != null => SkillClip.length,
            SkillAnimationKind.TimelineAsset when SkillTimeline != null => SkillTimeline.duration,
            _ => 0d,
        };

        if (double.IsNaN(duration) || double.IsInfinity(duration))
            return 0f;

        return Mathf.Max(0f, (float)duration);
    }
}
