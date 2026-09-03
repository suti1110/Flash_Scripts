using UnityEngine;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(DecalProjector))]
public sealed class SkillRangeDecal : MonoBehaviour
{
    private static readonly int PulseProgressId = Shader.PropertyToID("_PulseProgress");

    [SerializeField]
    private DecalProjector _decalProjector;

    [Header("인디케이터 애니메이션")]
    [SerializeField, InspectorName("점멸 사용")]
    private bool _useBlink = true;

    [SerializeField, InspectorName("점멸 횟수 (초당)"), Min(0f)]
    private float _blinkFrequency = 2f;

    [SerializeField, InspectorName("최소 불투명도"), Range(0f, 1f)]
    private float _minimumOpacity = 0.35f;

    [SerializeField, InspectorName("최대 불투명도"), Range(0f, 1f)]
    private float _maximumOpacity = 1f;

    [SerializeField, InspectorName("레이더 확산 사용")]
    private bool _useRadarPulse = true;

    [SerializeField, InspectorName("레이더 확산 횟수 (초당)"), Min(0f)]
    private float _radarCyclesPerSecond = 1.25f;

    private Material _runtimeMaterial;
    private float _animationStartTime;
    private bool _isInitialized;

    private void Awake()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponent<DecalProjector>();

        CreateRuntimeMaterial();
    }

    private void Update()
    {
        if (!_isInitialized || _decalProjector == null)
            return;

        float elapsedTime = Mathf.Max(0f, Time.time - _animationStartTime);
        UpdateBlink(elapsedTime);
        UpdateRadarPulse(elapsedTime);
    }

    public void Initialize(float radius, float elapsedTime)
    {
        SetRadius(radius);
        _animationStartTime = Time.time - Mathf.Max(0f, elapsedTime);
        _isInitialized = true;
        UpdateBlink(Mathf.Max(0f, elapsedTime));
        UpdateRadarPulse(Mathf.Max(0f, elapsedTime));
    }

    private void SetRadius(float radius)
    {
        if (_decalProjector == null)
            return;

        Vector3 size = _decalProjector.size;
        float diameter = Mathf.Max(0f, radius * 2f);
        size.x = diameter;
        size.y = diameter;
        _decalProjector.size = size;
    }

    private void UpdateBlink(float elapsedTime)
    {
        if (!_useBlink)
        {
            _decalProjector.fadeFactor = Mathf.Max(_minimumOpacity, _maximumOpacity);
            return;
        }

        float blink =
            _blinkFrequency <= 0f
                ? 1f
                : (Mathf.Sin(elapsedTime * _blinkFrequency * Mathf.PI * 2f) + 1f) * 0.5f;
        float minimum = Mathf.Min(_minimumOpacity, _maximumOpacity);
        float maximum = Mathf.Max(_minimumOpacity, _maximumOpacity);
        _decalProjector.fadeFactor = Mathf.Lerp(minimum, maximum, blink);
    }

    private void UpdateRadarPulse(float elapsedTime)
    {
        if (_runtimeMaterial == null)
            return;

        if (!_useRadarPulse || _radarCyclesPerSecond <= 0f)
        {
            _runtimeMaterial.SetFloat(PulseProgressId, 0f);
            return;
        }

        float progress = Mathf.Repeat(elapsedTime * _radarCyclesPerSecond, 1f);
        _runtimeMaterial.SetFloat(PulseProgressId, progress);
    }

    private void CreateRuntimeMaterial()
    {
        if (_decalProjector == null || _decalProjector.material == null)
            return;
        if (!_decalProjector.material.HasProperty(PulseProgressId))
            return;

        _runtimeMaterial = new Material(_decalProjector.material);
        _decalProjector.material = _runtimeMaterial;
    }

    private void OnValidate()
    {
        if (_decalProjector == null)
            _decalProjector = GetComponent<DecalProjector>();
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
            Destroy(_runtimeMaterial);
    }
}
// SkillRangeDecal은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
