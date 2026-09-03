using UnityEngine;

/// <summary>
/// Timeline의 Control Track 또는 런타임에 소환되어 시전자(플레이어)의 검 메시를 자동 인식하고
/// 칼날 형태에 맞춰 파티클을 방출하는 무기 인챈트 이펙트 컴포넌트입니다.
/// </summary>
[ExecuteAlways]
public sealed class WeaponEnchantEffect : MonoBehaviour
{
    [Header("파티클 설정")]
    [SerializeField]
    private ParticleSystem[] _particleSystems;

    [SerializeField, InspectorName("메시 방출 모드")]
    private ParticleSystemMeshShapeType _meshShapeType = ParticleSystemMeshShapeType.Edge;

    private void Awake()
    {
        CacheParticleSystems();
    }

    private void OnEnable()
    {
        CacheParticleSystems();
        BindToSword();
    }

    private void Update()
    {
        // 에디터 모드에서 타임라인 재생 헤드를 스크러빙할 때 실시간으로 검 위치 추적 및 바인딩 유지
        if (!Application.isPlaying)
        {
            PlayerSwordSync swordSync = GetComponentInParent<PlayerSwordSync>();
            if (swordSync == null)
                swordSync = FindAnyObjectByType<PlayerSwordSync>();

#if UNITY_EDITOR
            if (swordSync == null)
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null)
                    swordSync = stage.prefabContentsRoot.GetComponentInChildren<PlayerSwordSync>();
            }
#endif

            if (swordSync != null && swordSync.SwordMeshRenderer != null)
            {
                transform.position = swordSync.SwordMeshRenderer.transform.position;
                transform.rotation = swordSync.SwordMeshRenderer.transform.rotation;
            }
        }
    }

    /// <summary>
    /// 시전자 계층 구조에서 PlayerSwordSync를 찾아 검의 자식으로 등록하고
    /// 검의 MeshRenderer를 파티클 Shape에 바인딩합니다.
    /// </summary>
    public void BindToSword()
    {
        // 1. 부모 계층에서 PlayerSwordSync 탐색 (Control Track이 플레이어 하위에 스폰한 경우)
        PlayerSwordSync swordSync = GetComponentInParent<PlayerSwordSync>();

        // 2. 만약 루트에 스폰되었더라도 루트 계층에서 탐색
        if (swordSync == null && transform.root != null)
            swordSync = transform.root.GetComponentInChildren<PlayerSwordSync>();

        // 3. 에디터 프리뷰 중 트랙 바인딩이 누락되어 독립 스폰된 경우 씬에서 탐색
        if (swordSync == null && !Application.isPlaying)
            swordSync = FindAnyObjectByType<PlayerSwordSync>();

#if UNITY_EDITOR
        // 4. 프리팹 모드(Prefab Stage) 안에서 테스트 중인 경우 프리팹 루트에서 탐색
        if (swordSync == null && !Application.isPlaying)
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
                swordSync = stage.prefabContentsRoot.GetComponentInChildren<PlayerSwordSync>();
        }
#endif

        if (swordSync == null || swordSync.SwordMeshRenderer == null)
            return;

        MeshRenderer swordRenderer = swordSync.SwordMeshRenderer;

        // 4. 런타임에는 검의 자식으로 등록하여 무비용 네이티브 추적, 에디터에서는 안전하게 위치 동기화
        if (Application.isPlaying)
        {
            transform.SetParent(swordRenderer.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
        else
        {
            transform.position = swordRenderer.transform.position;
            transform.rotation = swordRenderer.transform.rotation;
        }

        // 4. 파티클 시스템들의 Shape 모듈에 검 메시 연결
        if (_particleSystems != null)
        {
            Mesh swordMesh =
                swordSync.SwordMeshFilter != null ? swordSync.SwordMeshFilter.sharedMesh : null;

            foreach (ParticleSystem ps in _particleSystems)
            {
                if (ps == null)
                    continue;

                ParticleSystem.ShapeModule shape = ps.shape;
                shape.enabled = true;

                if (swordMesh != null)
                {
                    shape.shapeType = ParticleSystemShapeType.Mesh;
                    shape.mesh = swordMesh;
                }
                else
                {
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshRenderer = swordRenderer;
                }

                shape.meshShapeType = _meshShapeType;
            }
        }
    }

    private void CacheParticleSystems()
    {
        if (_particleSystems == null || _particleSystems.Length == 0)
            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void OnValidate()
    {
        CacheParticleSystems();
    }
}
