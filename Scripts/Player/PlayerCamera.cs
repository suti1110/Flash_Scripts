using DG.Tweening;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerCamera : MonoBehaviour, IReflectable
{
    private const float MirrorRotationDuration = 0.1f;
    private const int DefaultCameraPriority = 20;
    private const int InactiveCameraPriority = 10;

    [field: SerializeField]
    public Camera MainCamera { get; private set; }

    [field: SerializeField]
    public Transform CameraPivot { get; private set; }

    [Header("Cinemachine")]
    [SerializeField]
    private CinemachineBrain _cinemachineBrain;

    [SerializeField]
    private CinemachineCamera _thirdPersonCamera;

    [SerializeField]
    private CinemachineCamera _firstPersonCamera;

    [SerializeField]
    private CinemachineCamera _improvedThirdPersonCamera;

    [Header("Improved Third Person")]
    [SerializeField]
    [Tooltip("CameraPivot 기준 위치입니다. X는 좌우 어깨 구도, 음수 Z는 뒤로 떨어진 거리입니다.")]
    private Vector3 _improvedThirdPersonOffset = new(1.2f, 0.35f, -2.6f);

    [SerializeField, Min(0f)]
    private float _improvedThirdPersonBlendDuration = 0.18f;

    [SerializeField]
    private PlayerLocalVisualFader _localVisualFader;

    [SerializeField, Min(0.001f)]
    private float _firstPersonNearClipPlane = 0.03f;

    private CameraController _controller;

    private Vector2 _pivotRotation;

    private Tween _mirrorRotationTween;
    private Tween _fieldOfViewTween;
    private Tween _rippleTween;
    private Tween _shakeTween;
    [SerializeField] private CameraSpeedLines _speedLinesPrefab;
    private CameraSpeedLines _speedLines;
    private float _baseFieldOfView;
    private float _baseNearClipPlane;
    private float _fieldOfViewOffset;
    private float _rippleWeight;
    private float _rippleStrength;
    private float _shakeWeight;
    private float _shakeStrength;

    [SerializeField]
    private float _mouseSensitivity = 1f;

    [SerializeField]
    private Vector3 _cameraOffset;

    [SerializeField]
    private float _cameraBlockRadius;

    [SerializeField]
    private LayerMask _cameraBlockLayers;

    private PlayerStateMachine _playerStateMachine;

    private Rigidbody _rb;
    private Vector3 _lastVelocity;
    public Vector3 LastVelocity => _lastVelocity;
    [SerializeField] private CameraFreezeFramePresenter _freezePresenter;
    private SkillCameraViewMode _skillCameraView = SkillCameraViewMode.ThirdPerson;
    private bool _isThrowAiming;

    private void Awake()
    {
        _controller = new CameraController();
        _rb = GetComponent<Rigidbody>();
        _playerStateMachine = GetComponent<Player>().StateMachine;
        _baseFieldOfView = MainCamera != null ? MainCamera.fieldOfView : 60f;
        _baseNearClipPlane = MainCamera != null ? MainCamera.nearClipPlane : 0.3f;
        _freezePresenter ??= GetComponent<CameraFreezeFramePresenter>();
        _localVisualFader ??= GetComponent<PlayerLocalVisualFader>();

        if (_cinemachineBrain == null || _thirdPersonCamera == null || _firstPersonCamera == null
            || _improvedThirdPersonCamera == null)
            EditorLog.LogError("PlayerCamera의 Cinemachine 참조가 누락되었습니다. Player 프리팹의 세 카메라와 Brain을 연결해 주세요.", this);

        EnsureCinemachineSetup();
        SetCameraPriorities(SkillCameraViewMode.ThirdPerson);
    }

    private void OnEnable()
    {
        _controller.Enable();
    }

    private void OnDisable()
    {
        _controller.Disable();
        KillPresentationTweens();
        ResetCameraPresentation();
    }

    private void OnDestroy()
    {
        KillPresentationTweens();
        _controller.Dispose();
    }

    private void FixedUpdate()
    {
        _lastVelocity = _rb.linearVelocity;

        if (!_playerStateMachine.CurrentState.UsesDetachedCameraRotation)
        {
            _rb.MoveRotation(Quaternion.Euler(0, _pivotRotation.x, 0));
        }
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f || CameraPivot == null)
            return;

        if (_mirrorRotationTween == null || !_mirrorRotationTween.IsActive())
        {
            SetCameraRotation(
                _pivotRotation
                    + 0.1f
                        * _mouseSensitivity
                        * _controller.MousePosition.Mouse.ReadValue<Vector2>()
            );
        }

        CameraPivot.rotation = Quaternion.Euler(-_pivotRotation.y, _pivotRotation.x, 0);

        Vector3 cameraPosition = GetThirdPersonPosition(_cameraOffset);
        Vector3 improvedCameraPosition = GetThirdPersonPosition(_improvedThirdPersonOffset);

        float time = Time.time;
        Vector3 ripplePosition = new(
            Mathf.Sin(time * 21f) * 0.025f,
            Mathf.Sin(time * 16f + 0.8f) * 0.018f,
            0f
        );
        ripplePosition *= _rippleStrength * _rippleWeight;

        Vector3 shakePosition = new(
            Mathf.PerlinNoise(time * 31f, 0.17f) * 2f - 1f,
            Mathf.PerlinNoise(0.41f, time * 37f) * 2f - 1f,
            0f
        );
        shakePosition *= _shakeStrength * _shakeWeight;

        Vector3 rippleRotation = new(
            Mathf.Sin(time * 18f) * 0.55f,
            Mathf.Sin(time * 14f + 1.1f) * 0.4f,
            Mathf.Sin(time * 23f + 0.4f)
        );
        rippleRotation *= _rippleStrength * _rippleWeight;

        Vector3 shakeRotation = new(
            shakePosition.y * 7f,
            shakePosition.x * 7f,
            (Mathf.PerlinNoise(time * 29f, 0.73f) * 2f - 1f)
                * _shakeStrength
                * _shakeWeight
                * 4f
        );

        Quaternion presentationRotation =
            Quaternion.LookRotation(CameraPivot.forward)
            * Quaternion.Euler(rippleRotation + shakeRotation);
        Vector3 presentationOffset = CameraPivot.TransformVector(ripplePosition + shakePosition);
        float fieldOfView = Mathf.Clamp(_baseFieldOfView + _fieldOfViewOffset, 1f, 179f);

        if (_thirdPersonCamera != null)
        {
            _thirdPersonCamera.transform.SetPositionAndRotation(
                cameraPosition + presentationOffset,
                presentationRotation
            );
            SetCameraLens(_thirdPersonCamera, fieldOfView, _baseNearClipPlane);
        }

        if (_firstPersonCamera != null)
        {
            _firstPersonCamera.transform.SetPositionAndRotation(
                CameraPivot.position + presentationOffset,
                presentationRotation
            );
            SetCameraLens(_firstPersonCamera, fieldOfView, _firstPersonNearClipPlane);
        }

        if (_improvedThirdPersonCamera != null)
        {
            _improvedThirdPersonCamera.transform.SetPositionAndRotation(
                improvedCameraPosition + presentationOffset,
                presentationRotation
            );
            SetCameraLens(_improvedThirdPersonCamera, fieldOfView, _baseNearClipPlane);
        }

        _cinemachineBrain?.ManualUpdate();
    }

    public void SetSkillCameraView(SkillCameraViewMode viewMode, float blendDuration)
    {
        if (!CanPlayLocalFeedback())
            return;

        _skillCameraView = viewMode;
        ApplyCameraView(blendDuration);
    }

    public void SetThrowAiming(bool isAiming)
    {
        if (!CanPlayLocalFeedback() || _isThrowAiming == isAiming)
            return;

        _isThrowAiming = isAiming;
        ApplyCameraView(_improvedThirdPersonBlendDuration);
    }

    private void ApplyCameraView(float blendDuration)
    {
        EnsureCinemachineSetup();
        if (_cinemachineBrain == null || _thirdPersonCamera == null || _firstPersonCamera == null
            || _improvedThirdPersonCamera == null)
            return;

        // 스킬의 명시적인 시점이 우선한다. 조준 종료가 새 스킬의 시점 전환을 덮어쓰지 않는다.
        SkillCameraViewMode viewMode = _skillCameraView != SkillCameraViewMode.ThirdPerson
            ? _skillCameraView
            : _isThrowAiming ? SkillCameraViewMode.ImprovedThirdPerson : SkillCameraViewMode.ThirdPerson;
        _cinemachineBrain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut,
            Mathf.Max(0f, blendDuration)
        );
        SetCameraPriorities(viewMode);

        if (viewMode == SkillCameraViewMode.FirstPerson)
            _localVisualFader?.FadeToFirstPerson(blendDuration);
        else
            _localVisualFader?.FadeToThirdPerson(blendDuration);
    }

    private Vector3 GetThirdPersonPosition(Vector3 offset)
    {
        Vector3 rayDirection = CameraPivot.TransformDirection(offset.normalized);
        if (Physics.SphereCast(
            CameraPivot.position,
            _cameraBlockRadius,
            rayDirection,
            out RaycastHit hit,
            offset.magnitude,
            _cameraBlockLayers,
            QueryTriggerInteraction.UseGlobal
        ))
            return CameraPivot.position + rayDirection * hit.distance;

        return CameraPivot.TransformPoint(offset);
    }

    public void SetCameraRotation(Vector2 value)
    {
        _pivotRotation = value;
        _pivotRotation.y = Mathf.Clamp(_pivotRotation.y, -80f, 80f);
    }

    public void TweenCameraRotation(Vector2 value)
    {
        _mirrorRotationTween?.Kill();

        Vector2 target = new(
            _pivotRotation.x + Mathf.DeltaAngle(_pivotRotation.x, value.x),
            Mathf.Clamp(value.y, -80f, 80f)
        );
        _mirrorRotationTween = DOTween
            .To(() => _pivotRotation, SetCameraRotation, target, MirrorRotationDuration)
            .SetEase(Ease.OutSine)
            .SetTarget(this)
            .OnComplete(() => _mirrorRotationTween = null);
    }

    // 스킬은 필요한 카메라 연출을 조합한다. 카메라는 스킬 종류를 알지 않는다.
    public void PlayZoomWithSpeedLines(
        float fieldOfViewIncrease,
        float zoomDuration,
        float returnDuration
    )
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;

        PlayFieldOfViewPulse(
            Mathf.Abs(fieldOfViewIncrease),
            returnDuration,
            Mathf.Max(0.01f, zoomDuration)
        );
        if (_speedLines == null && _speedLinesPrefab != null)
        {
            _speedLines = Instantiate(_speedLinesPrefab, MainCamera.transform);
            _speedLines.Initialize(MainCamera);
        }
        if (_speedLines == null) return;
        _speedLines.Play(Mathf.Max(0.01f, zoomDuration) + returnDuration);
    }

    public void PlayFieldOfViewFeedback(float fieldOfViewChange, float returnDuration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;
        PlayFieldOfViewPulse(fieldOfViewChange, returnDuration);
    }

    public void PlayDamageFeedback(float rippleStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;

        PlayRippleFeedback(rippleStrength, duration);
    }

    public void PlayRippleFeedback(float rippleStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;
        _rippleTween?.Kill();
        _rippleStrength = Mathf.Max(0f, rippleStrength);
        _rippleWeight = 1f;
        _rippleTween = DOTween
            .To(
                () => _rippleWeight,
                value => _rippleWeight = value,
                0f,
                Mathf.Max(0.01f, duration)
            )
            .SetEase(Ease.OutSine)
            .SetTarget(this)
            .OnComplete(() => _rippleTween = null);
    }

    public void PlayAttackHitFeedback(float shakeStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;

        PlayShakeFeedback(shakeStrength, duration);
    }

    public void PlayShakeFeedback(float shakeStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;
        _shakeTween?.Kill();
        _shakeStrength = Mathf.Max(0f, shakeStrength);
        _shakeWeight = 1f;
        _shakeTween = DOTween
            .To(() => _shakeWeight, value => _shakeWeight = value, 0f, Mathf.Max(0.01f, duration))
            .SetEase(Ease.OutQuad)
            .SetTarget(this)
            .OnComplete(() => _shakeTween = null);
    }

    private bool CanPlayLocalFeedback()
    {
        NetworkObject networkObject = GetComponentInParent<NetworkObject>();
        return networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;
    }

    private void EnsureCinemachineSetup()
    {
        if (MainCamera == null || _cinemachineBrain == null || _thirdPersonCamera == null ||
            _firstPersonCamera == null || _improvedThirdPersonCamera == null) return;
        _cinemachineBrain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        _cinemachineBrain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
        LensSettings defaultLens = LensSettings.FromCamera(MainCamera);
        defaultLens.NearClipPlane = _baseNearClipPlane;
        _thirdPersonCamera.Lens = defaultLens;
        _improvedThirdPersonCamera.Lens = defaultLens;
        defaultLens.NearClipPlane = _firstPersonNearClipPlane;
        _firstPersonCamera.Lens = defaultLens;
    }

    private void SetCameraPriorities(SkillCameraViewMode viewMode)
    {
        if (_thirdPersonCamera == null || _firstPersonCamera == null || _improvedThirdPersonCamera == null)
            return;

        _thirdPersonCamera.Priority = viewMode == SkillCameraViewMode.ThirdPerson
            ? DefaultCameraPriority : InactiveCameraPriority;
        _firstPersonCamera.Priority = viewMode == SkillCameraViewMode.FirstPerson
            ? DefaultCameraPriority : InactiveCameraPriority;
        _improvedThirdPersonCamera.Priority = viewMode == SkillCameraViewMode.ImprovedThirdPerson
            ? DefaultCameraPriority : InactiveCameraPriority;
    }

    private static void SetCameraLens(
        CinemachineCamera camera,
        float fieldOfView,
        float nearClipPlane
    )
    {
        LensSettings lens = camera.Lens;
        lens.FieldOfView = fieldOfView;
        lens.NearClipPlane = nearClipPlane;
        camera.Lens = lens;
    }

    private void PlayFieldOfViewPulse(
        float offset,
        float returnDuration,
        float transitionDuration = 0f
    )
    {
        _fieldOfViewTween?.Kill();

        if (transitionDuration <= 0f)
        {
            _fieldOfViewOffset = offset;
            _fieldOfViewTween = DOTween
                .To(
                    () => _fieldOfViewOffset,
                    value => _fieldOfViewOffset = value,
                    0f,
                    Mathf.Max(0.01f, returnDuration)
                )
                .SetEase(Ease.OutCubic)
                .SetTarget(this)
                .OnComplete(() => _fieldOfViewTween = null);
            return;
        }

        Sequence sequence = DOTween.Sequence();
        sequence.SetTarget(this);
        sequence.Append(
            DOTween
                .To(
                    () => _fieldOfViewOffset,
                    value => _fieldOfViewOffset = value,
                    offset,
                    transitionDuration
                )
                .SetEase(Ease.OutCubic)
        );
        sequence.Append(
            DOTween
                .To(
                    () => _fieldOfViewOffset,
                    value => _fieldOfViewOffset = value,
                    0f,
                    Mathf.Max(0.01f, returnDuration)
                )
                .SetEase(Ease.OutCubic)
        );
        _fieldOfViewTween = sequence.OnComplete(() => _fieldOfViewTween = null);
    }

    public bool BeginFreezeFrame()
    {
        if (MainCamera == null || !CanPlayLocalFeedback() || _freezePresenter == null) return false;
        return _freezePresenter.CaptureAndFreeze(MainCamera);
    }

    public void EndFreezeFrame()
    {
        _freezePresenter?.EndFreeze();
    }

    private void KillPresentationTweens()
    {
        _mirrorRotationTween?.Kill();
        _fieldOfViewTween?.Kill();
        _rippleTween?.Kill();
        _shakeTween?.Kill();
        _mirrorRotationTween = null;
        _fieldOfViewTween = null;
        _rippleTween = null;
        _shakeTween = null;
    }

    private void ResetCameraPresentation()
    {
        _fieldOfViewOffset = 0f;
        _rippleWeight = 0f;
        _shakeWeight = 0f;
        _speedLines?.Stop();
        EndFreezeFrame();

        _isThrowAiming = false;
        _skillCameraView = SkillCameraViewMode.ThirdPerson;
        SetCameraPriorities(SkillCameraViewMode.ThirdPerson);
        _localVisualFader?.RestoreImmediately();

        if (MainCamera != null)
            MainCamera.fieldOfView = _baseFieldOfView;
    }

    private void OnValidate()
    {
        if (!MainCamera)
        {
            EditorLog.LogError("MainCamera가 할당되지 않았습니다!!!", this);
        }

        if (!CameraPivot)
        {
            EditorLog.LogError("CameraPivot이 할당되지 않았습니다!!!", this);
        }

        if (!_localVisualFader)
        {
            EditorLog.LogError("PlayerLocalVisualFader가 할당되지 않았습니다!!!", this);
        }

        if (_cameraOffset == Vector3.zero)
        {
            EditorLog.LogError("CameraOffset이 (0,0,0)입니다!!!", this);
        }

        if (_cameraBlockRadius <= 0)
        {
            EditorLog.LogError("CameraBlockRadius가 0 이하입니다!!!", this);
        }
    }

    // Raycast를 화면에 그리기
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 startPos = CameraPivot.position;
        Vector3 endPos = startPos + _cameraOffset;

        // 시작점과 끝점에 구체를 그리고 선으로 연결
        Gizmos.DrawWireSphere(startPos, _cameraBlockRadius);
        Gizmos.DrawWireSphere(endPos, _cameraBlockRadius);
        Gizmos.DrawLine(
            startPos + Vector3.left * _cameraBlockRadius,
            endPos + Vector3.left * _cameraBlockRadius
        );
        Gizmos.DrawLine(
            startPos + Vector3.right * _cameraBlockRadius,
            endPos + Vector3.right * _cameraBlockRadius
        );
    }
}
