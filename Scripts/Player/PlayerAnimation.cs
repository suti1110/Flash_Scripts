using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Serialization;
using UnityEngine.Timeline;

public static class PlayerAnimationHash
{
    public static readonly int DirX = Animator.StringToHash("DirX");
    public static readonly int DirY = Animator.StringToHash("DirY");
    public static readonly int IsGrounded = Animator.StringToHash("IsGround");
    public static readonly int IsJumping = Animator.StringToHash("IsJumping");
    public static readonly int IsAttacking = Animator.StringToHash("IsAttacking");
    public static readonly int IsTakingDamage = Animator.StringToHash("IsTakingDamage");
    public static readonly int IsTrapped = Animator.StringToHash("IsTrapped");
    public static readonly int IsTrapJumping = Animator.StringToHash("IsTrapJumping");
    public static readonly int IsThrowing = Animator.StringToHash("IsThrowing");
}

public static class AnimationLayerManager
{
    // 부드럽게 변경
    public static void SetLayerWeight(this Animator anim, int layer, float weight, float duration)
    {
        string tweenId = anim.GetEntityId().ToString() + "_" + layer;

        DOTween.Kill(tweenId);

        DOTween
            .To(
                () => anim.GetLayerWeight(layer),
                x => anim.SetLayerWeight(layer, x),
                weight,
                duration
            )
            .SetId(tweenId)
            .SetLink(anim.gameObject, LinkBehaviour.KillOnDestroy);
    }
}

[RequireComponent(typeof(Player))]
public class PlayerAnimation : MonoBehaviour
{
    [SerializeField]
    private Animator _anim;

    [Header("Action (Attack/Throw) Animation")]
    [SerializeField]
    [FormerlySerializedAs("_attackLayerIndex")]
    private int _actionLayerIndex = 1;

    [SerializeField, Min(0f)]
    [FormerlySerializedAs("_attackLayerTransitionDuration")]
    private float _actionLayerTransitionDuration = 0.1f;

    private NetworkObject _networkObject;
    private OwnerNetworkAnimator _networkAnimator;
    private IPlayerMovingInput _playerMovingInput;
    private bool _isLocallyControlled;

    // --- Playables API 변수 ---
    private PlayableGraph _skillGraph;
    private AnimationLayerMixerPlayable _skillMixer;
    private AnimationClipPlayable _currentSkillPlayable;
    private AnimatorControllerPlayable _skillControllerPlayable;
    private PlayableDirector _skillDirector;
    private TimelineAsset _activeSkillTimeline;
    private double _skillStartTime;
    private double _skillEndTime;
    private float _skillActionDuration;
    private float _skillTransitionDuration;
    private bool _isSkillPresentationPlaying;

    private InputAction _horizontal;
    private InputAction _vertical;

    [Header("InputAnimation")]
    [SerializeField]
    private float _inputSensitivity = 3f;

    private float _currentDirX;
    private float _currentDirY;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
        _networkAnimator = GetComponent<OwnerNetworkAnimator>();
        _skillDirector = GetComponent<PlayableDirector>();
        if (_skillDirector == null)
            _skillDirector = gameObject.AddComponent<PlayableDirector>();

        _skillDirector.playOnAwake = false;
        _skillDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
    }

    private void Start()
    {
        _isLocallyControlled =
            _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;

        if (_actionLayerIndex >= 0 && _actionLayerIndex < _anim.layerCount)
            _anim.SetLayerWeight(_actionLayerIndex, 0f);

        if (_isLocallyControlled)
        {
            _playerMovingInput = GetComponent<IPlayerMovingInput>();
            _horizontal = _playerMovingInput.Moving.Move.Horizontal;
            _vertical = _playerMovingInput.Moving.Move.Vertical;
        }
    }

    // 연속적으로 바뀔 수 있는 요소들
    private void Update()
    {
        if (_isSkillPresentationPlaying)
        {
            double currentTime = Time.timeAsDouble;
            if (currentTime >= _skillEndTime)
            {
                StopSkillPresentation();
            }
            else if (_skillMixer.IsValid() && _skillTransitionDuration > 0f)
            {
                // 스킬 클립 믹서 크로스페이드 가중치 계산
                double elapsedFromStart = currentTime - _skillStartTime;
                double remainingToEnd = _skillEndTime - currentTime;

                float weight = 1f;
                if (elapsedFromStart < _skillTransitionDuration)
                {
                    weight = Mathf.Clamp01((float)(elapsedFromStart / _skillTransitionDuration));
                }
                else if (remainingToEnd < _skillTransitionDuration)
                {
                    weight = Mathf.Clamp01((float)(remainingToEnd / _skillTransitionDuration));
                }

                _skillMixer.SetInputWeight(0, 1f - weight);
                _skillMixer.SetInputWeight(1, weight);
            }
        }

        if (!_isLocallyControlled)
            return;

        _currentDirX = Mathf.MoveTowards(
            _currentDirX,
            _horizontal.ReadValue<float>(),
            _inputSensitivity * Time.deltaTime
        );
        _currentDirY = Mathf.MoveTowards(
            _currentDirY,
            _vertical.ReadValue<float>(),
            _inputSensitivity * Time.deltaTime
        );

        _anim.SetFloat(PlayerAnimationHash.DirX, _currentDirX);
        _anim.SetFloat(PlayerAnimationHash.DirY, _currentDirY);

        // 스킬 블렌딩 중인 베이스 컨트롤러에도 이동 파라미터 동기화
        if (_isSkillPresentationPlaying && _skillControllerPlayable.IsValid())
        {
            _skillControllerPlayable.SetFloat(PlayerAnimationHash.DirX, _currentDirX);
            _skillControllerPlayable.SetFloat(PlayerAnimationHash.DirY, _currentDirY);
        }
    }

    // Base Layer (Layer 0) 제어: 구조체 직속 메서드에 위임하고 Action Layer 가중치를 0으로 설정
    internal void ApplyBaseLayerState(PlayerBaseAnimationFlags flags)
    {
        if (!flags.KeepSkillPresentation)
            StopSkillPresentation();

        flags.ApplyAnimation(SetBoolParameter);
        SetActionLayerWeight(0f);
    }

    // Action Layer (Layer 1) 제어: 구조체 직속 메서드에 위임하고 Action Layer 가중치를 1로 설정
    internal void ApplyActionLayerState(PlayerActionAnimationFlags flags)
    {
        flags.ApplyAnimation(SetBoolParameter);
        SetActionLayerWeight(1f);
    }

    private void SetBoolParameter(int parameterHash, bool value)
    {
        if (_anim == null)
            return;

        _anim.SetBool(parameterHash, value);

        // 스킬 Playable이 Animator Controller를 감싸는 중에도 동일 Bool 파라미터가 동기화되도록 전달한다.
        if (_isSkillPresentationPlaying && _skillControllerPlayable.IsValid())
            _skillControllerPlayable.SetBool(parameterHash, value);
    }

    internal void SetActionLayerWeight(float weight)
    {
        if (_actionLayerIndex < 0 || _actionLayerIndex >= _anim.layerCount)
            return;

        _anim.SetLayerWeight(_actionLayerIndex, weight, _actionLayerTransitionDuration);
    }

    public void StopSkillPresentation()
    {
        bool hadSkillPresentation =
            _isSkillPresentationPlaying
            || _skillGraph.IsValid()
            || _activeSkillTimeline != null
            || (_skillDirector != null && _skillDirector.playableAsset != null);
        if (!hadSkillPresentation)
            return;

        if (_skillGraph.IsValid())
        {
            _skillGraph.Destroy();
            _skillGraph = default;
        }
        _skillMixer = default;
        _currentSkillPlayable = default;
        _skillControllerPlayable = default;

        if (_skillDirector != null)
        {
            ClearTimelineBindings();
            _skillDirector.Stop();
            _skillDirector.playableAsset = null;

            if (_anim != null)
            {
                _anim.Rebind();
                _anim.Update(0f);
            }
        }

        _activeSkillTimeline = null;
        _isSkillPresentationPlaying = false;
        _skillActionDuration = 0f;
        _skillTransitionDuration = 0f;
    }

    public void PlaySkillClip(
        AnimationClip clip,
        double elapsedTime,
        float actionDuration,
        float transitionDuration = 0.15f
    )
    {
        if (clip == null || actionDuration <= 0f)
            return;

        double clampedElapsedTime = System.Math.Max(
            0d,
            System.Math.Min(elapsedTime, actionDuration)
        );
        if (clampedElapsedTime >= actionDuration)
            return;

        StopSkillPresentation();

        _skillGraph = PlayableGraph.Create($"PlayerSkillGraph_{GetEntityId()}");
        _skillGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            _skillGraph,
            "SkillAnimation",
            _anim
        );

        _skillActionDuration = actionDuration;
        _skillTransitionDuration = Mathf.Clamp(transitionDuration, 0f, actionDuration * 0.5f);
        _currentSkillPlayable = AnimationClipPlayable.Create(_skillGraph, clip);
        double playbackSpeed = clip.length / actionDuration;
        _currentSkillPlayable.SetTime(clampedElapsedTime * playbackSpeed);
        _currentSkillPlayable.SetSpeed(playbackSpeed);

        if (_anim.runtimeAnimatorController != null)
        {
            _skillMixer = AnimationLayerMixerPlayable.Create(_skillGraph, 2);
            _skillControllerPlayable = AnimatorControllerPlayable.Create(
                _skillGraph,
                _anim.runtimeAnimatorController
            );
            _skillGraph.Connect(_skillControllerPlayable, 0, _skillMixer, 0);
            _skillGraph.Connect(_currentSkillPlayable, 0, _skillMixer, 1);

            float initialWeight = 1f;
            if (_skillTransitionDuration > 0f)
            {
                if (clampedElapsedTime < _skillTransitionDuration)
                    initialWeight = Mathf.Clamp01(
                        (float)(clampedElapsedTime / _skillTransitionDuration)
                    );
                else if (actionDuration - clampedElapsedTime < _skillTransitionDuration)
                    initialWeight = Mathf.Clamp01(
                        (float)((actionDuration - clampedElapsedTime) / _skillTransitionDuration)
                    );
            }

            _skillMixer.SetInputWeight(0, 1f - initialWeight);
            _skillMixer.SetInputWeight(1, initialWeight);
            output.SetSourcePlayable(_skillMixer);
        }
        else
        {
            output.SetSourcePlayable(_currentSkillPlayable);
        }

        _skillStartTime = Time.timeAsDouble - clampedElapsedTime;
        _skillEndTime = _skillStartTime + actionDuration;
        _isSkillPresentationPlaying = true;
        _skillGraph.Play();
    }

    public void PlaySkillTimeline(TimelineAsset timeline, double elapsedTime, float actionDuration)
    {
        if (timeline == null || actionDuration <= 0f || timeline.duration <= 0d)
            return;

        double clampedElapsedTime = System.Math.Max(
            0d,
            System.Math.Min(elapsedTime, actionDuration)
        );
        if (clampedElapsedTime >= actionDuration)
            return;

        StopSkillPresentation();

        _activeSkillTimeline = timeline;
        _skillActionDuration = actionDuration;
        _skillDirector.playableAsset = timeline;
        _skillDirector.timeUpdateMode = DirectorUpdateMode.GameTime;
        BindTimelineOutputs(timeline);

        double speed = timeline.duration / actionDuration;
        _skillDirector.time = clampedElapsedTime * speed;
        _skillDirector.Play();

        PlayableGraph directorGraph = _skillDirector.playableGraph;
        if (directorGraph.IsValid())
        {
            for (int i = 0; i < directorGraph.GetRootPlayableCount(); i++)
            {
                directorGraph.GetRootPlayable(i).SetSpeed(speed);
            }
        }

        _skillStartTime = Time.timeAsDouble - clampedElapsedTime;
        _skillEndTime = _skillStartTime + actionDuration;
        _isSkillPresentationPlaying = true;
    }

    private void BindTimelineOutputs(TimelineAsset timeline)
    {
        foreach (PlayableBinding output in timeline.outputs)
        {
            if (output.sourceObject == null || output.outputTargetType == null)
                continue;

            if (typeof(Animator).IsAssignableFrom(output.outputTargetType))
            {
                _skillDirector.SetGenericBinding(output.sourceObject, _anim);
            }
            else if (typeof(GameObject).IsAssignableFrom(output.outputTargetType))
            {
                _skillDirector.SetGenericBinding(output.sourceObject, gameObject);
            }
        }
    }

    private void ClearTimelineBindings()
    {
        if (_skillDirector == null || _activeSkillTimeline == null)
            return;

        foreach (PlayableBinding output in _activeSkillTimeline.outputs)
        {
            if (output.sourceObject != null)
                _skillDirector.ClearGenericBinding(output.sourceObject);
        }
    }

    internal void ApplyJumpingState(bool isGrounded, bool isJumping)
    {
        _anim.SetBool(PlayerAnimationHash.IsGrounded, isGrounded);
        _anim.SetBool(PlayerAnimationHash.IsJumping, isJumping);

        if (_isSkillPresentationPlaying && _skillControllerPlayable.IsValid())
        {
            _skillControllerPlayable.SetBool(PlayerAnimationHash.IsGrounded, isGrounded);
            _skillControllerPlayable.SetBool(PlayerAnimationHash.IsJumping, isJumping);
        }
    }

    private void OnValidate()
    {
        if (!_anim)
        {
            EditorLog.LogError("Animator가 할당되지 않았습니다!", this);
        }

        ValidateBoolParameter(PlayerAnimationHash.IsTrapped, "IsTrapped");
        ValidateBoolParameter(PlayerAnimationHash.IsTrapJumping, "IsTrapJumping");
        ValidateBoolParameter(PlayerAnimationHash.IsThrowing, "IsThrowing");
    }

    private void ValidateBoolParameter(int parameterHash, string parameterName)
    {
        if (_anim == null || _anim.runtimeAnimatorController == null)
            return;

        AnimatorControllerParameter[] parameters = _anim.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (
                parameter.nameHash == parameterHash
                && parameter.type == AnimatorControllerParameterType.Bool
            )
                return;
        }

        EditorLog.LogError(
            $"Animator Controller에 Bool 파라미터 '{parameterName}'가 필요합니다.",
            this
        );
    }

    private void OnDestroy()
    {
        StopSkillPresentation();
    }
}
