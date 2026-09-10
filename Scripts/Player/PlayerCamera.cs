using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

public class PlayerCamera : MonoBehaviour, IReflectable
{
    private const float MirrorRotationDuration = 0.1f;

    [field: SerializeField]
    public Camera MainCamera { get; private set; }

    [field: SerializeField]
    public Transform CameraPivot { get; private set; }
    private CameraController _controller;

    private Vector2 _pivotRotation;

    private Tween _mirrorRotationTween;
    private Tween _fieldOfViewTween;
    private Tween _rippleTween;
    private Tween _shakeTween;
    private CameraSpeedLines _speedLines;
    private float _baseFieldOfView;
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
    private CameraFreezeFramePresenter _freezePresenter;

    private void Awake()
    {
        _controller = new CameraController();
        _rb = GetComponent<Rigidbody>();
        _playerStateMachine = GetComponent<Player>().StateMachine;
        _baseFieldOfView = MainCamera != null ? MainCamera.fieldOfView : 60f;
        _freezePresenter = GetComponent<CameraFreezeFramePresenter>();
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
        if (Time.timeScale <= 0f)
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

        Vector3 rayDirection = CameraPivot.TransformDirection(_cameraOffset.normalized);

        Vector3 cameraPosition;
        if (
            Physics.SphereCast(
                CameraPivot.position,
                _cameraBlockRadius,
                rayDirection,
                out RaycastHit hitInfo,
                _cameraOffset.magnitude,
                _cameraBlockLayers
            )
        )
            cameraPosition = CameraPivot.position + rayDirection * hitInfo.distance;
        else
            cameraPosition = CameraPivot.TransformPoint(_cameraOffset);

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

        MainCamera.transform.position =
            cameraPosition + CameraPivot.TransformVector(ripplePosition + shakePosition);
        MainCamera.transform.rotation =
            Quaternion.LookRotation(CameraPivot.forward)
            * Quaternion.Euler(rippleRotation + shakeRotation);
        MainCamera.fieldOfView = Mathf.Clamp(_baseFieldOfView + _fieldOfViewOffset, 1f, 179f);
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

    public void PlayBlinkFeedback(
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
        _speedLines ??= CameraSpeedLines.Create(MainCamera);
        _speedLines.Play(Mathf.Max(0.01f, zoomDuration) + returnDuration);
    }

    public void PlayHomingBeamFeedback(
        float fieldOfViewDecrease,
        float rippleStrength,
        float returnDuration
    )
    {
        PlayFieldOfViewPulse(-Mathf.Abs(fieldOfViewDecrease), returnDuration);
        PlayRippleFeedback(rippleStrength, returnDuration);
    }

    public void PlayDamageFeedback(float rippleStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;

        PlayRippleFeedback(rippleStrength, duration);
    }

    private void PlayRippleFeedback(float rippleStrength, float duration)
    {
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

    public void PlayDivinePunishmentFeedback(float shakeStrength, float duration)
    {
        PlayShakeFeedback(shakeStrength, duration);
    }

    public void PlayAttackHitFeedback(float shakeStrength, float duration)
    {
        if (MainCamera == null || !CanPlayLocalFeedback())
            return;

        PlayShakeFeedback(shakeStrength, duration);
    }

    private void PlayShakeFeedback(float shakeStrength, float duration)
    {
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
        if (MainCamera == null || !CanPlayLocalFeedback())
            return false;

        if (_freezePresenter == null)
        {
            _freezePresenter = GetComponent<CameraFreezeFramePresenter>();
            if (_freezePresenter == null)
                _freezePresenter = gameObject.AddComponent<CameraFreezeFramePresenter>();
        }

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

[DisallowMultipleComponent]
internal sealed class CameraSpeedLines : MonoBehaviour
{
    private const float EmissionDistance = 12f;
    private const float ViewEdgeRadiusRatio = 0.88f;

    private Camera _camera;
    private ParticleSystem _particles;
    private Material _runtimeMaterial;

    public static CameraSpeedLines Create(Camera camera)
    {
        GameObject effectObject = new("Camera Speed Lines");
        effectObject.transform.SetParent(camera.transform, false);
        effectObject.layer = camera.gameObject.layer;

        CameraSpeedLines effect = effectObject.AddComponent<CameraSpeedLines>();
        effect._camera = camera;
        effect.Initialize();
        return effect;
    }

    public void Play(float duration)
    {
        if (_particles == null)
            Initialize();

        ConfigureEdgeEmission();
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = _particles.main;
        main.duration = Mathf.Max(0.05f, duration);
        _particles.Play(true);
    }

    public void Stop()
    {
        if (_particles != null)
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void Initialize()
    {
        if (_particles != null)
            return;

        _particles = gameObject.AddComponent<ParticleSystem>();
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = _particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(28f, 40f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.55f, 0.85f, 1f, 0.18f),
            new Color(0.85f, 0.98f, 1f, 0.58f)
        );
        main.maxParticles = 70;

        ParticleSystem.EmissionModule emission = _particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 55f;

        ParticleSystem.ShapeModule shape = _particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 4f;
        shape.radiusThickness = 0.12f;
        shape.position = new Vector3(0f, 0f, EmissionDistance);
        shape.rotation = new Vector3(0f, 180f, 0f);

        ParticleSystemRenderer particleRenderer = GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.lengthScale = 5f;
        particleRenderer.velocityScale = 0.08f;
        particleRenderer.maxParticleSize = 0.12f;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        particleRenderer.sortingOrder = 100;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;

        _runtimeMaterial = new Material(shader)
        {
            name = "CameraSpeedLines_RuntimeMaterial",
            hideFlags = HideFlags.HideAndDontSave,
        };
        if (_runtimeMaterial.HasProperty("_BaseColor"))
            _runtimeMaterial.SetColor("_BaseColor", Color.white);
        if (_runtimeMaterial.HasProperty("_Color"))
            _runtimeMaterial.SetColor("_Color", Color.white);
        particleRenderer.sharedMaterial = _runtimeMaterial;
    }

    private void ConfigureEdgeEmission()
    {
        if (_particles == null || _camera == null)
            return;

        float verticalRadius =
            Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
            * EmissionDistance
            * ViewEdgeRadiusRatio;

        ParticleSystem.ShapeModule shape = _particles.shape;
        shape.radius = verticalRadius;
        shape.scale = new Vector3(Mathf.Max(1f, _camera.aspect), 1f, 1f);
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}
