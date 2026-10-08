using System;
using UnityEngine;
using Random = System.Random;

[DisallowMultipleComponent]
public sealed class DivinePunishmentStrikeEffect : MonoBehaviour
{
    private const int BranchCount = 2;

    [Header("크기")]
    [SerializeField, InspectorName("기준 공격 반경"), Min(0.1f)]
    [Tooltip("이 프리팹이 원래 크기로 재생되는 천벌의 공격 반경입니다.")]
    private float _referenceStrikeRadius = 5f;

    [Header("낙뢰")]
    [SerializeField, InspectorName("번개 높이"), Min(1f)]
    private float _boltHeight = 18f;

    [SerializeField, InspectorName("번개 구간 수"), Range(6, 32)]
    private int _boltSegments = 16;

    [SerializeField, InspectorName("번개 흔들림"), Min(0f)]
    private float _boltJitter = 0.75f;

    [SerializeField, InspectorName("번개 표시 시간"), Min(0.01f)]
    private float _boltVisibleDuration = 0.24f;

    [SerializeField, InspectorName("경로 재생성 간격"), Min(0.01f)]
    private float _boltRefreshInterval = 0.045f;

    [Header("충돌")]
    [SerializeField, InspectorName("지면 링 반경"), Min(0.1f)]
    private float _groundRingRadius = 4.5f;

    [SerializeField, InspectorName("지면 링 지속 시간"), Min(0.01f)]
    private float _groundRingDuration = 0.55f;

    private float _lightPeakIntensity;

    [Header("수명과 오디오")]
    [SerializeField, InspectorName("전체 수명"), Min(0.1f)]
    private float _effectDuration = 1.25f;

    [SerializeField] private LineRenderer[] _branchCores = new LineRenderer[BranchCount];
    [SerializeField] private LineRenderer[] _branchGlows = new LineRenderer[BranchCount];

    private Random _random;
    private Vector3[] _boltPositions;
    [SerializeField] private LineRenderer _boltCore;
    [SerializeField] private LineRenderer _boltGlow;
    [SerializeField] private LineRenderer _groundRing;
    [SerializeField] private Light _flashLight;

    [SerializeField] private ParticleSystem[] _impactParticles;
    [SerializeField] private AudioSource _audioSource;
    private readonly System.Collections.Generic.Dictionary<LineRenderer, Gradient> _lineColors = new();
    private float _elapsedTime;
    private float _nextBoltRefreshTime;
    private float _visualScale = 1f;

    public void Initialize(float strikeRadius)
    {
        _visualScale = Mathf.Max(0.1f, strikeRadius / Mathf.Max(0.1f, _referenceStrikeRadius));
    }

    private void Start()
    {
        _random = new Random(unchecked(GetEntityId().GetHashCode() * 397 ^ Environment.TickCount));
        _boltPositions = new Vector3[Mathf.Max(2, _boltSegments)];

        foreach (var line in GetComponentsInChildren<LineRenderer>(true))
        {
            _lineColors[line] = line.colorGradient;
            line.widthMultiplier *= _visualScale;
        }
        foreach (var particles in _impactParticles)
        {
            if (particles == null) continue;
            var main = particles.main;
            main.startSpeedMultiplier *= _visualScale;
            main.startSizeMultiplier *= _visualScale;
            var shape = particles.shape;
            shape.radius *= _visualScale;
            particles.Play();
        }
        if (_flashLight != null)
        {
            _lightPeakIntensity = _flashLight.intensity;
            _flashLight.range *= _visualScale;
            _flashLight.transform.localPosition *= _visualScale;
        }
        PlayStrikeAudio();

        RefreshBoltPath();
        UpdateGroundRing(0f);
        Destroy(gameObject, _effectDuration);
    }

    private void Update()
    {
        _elapsedTime += Time.deltaTime;

        if (_elapsedTime < _boltVisibleDuration)
        {
            if (_elapsedTime >= _nextBoltRefreshTime)
            {
                RefreshBoltPath();
                _nextBoltRefreshTime += _boltRefreshInterval;
            }

            float normalizedTime = Mathf.Clamp01(_elapsedTime / _boltVisibleDuration);
            float flicker = 0.55f + Mathf.Abs(Mathf.Sin(_elapsedTime * 95f)) * 0.45f;
            SetBoltVisibility((1f - normalizedTime) * flicker);
        }
        else
        {
            SetBoltRenderersEnabled(false);
        }

        UpdateGroundRing(Mathf.Clamp01(_elapsedTime / _groundRingDuration));
        UpdateFlashLight();
    }

    private void RefreshBoltPath()
    {
        int segmentCount = _boltPositions.Length;
        for (int i = 0; i < segmentCount; i++)
        {
            float normalizedHeight = i / (float)(segmentCount - 1);
            float endpointWeight = Mathf.Sin(normalizedHeight * Mathf.PI);
            Vector2 jitter = RandomInsideUnitCircle() * Scale(_boltJitter * endpointWeight);
            _boltPositions[i] = new Vector3(
                jitter.x,
                normalizedHeight * Scale(_boltHeight),
                jitter.y
            );
        }

        SetLinePositions(_boltCore, _boltPositions);
        SetLinePositions(_boltGlow, _boltPositions);

        for (int i = 0; i < BranchCount; i++)
            RefreshBranch(i);
    }

    private void RefreshBranch(int branchIndex)
    {
        int startIndex = Mathf.RoundToInt(
            Mathf.Lerp(3f, _boltPositions.Length - 4f, (branchIndex + 1f) / (BranchCount + 1f))
        );
        startIndex = Mathf.Clamp(startIndex, 1, _boltPositions.Length - 2);

        const int branchSegments = 6;
        Vector3[] branchPositions = new Vector3[branchSegments];
        Vector2 horizontalDirection = RandomInsideUnitCircle().normalized;
        float branchLength = Scale(RandomRange(1.8f, 3.4f));
        Vector3 branchStart = _boltPositions[startIndex];

        for (int i = 0; i < branchSegments; i++)
        {
            float normalizedLength = i / (float)(branchSegments - 1);
            Vector2 jitter =
                RandomInsideUnitCircle() * Scale(_boltJitter * 0.25f * normalizedLength);
            branchPositions[i] =
                branchStart
                + new Vector3(horizontalDirection.x, -0.35f, horizontalDirection.y)
                    * (branchLength * normalizedLength)
                + new Vector3(jitter.x, 0f, jitter.y);
        }

        SetLinePositions(_branchCores[branchIndex], branchPositions);
        SetLinePositions(_branchGlows[branchIndex], branchPositions);
    }

    private static void SetLinePositions(LineRenderer line, Vector3[] positions)
    {
        line.positionCount = positions.Length;
        line.SetPositions(positions);
    }

    private void SetBoltVisibility(float alpha)
    {
        SetBoltRenderersEnabled(true);
        SetLineColor(_boltCore, alpha);
        SetLineColor(_boltGlow, alpha);

        for (int i = 0; i < BranchCount; i++)
        {
            SetLineColor(_branchCores[i], alpha);
            SetLineColor(_branchGlows[i], alpha);
        }
    }

    private void SetBoltRenderersEnabled(bool isEnabled)
    {
        _boltCore.enabled = isEnabled;
        _boltGlow.enabled = isEnabled;

        for (int i = 0; i < BranchCount; i++)
        {
            _branchCores[i].enabled = isEnabled;
            _branchGlows[i].enabled = isEnabled;
        }
    }

    private void SetLineColor(LineRenderer line, float alpha)
    {
        if (line == null || !_lineColors.TryGetValue(line, out var authored)) return;
        var keys = authored.alphaKeys;
        for (int i = 0; i < keys.Length; i++) keys[i].alpha *= Mathf.Clamp01(alpha);
        var gradient = new Gradient { mode = authored.mode };
        gradient.SetKeys(authored.colorKeys, keys);
        line.colorGradient = gradient;
    }

    private void UpdateGroundRing(float normalizedTime)
    {
        if (_groundRing == null)
            return;

        if (normalizedTime >= 1f)
        {
            _groundRing.enabled = false;
            return;
        }

        _groundRing.enabled = true;
        float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 3f);
        float radius = Mathf.Lerp(Scale(0.25f), Scale(_groundRingRadius), easedTime);

        for (int i = 0; i < _groundRing.positionCount; i++)
        {
            float angle = i / (float)_groundRing.positionCount * Mathf.PI * 2f;
            _groundRing.SetPosition(
                i,
                new Vector3(Mathf.Cos(angle) * radius, 0.035f, Mathf.Sin(angle) * radius)
            );
        }

        SetLineColor(_groundRing, (1f - normalizedTime) * 0.8f);
    }

    private void UpdateFlashLight()
    {
        if (_flashLight == null)
            return;

        float normalizedTime = Mathf.Clamp01(_elapsedTime / Mathf.Max(0.01f, _boltVisibleDuration));
        _flashLight.intensity = _lightPeakIntensity * Mathf.Pow(1f - normalizedTime, 2f);
        _flashLight.enabled = normalizedTime < 1f;
    }

    private void PlayStrikeAudio()
    {
        if (_audioSource == null) return;
        if (_audioSource.clip != null) _audioSource.Play();
    }

    private Vector2 RandomInsideUnitCircle()
    {
        float angle = RandomRange(0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt(RandomRange(0f, 1f));
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private float RandomRange(float min, float max)
    {
        return Mathf.Lerp(min, max, (float)_random.NextDouble());
    }

    private float Scale(float value) => value * _visualScale;

    private void OnValidate()
    {
        _effectDuration = Mathf.Max(_effectDuration, _boltVisibleDuration, _groundRingDuration);
        _boltRefreshInterval = Mathf.Max(0.01f, _boltRefreshInterval);
        _boltSegments = Mathf.Max(6, _boltSegments);
    }
}
// DivinePunishmentStrikeEffect은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
