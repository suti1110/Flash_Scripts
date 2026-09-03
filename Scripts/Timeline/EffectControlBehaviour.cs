using System;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// EffectControlTrack 클립의 런타임 및 에디터 프리뷰 생명주기와
/// 위치/회전 오프셋 및 파티클 시뮬레이션을 동기화하는 PlayableBehaviour입니다.
/// </summary>
[Serializable]
public sealed class EffectControlBehaviour : PlayableBehaviour
{
    private GameObject _prefab;
    private Transform _parentTransform;
    private Vector3 _positionOffset;
    private Vector3 _rotationOffset;
    private Vector3 _scaleMultiplier = Vector3.one;
    private bool _worldSpacePosition;
    private bool _simulateParticles = true;

    private GameObject _instance;
    private ParticleSystem[] _particleSystems;
    private bool _isInitialized;

    public void Setup(
        GameObject prefab,
        Transform parentTransform,
        Vector3 positionOffset,
        Vector3 rotationOffset,
        Vector3 scaleMultiplier,
        bool worldSpacePosition,
        bool simulateParticles)
    {
        _prefab = prefab;
        _parentTransform = parentTransform;
        _positionOffset = positionOffset;
        _rotationOffset = rotationOffset;
        _scaleMultiplier = scaleMultiplier == Vector3.zero ? Vector3.one : scaleMultiplier;
        _worldSpacePosition = worldSpacePosition;
        _simulateParticles = simulateParticles;
    }

    public override void OnGraphStart(Playable playable)
    {
        _isInitialized = false;
    }

    public override void OnGraphStop(Playable playable)
    {
        DestroyInstance();
    }

    public override void OnPlayableDestroy(Playable playable)
    {
        DestroyInstance();
    }

    public override void OnBehaviourPlay(Playable playable, FrameData info)
    {
        EnsureInstanceCreated();
        if (_instance != null)
        {
            _instance.SetActive(true);
            UpdateTransform();
        }
    }

    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        // 클립 구간을 벗어났거나 재생이 일시정지되었을 때 인스턴스 정리
        if (_instance != null)
        {
            if (Application.isPlaying)
            {
                _instance.SetActive(false);
            }
            else
            {
                // 에디터 프리뷰에서는 씬이 지저분해지지 않도록 즉시 정리
                DestroyInstance();
            }
        }
    }

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        // 트랙 바인딩에 GameObject가 들어왔고 클립 레벨 부모가 지정되지 않았다면 트랙 바인딩을 기본 부모로 사용
        if (_parentTransform == null && playerData is GameObject boundGo)
        {
            _parentTransform = boundGo.transform;
        }

        EnsureInstanceCreated();

        if (_instance == null) return;

        // 1. 실시간 위치, 회전, 스케일 오프셋 동기화
        UpdateTransform();

        // 2. 파티클 시스템 타임라인 시간 동기화 (에디터 스크러빙 프리뷰 지원)
        if (_simulateParticles && _particleSystems != null)
        {
            float clipTime = (float)playable.GetTime();
            for (int i = 0; i < _particleSystems.Length; i++)
            {
                ParticleSystem ps = _particleSystems[i];
                if (ps != null)
                {
                    ps.Simulate(clipTime, true, false, true);
                }
            }
        }
    }

    private void EnsureInstanceCreated()
    {
        if (_prefab == null) return;

        if (_instance == null)
        {
            _instance = UnityEngine.Object.Instantiate(_prefab);
            _instance.name = $"{_prefab.name} (Timeline Preview)";

            // 에디터 모드에서 씬 저장 시 클론 오브젝트가 저장되지 않도록 방지
            if (!Application.isPlaying)
            {
                _instance.hideFlags = HideFlags.DontSave;
            }

            _particleSystems = _instance.GetComponentsInChildren<ParticleSystem>(true);
            UpdateTransform();
            _instance.SetActive(true);
        }
    }

    private void UpdateTransform()
    {
        if (_instance == null) return;

        if (_worldSpacePosition || _parentTransform == null)
        {
            _instance.transform.SetParent(null, false);
            _instance.transform.position = _positionOffset;
            _instance.transform.rotation = Quaternion.Euler(_rotationOffset);
        }
        else
        {
            _instance.transform.SetParent(_parentTransform, false);
            _instance.transform.localPosition = _positionOffset;
            _instance.transform.localRotation = Quaternion.Euler(_rotationOffset);
        }

        Vector3 baseScale = _prefab != null ? _prefab.transform.localScale : Vector3.one;
        _instance.transform.localScale = Vector3.Scale(baseScale, _scaleMultiplier);
    }

    private void DestroyInstance()
    {
        if (_instance != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(_instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(_instance);
            }
            _instance = null;
            _particleSystems = null;
        }
    }
}
