using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public sealed class MapPropPresenceRandomizer
{
    [SerializeField]
    [Tooltip("존재 여부를 각각 독립적으로 결정할 씬의 MapProps 프리팹 인스턴스입니다.")]
    private GameObject[] _mapProps = Array.Empty<GameObject>();

    [SerializeField, Range(0f, 1f)]
    [Tooltip("각 MapProp이 이번 경기에서 존재할 확률입니다.")]
    private float _existenceProbability = 0.5f;

    internal void Apply(int seed, bool isServer)
    {
        System.Random random = new(seed);

        for (int i = 0; i < _mapProps.Length; i++)
        {
            // 서버가 공유한 같은 시드와 배열 순서를 사용하므로 모든 피어가 같은 존재 결과를 얻는다.
            bool shouldExist = random.NextDouble() < _existenceProbability;

            GameObject mapProp = _mapProps[i];
            if (mapProp == null)
                continue;

            if (mapProp.TryGetComponent(out NetworkObject networkObject))
            {
                // NetworkObject는 서버만 제거하고 NGO의 Despawn 메시지로 모든 클라이언트에 반영한다.
                if (!shouldExist && isServer)
                {
                    if (networkObject.IsSpawned)
                        networkObject.Despawn();
                    else
                        EditorLog.LogError(
                            "랜덤 제거할 MapProp NetworkObject가 Spawn되지 않았습니다.",
                            mapProp
                        );
                }

                continue;
            }

            // NetworkObject가 없는 정적 기믹은 동일한 시드로 각 피어에서 직접 활성 상태를 맞춘다.
            mapProp.SetActive(shouldExist);
        }
    }

    internal void Validate(Component owner)
    {
        if (_mapProps == null)
        {
            EditorLog.LogError("MapProp Presence Randomizer의 Map Props 배열이 null입니다.", owner);
            return;
        }

        for (int i = 0; i < _mapProps.Length; i++)
        {
            GameObject mapProp = _mapProps[i];
            if (mapProp == null)
            {
                EditorLog.LogError($"Map Props의 {i}번 요소가 비어 있습니다.", owner);
                continue;
            }

            if (mapProp == owner.gameObject)
                EditorLog.LogError(
                    "GameModeManager 자신은 랜덤 MapProp으로 지정할 수 없습니다.",
                    owner
                );

            for (int otherIndex = i + 1; otherIndex < _mapProps.Length; otherIndex++)
            {
                if (_mapProps[otherIndex] == mapProp)
                    EditorLog.LogError(
                        $"Map Props에 {mapProp.name}이 중복 등록되어 있습니다.",
                        owner
                    );
            }
        }
    }
}

public abstract class GameModeManager : NetworkBehaviour
{
    private static GameModeManager _instance;
    public static GameModeManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<GameModeManager>();

                if (_instance == null)
                {
                    EditorLog.LogError("GameModeManager를 찾을 수 없습니다!!!");
                    return null;
                }
            }

            return _instance;
        }
    }

    [Header("시작 설정")]
    [SerializeField]
    protected Collider[] _startColliders; // 시작지점을 막는 콜라이더

    [SerializeField]
    protected GameObject _startPanel; // UI Panel

    [SerializeField]
    protected TMP_Text _countingText; // 카운트다운 텍스트

    [SerializeField]
    protected int _startCount;

    [Header("맵 소품 랜덤 존재 설정")]
    [SerializeField]
    private MapPropPresenceRandomizer _mapPropPresenceRandomizer = new();

    [Header("결과 UI 설정")]
    [SerializeField]
    protected GameObject _resultPanel; // 결과를 띄울 패널

    [SerializeField]
    protected TMP_Text _resultText; // "승리" 또는 "패배" 글자가 들어갈 텍스트

    [Header("씬 설정")]
    [SerializeField, InspectorName("메인 메뉴 씬")]
    private OnlyOneUnityString _mainMenuSceneName;

    private GameStartPresentation _startPresentation;
    private MatchEndCoordinator _matchEndCoordinator;

    protected abstract GameKind GameKind { get; }

    protected virtual void Awake()
    {
        _instance = this;
        _startPresentation = new GameStartPresentation(
            _startColliders,
            _startPanel,
            _countingText,
            _startCount
        );
        _matchEndCoordinator = new MatchEndCoordinator(_mainMenuSceneName);
    }

    public override void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayersSpawned -= OnPlayersSpawned;

        _startPresentation?.Dispose();

        if (_instance == this)
            _instance = null;

        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager.ArePlayersSpawned)
                StartGameOnServer();
            else
                gameManager.OnPlayersSpawned += OnPlayersSpawned;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (GameManager.Instance != null)
            GameManager.Instance.OnPlayersSpawned -= OnPlayersSpawned;
    }

    private void OnPlayersSpawned()
    {
        GameManager.Instance.OnPlayersSpawned -= OnPlayersSpawned;
        StartGameOnServer();
    }

    private void StartGameOnServer()
    {
        // 서버가 경기마다 새 시드를 확정하고 시작 RPC에 포함하여 존재 판정을 원자적으로 공유한다.
        int mapPropSeed = UnityEngine.Random.Range(0, int.MaxValue);
        GameStartRpc(mapPropSeed);
    }

    [Rpc(SendTo.Everyone)]
    protected virtual void GameStartRpc(int mapPropSeed)
    {
        _mapPropPresenceRandomizer?.Apply(mapPropSeed, IsServer);
        GameManager.Instance.SetGameKind(GameKind);
        _startPresentation.Play();
    }

    protected virtual void OnValidate()
    {
        _mapPropPresenceRandomizer?.Validate(this);
    }

    protected virtual void GameFinishAction()
    {
        _matchEndCoordinator.FinishMatch();
    }

    public virtual void ReportPlayerEliminated(ulong clientId) { }

    public virtual void ReportPlayerFinished(ulong clientId) { }
}
// GameModeManager은 경기 규칙과 진행 상태 중 하나의 독립된 게임플레이 책임을 담당한다.
// 모드별 정책을 분리하여 공통 경기 흐름이 구체적인 모드 구현에 직접 의존하지 않도록 한다.
