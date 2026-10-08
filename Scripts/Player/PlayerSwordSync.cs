using DG.Tweening;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public interface ISwordSizeProvider
{
    float SwordSize { get; }
}

public interface ISwordSizeController : ISwordSizeProvider
{
    void SetSwordSizeMultiplier(object source, float multiplier);
    void RemoveSwordSizeMultiplier(object source);
}

public class PlayerSwordSync : NetworkBehaviour, ISwordSizeController
{
    private const float ResizeDuration = 0.3f;
    private const float EmissionFadeDuration = 0.2f;
    private const float EmissionHoldDuration = 0.8f;
    private const float EmissionIntensityStops = 5f;
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Renderer And MeshFilter")]
    [SerializeField]
    private Renderer _swordRenderer;

    [SerializeField]
    private MeshFilter _swordMeshFilter;

    [SerializeField]
    private SO_Sword _sword;

    /// <summary>
    /// 플레이어가 장착 중인 검의 MeshRenderer입니다.
    /// </summary>
    public MeshRenderer SwordMeshRenderer => _swordRenderer as MeshRenderer;

    /// <summary>
    /// 플레이어가 장착 중인 검의 MeshFilter입니다.
    /// </summary>
    public MeshFilter SwordMeshFilter => _swordMeshFilter;
    public SO_SwordStats SwordStats => _sword != null ? _sword.SwordStats : null;
    public float SwordSize => SwordStats != null
        ? SwordStats.GetSwordSize(_currentMultiplier) : 1f;

    private readonly NetworkVariable<int> _swordIndex = new();
    private readonly Dictionary<object, float> _sizeModifiers = new();
    private Vector3 _baseSwordScale;
    private Vector3 _baseSwordPosition;
    private Vector3 _gripLocalPoint;
    private float _currentMultiplier = 1f;
    private float _targetMultiplier = 1f;
    private Tween _resizeTween;
    private Sequence _emissionTween;
    private Material _runtimeSwordMaterial;
    private Material _sourceSwordMaterial;
    private bool _baseEmissionEnabled;
    private Color _originalEmissionColor;
    private Color _flashEmissionColor;
    private Texture _originalEmissionMap;

    // 효과마다 자신의 배율만 등록/해제한다. 하나의 종료가 다른 효과를 지우지 않는다.
    public void SetSwordSizeMultiplier(object source, float multiplier)
    {
        if (source == null || !float.IsFinite(multiplier) || multiplier <= 0f)
            return;
        if (_sizeModifiers.TryGetValue(source, out float current)
            && Mathf.Approximately(current, multiplier))
            return;
        _sizeModifiers[source] = multiplier;
        RefreshSizeModifiers();
    }

    public void RemoveSwordSizeMultiplier(object source)
    {
        if (source != null && _sizeModifiers.Remove(source))
            RefreshSizeModifiers();
    }

    private void RefreshSizeModifiers()
    {
        float multiplier = 1f;
        foreach (float value in _sizeModifiers.Values)
            multiplier *= value;
        ApplySwordSizeMultiplier(multiplier);
    }

    private void ApplySwordSizeMultiplier(float multiplier)
    {
        SO_SwordStats stats = SwordStats;
        if (_swordMeshFilter == null || stats == null)
            return;

        multiplier = Mathf.Max(0.01f, multiplier);
        if (Mathf.Approximately(_targetMultiplier, multiplier))
        {
            // 런타임에 기본 SwordSize를 바꿔도 현재 배율을 실제 Transform에 반영한다.
            if (_resizeTween == null || !_resizeTween.IsActive())
                ApplyCurrentSwordSize(_currentMultiplier);
            return;
        }

        _targetMultiplier = multiplier;
        _resizeTween?.Kill();
        if (!Application.isPlaying || !gameObject.activeInHierarchy || !enabled)
        {
            ApplyCurrentSwordSize(multiplier);
            return;
        }

        // 중간에 침묵 또는 재발동으로 목표가 바뀌면 현재 크기에서 새 목표로 이어진다.
        _resizeTween = DOTween
            .To(() => _currentMultiplier, ApplyCurrentSwordSize, multiplier, ResizeDuration)
            .SetEase(Ease.InOutSine);
        PlayEmissionPulse();
    }

    public void ResetSwordSize()
    {
        _sizeModifiers.Clear();
        _resizeTween?.Kill();
        _resizeTween = null;
        _emissionTween?.Kill();
        _emissionTween = null;
        SetEmissionPulse(0f);
        _targetMultiplier = 1f;
        ApplyCurrentSwordSize(1f);
    }

    public override void OnNetworkSpawn()
    {
        UpdateSword(_swordIndex.Value);

        _swordIndex.OnValueChanged += OnSkinChanged;

        if (IsOwner)
        {
            PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
            loadout.InitializeSword(_sword.DefaultSwordIndex, _sword.AllSwords.Length);
            SetSkinIndexServerRpc(loadout.SwordIndex);
        }
    }

    public override void OnNetworkDespawn()
    {
        _swordIndex.OnValueChanged -= OnSkinChanged;
        ResetSwordSize();
    }

    [ServerRpc]
    private void SetSkinIndexServerRpc(int index)
    {
        if (index < 0 || index >= _sword.AllSwords.Length)
            return;

        _swordIndex.Value = index;
    }

    private void OnSkinChanged(int preValue, int newValue)
    {
        UpdateSword(newValue);
    }

    private void Awake()
    {
        if (_swordMeshFilter != null)
        {
            Transform sword = _swordMeshFilter.transform;
            _baseSwordScale = sword.localScale;
            _baseSwordPosition = sword.localPosition;
            // 기본 자세에서 Hand_R 원점에 놓인 메시 지점을 잡는 기준점으로 고정한다.
            _gripLocalPoint =
                sword.parent != null
                    ? sword.InverseTransformPoint(sword.parent.position)
                    : Vector3.zero;
        }
        InitializeDefaultSwordInEditor();
        if (_swordRenderer != null)
            SetSwordMaterial(_swordRenderer.sharedMaterial);
        ApplySwordSizeMultiplier(1f);
    }

    private void OnDisable() => ResetSwordSize();

    public override void OnDestroy()
    {
        _resizeTween?.Kill();
        _emissionTween?.Kill();
        if (_swordMeshFilter != null)
        {
            _swordMeshFilter.transform.localScale = _baseSwordScale;
            _swordMeshFilter.transform.localPosition = _baseSwordPosition;
        }
        if (_swordRenderer != null && _runtimeSwordMaterial != null)
            _swordRenderer.sharedMaterial = _sourceSwordMaterial;
        if (_runtimeSwordMaterial != null)
            Destroy(_runtimeSwordMaterial);
        base.OnDestroy();
    }

    private void UpdateSword(int index)
    {
        if (index < 0 || index >= _sword.AllSwords.Length)
            return;

        SetSwordMaterial(_sword.AllSwords[index].Material);
        _swordMeshFilter.mesh = _sword.AllSwords[index].Mesh;
    }

    private void ApplyCurrentSwordSize(float multiplier)
    {
        _currentMultiplier = multiplier;
        if (_swordMeshFilter == null)
            return;
        if (SwordStats)
            SwordStats.ApplySwordSize(
                _swordMeshFilter.transform,
                _baseSwordScale,
                _gripLocalPoint,
                multiplier
            );
    }

    private void SetSwordMaterial(Material source)
    {
        _emissionTween?.Kill();
        _emissionTween = null;
        if (_runtimeSwordMaterial != null)
        {
            _swordRenderer.sharedMaterial = source;
            Destroy(_runtimeSwordMaterial);
            _runtimeSwordMaterial = null;
        }

        _sourceSwordMaterial = source;
        if (!Application.isPlaying || source == null)
        {
            _swordRenderer.sharedMaterial = source;
            return;
        }

        // 공유 에셋의 색을 건드리지 않도록 플레이어마다 검 머티리얼을 복제한다.
        _runtimeSwordMaterial = new Material(source);
        _baseEmissionEnabled = source.IsKeywordEnabled("_EMISSION");
        _swordRenderer.sharedMaterial = _runtimeSwordMaterial;
        if (!_runtimeSwordMaterial.HasProperty(EmissionColorId))
            return;

        _originalEmissionColor = source.GetColor(EmissionColorId);
        _originalEmissionMap = source.HasProperty(EmissionMapId)
            ? source.GetTexture(EmissionMapId) : null;
        Color flashBase = _originalEmissionColor;
        if (flashBase.maxColorComponent <= 0.001f)
            flashBase = source.HasProperty(BaseColorId)
                ? source.GetColor(BaseColorId)
                : Color.white;
        if (flashBase.maxColorComponent <= 0.001f)
            flashBase = Color.white;
        _flashEmissionColor = flashBase * Mathf.Pow(2f, EmissionIntensityStops);
    }

    private void PlayEmissionPulse()
    {
        if (_runtimeSwordMaterial == null || !_runtimeSwordMaterial.HasProperty(EmissionColorId))
            return;

        _emissionTween?.Kill();
        SetEmissionPulse(0f);
        _emissionTween = DOTween
            .Sequence()
            .Append(DOTween.To(() => 0f, SetEmissionPulse, 1f, EmissionFadeDuration))
            .AppendInterval(EmissionHoldDuration)
            .Append(DOTween.To(() => 1f, SetEmissionPulse, 0f, EmissionFadeDuration))
            .OnComplete(() => SetEmissionPulse(0f));
    }

    private void SetEmissionPulse(float progress)
    {
        if (_runtimeSwordMaterial != null && _runtimeSwordMaterial.HasProperty(EmissionColorId))
        {
            if (progress > 0f || _baseEmissionEnabled)
                _runtimeSwordMaterial.EnableKeyword("_EMISSION");
            else
                _runtimeSwordMaterial.DisableKeyword("_EMISSION");
            _runtimeSwordMaterial.SetColor(
                EmissionColorId,
                Color.LerpUnclamped(_originalEmissionColor, _flashEmissionColor, progress)
            );
            // 원본 발광 맵은 검의 일부만 흰색이라 색만 32배 높여도 대부분 보이지 않는다.
            // 펄스 동안만 전체 검에 발광을 적용하고 끝나면 원본 맵을 복원한다.
            if (_runtimeSwordMaterial.HasProperty(EmissionMapId))
                _runtimeSwordMaterial.SetTexture(
                    EmissionMapId,
                    progress > 0f ? Texture2D.whiteTexture : _originalEmissionMap
                );
        }
    }

    private void InitializeDefaultSwordInEditor()
    {
        if (_sword != null && _sword.AllSwords != null && _sword.AllSwords.Length > 0)
        {
            if (
                _swordMeshFilter != null
                && _swordMeshFilter.sharedMesh == null
                && _swordRenderer != null
            )
            {
                int defaultIdx = Mathf.Clamp(
                    _sword.DefaultSwordIndex,
                    0,
                    _sword.AllSwords.Length - 1
                );
                _swordRenderer.sharedMaterial = _sword.AllSwords[defaultIdx].Material;
                _swordMeshFilter.sharedMesh = _sword.AllSwords[defaultIdx].Mesh;
            }
        }
    }

    private void OnValidate()
    {
        InitializeDefaultSwordInEditor();

        if (!_swordRenderer)
        {
            EditorLog.LogError("Rederer가 입력되지 않았습니다!!!", this);
        }

        if (!_swordMeshFilter)
        {
            EditorLog.LogError("MeshFilter가 입력되지 않았습니다!!!", this);
        }

        if (!_sword)
        {
            EditorLog.LogError("SO_Sword가 입력되지 않았습니다!!!", this);
        }
        else if (_sword.SwordStats == null)
        {
            EditorLog.LogError("SO_SwordStats가 할당되지 않았습니다.", this);
        }
    }
}
