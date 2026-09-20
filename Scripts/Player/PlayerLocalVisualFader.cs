using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 몸체 Renderer만 페이드합니다.
/// 자식 계층을 탐색하지 않으므로 Sword 같은 장착물은 영향을 받지 않습니다.
/// </summary>
public sealed class PlayerLocalVisualFader : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField]
    [Tooltip("1인칭에서 숨길 PlayerVisual 몸체 Renderer만 명시적으로 등록합니다.")]
    private Renderer[] _playerVisualRenderers;

    private NetworkObject _networkObject;
    private MaterialPropertyBlock _propertyBlock;
    private bool[] _enabledBeforeFirstPerson;
    private Color[][] _originalColors;
    private int[][] _colorPropertyIds;
    private Tween _fadeTween;
    private float _visibility = 1f;
    private bool _isFirstPersonRequested;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
        _propertyBlock = new MaterialPropertyBlock();
        ResolvePlayerVisualRenderersFromLodGroup();
        BuildRendererCache();
    }

    public void FadeToFirstPerson(float duration)
    {
        if (!CanControlLocalVisual() || _isFirstPersonRequested)
            return;

        EnsureRendererCache();
        _isFirstPersonRequested = true;
        CaptureCurrentRendererState();
        StartFade(0f, duration, DisablePlayerVisualRenderers);
    }

    public void FadeToThirdPerson(float duration)
    {
        if (!CanControlLocalVisual() || !_isFirstPersonRequested)
            return;

        _isFirstPersonRequested = false;
        EnablePlayerVisualRenderers();
        StartFade(1f, duration, RestoreRendererState);
    }

    public void RestoreImmediately()
    {
        if (!CanControlLocalVisual())
            return;

        _fadeTween?.Kill();
        _fadeTween = null;
        _isFirstPersonRequested = false;
        EnablePlayerVisualRenderers();
        ApplyVisibility(1f);
        RestoreRendererState();
    }

    private void StartFade(float targetVisibility, float duration, TweenCallback onComplete)
    {
        _fadeTween?.Kill();
        _fadeTween = null;

        if (duration <= 0f)
        {
            ApplyVisibility(targetVisibility);
            onComplete?.Invoke();
            return;
        }

        _fadeTween = DOTween
            .To(() => _visibility, ApplyVisibility, targetVisibility, duration)
            .SetEase(Ease.InOutSine)
            .SetTarget(this)
            .OnComplete(() =>
            {
                _fadeTween = null;
                onComplete?.Invoke();
            });
    }

    private void CaptureCurrentRendererState()
    {
        for (int rendererIndex = 0; rendererIndex < _playerVisualRenderers.Length; rendererIndex++)
        {
            Renderer renderer = _playerVisualRenderers[rendererIndex];
            if (renderer == null)
                continue;

            _enabledBeforeFirstPerson[rendererIndex] = renderer.enabled;
            Material[] materials = renderer.sharedMaterials;
            if (_originalColors[rendererIndex].Length != materials.Length)
            {
                _originalColors[rendererIndex] = new Color[materials.Length];
                _colorPropertyIds[rendererIndex] = new int[materials.Length];
            }
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                Material material = materials[materialIndex];
                int propertyId = GetColorPropertyId(material);
                _colorPropertyIds[rendererIndex][materialIndex] = propertyId;
                _originalColors[rendererIndex][materialIndex] =
                    propertyId != 0 ? material.GetColor(propertyId) : Color.white;
            }
        }
    }

    private void ApplyVisibility(float visibility)
    {
        _visibility = Mathf.Clamp01(visibility);

        for (int rendererIndex = 0; rendererIndex < _playerVisualRenderers.Length; rendererIndex++)
        {
            Renderer renderer = _playerVisualRenderers[rendererIndex];
            if (renderer == null)
                continue;

            int materialCount = Mathf.Min(
                renderer.sharedMaterials.Length,
                _colorPropertyIds[rendererIndex].Length
            );
            for (int materialIndex = 0; materialIndex < materialCount; materialIndex++)
            {
                int propertyId = _colorPropertyIds[rendererIndex][materialIndex];
                if (propertyId == 0)
                    continue;

                renderer.GetPropertyBlock(_propertyBlock, materialIndex);
                Color color = _originalColors[rendererIndex][materialIndex];
                color.a *= _visibility;
                _propertyBlock.SetColor(propertyId, color);
                renderer.SetPropertyBlock(_propertyBlock, materialIndex);
                _propertyBlock.Clear();
            }
        }
    }

    private void DisablePlayerVisualRenderers()
    {
        if (!_isFirstPersonRequested)
            return;

        for (int i = 0; i < _playerVisualRenderers.Length; i++)
        {
            if (_playerVisualRenderers[i] != null && _enabledBeforeFirstPerson[i])
                _playerVisualRenderers[i].enabled = false;
        }
    }

    private void EnablePlayerVisualRenderers()
    {
        for (int i = 0; i < _playerVisualRenderers.Length; i++)
        {
            if (_playerVisualRenderers[i] != null && _enabledBeforeFirstPerson[i])
                _playerVisualRenderers[i].enabled = true;
        }
    }

    private void RestoreRendererState()
    {
        if (_isFirstPersonRequested)
            return;

        for (int i = 0; i < _playerVisualRenderers.Length; i++)
        {
            if (_playerVisualRenderers[i] != null)
                _playerVisualRenderers[i].enabled = _enabledBeforeFirstPerson[i];
        }
    }

    private void BuildRendererCache()
    {
        int rendererCount = _playerVisualRenderers?.Length ?? 0;
        _enabledBeforeFirstPerson = new bool[rendererCount];
        _originalColors = new Color[rendererCount][];
        _colorPropertyIds = new int[rendererCount][];

        for (int rendererIndex = 0; rendererIndex < rendererCount; rendererIndex++)
        {
            Renderer renderer = _playerVisualRenderers[rendererIndex];
            int materialCount = renderer != null ? renderer.sharedMaterials.Length : 0;
            _originalColors[rendererIndex] = new Color[materialCount];
            _colorPropertyIds[rendererIndex] = new int[materialCount];
            _enabledBeforeFirstPerson[rendererIndex] = renderer != null && renderer.enabled;
        }

        CaptureCurrentRendererState();
    }

    private void EnsureRendererCache()
    {
        if (
            _enabledBeforeFirstPerson == null
            || _enabledBeforeFirstPerson.Length != (_playerVisualRenderers?.Length ?? 0)
        )
        {
            BuildRendererCache();
        }
    }

    private bool CanControlLocalVisual()
    {
        return _networkObject == null || !_networkObject.IsSpawned || _networkObject.IsOwner;
    }

    private void ResolvePlayerVisualRenderersFromLodGroup()
    {
        if (_playerVisualRenderers is { Length: > 0 })
            return;

        LODGroup playerVisualLodGroup = GetComponentInChildren<LODGroup>(true);
        if (playerVisualLodGroup == null)
            return;

        LOD[] lods = playerVisualLodGroup.GetLODs();
        int rendererCount = 0;
        for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
        {
            Renderer[] renderers = lods[lodIndex].renderers;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] is MeshRenderer or SkinnedMeshRenderer)
                    rendererCount++;
            }
        }

        _playerVisualRenderers = new Renderer[rendererCount];
        int destinationIndex = 0;
        for (int lodIndex = 0; lodIndex < lods.Length; lodIndex++)
        {
            Renderer[] renderers = lods[lodIndex].renderers;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                if (renderers[rendererIndex] is MeshRenderer or SkinnedMeshRenderer)
                    _playerVisualRenderers[destinationIndex++] = renderers[rendererIndex];
            }
        }
    }

    private static int GetColorPropertyId(Material material)
    {
        if (material == null)
            return 0;
        if (material.HasProperty(BaseColorId))
            return BaseColorId;
        return material.HasProperty(ColorId) ? ColorId : 0;
    }

    private void OnDisable()
    {
        _fadeTween?.Kill();
        _fadeTween = null;

        if (CanControlLocalVisual())
        {
            _isFirstPersonRequested = false;
            EnablePlayerVisualRenderers();
            ApplyVisibility(1f);
            RestoreRendererState();
        }
    }

    private void OnValidate()
    {
        if (_playerVisualRenderers == null || _playerVisualRenderers.Length == 0)
            EditorLog.LogError("PlayerVisual 몸체 Renderer가 할당되지 않았습니다.", this);
    }
}
