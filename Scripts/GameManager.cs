using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[System.Flags]
public enum GameKind
{
    [InspectorName("없음")]
    None = 0,

    [InspectorName("레이스")]
    Race = 1 << 0,

    [InspectorName("배틀")]
    Battle = 1 << 1,
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("플레이어 설정")]
    [Tooltip("스폰할 플레이어 캐릭터 프리팹 (NetworkObject 필수)")]
    [SerializeField, InspectorName("플레이어 프리팹")]
    private GameObject _playerPrefab;

    [Header("스폰 포인트 설정")]
    [Tooltip("플레이어들이 태어날 위치 배열 (1P, 2P, 3P, 4P)")]
    [SerializeField, InspectorName("스폰 지점 목록")]
    private Transform[] _spawnPoints;

    public GameKind GameKind { get; private set; }

    private readonly List<Player> _players = new();
    private bool _loadCallbackRegistered;

    public IReadOnlyList<Player> Players => _players;
    public bool ArePlayersSpawned { get; private set; }

    public event Action OnPlayersSpawned;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    private void Start()
    {
        if (
            Instance != this
            || RelayManager.Instance == null
            || !RelayManager.Instance.IsCustomRoom
        )
            return;

        UnityEngine.SceneManagement.Scene gameScene = gameObject.scene;
        if (gameScene.IsValid() && gameScene.isLoaded)
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(gameScene);
    }

    public void SetGameKind(GameKind gameKind)
    {
        GameKind = gameKind;
    }

    public void RegisterPlayer(Player player)
    {
        if (player != null && !_players.Contains(player))
            _players.Add(player);
    }

    public void UnregisterPlayer(Player player)
    {
        if (player != null)
            _players.Remove(player);
    }

    public void FreezeAllPlayers()
    {
        for (int i = _players.Count - 1; i >= 0; i--)
        {
            Player player = _players[i];
            if (player == null)
            {
                _players.RemoveAt(i);
                continue;
            }

            player.FreezeForMatchEnd();
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 절대 규칙: 오직 방장(서버)만이 캐릭터 스폰 권한을 가집니다.
        if (IsServer)
        {
            // 모든 플레이어가 로딩이 완료되었을 때
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnEveryPlayerLoadedScene;
            _loadCallbackRegistered = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        UnregisterSceneLoadCallback();
        base.OnNetworkDespawn();
    }

    private void OnEveryPlayerLoadedScene(
        string sceneName,
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        UnregisterSceneLoadCallback();

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
        {
            EditorLog.LogError(
                $"플레이어 씬 로딩이 완료되지 않아 스폰을 중단합니다. "
                    + $"TimedOut=[{string.Join(", ", clientsTimedOut)}]"
            );
            return;
        }

        if (_playerPrefab == null)
        {
            EditorLog.LogError("Player Prefab이 할당되지 않았습니다!");
            return;
        }

        int index = 0;

        // 현재 네트워크망에 접속해 있는 모든 '유령' 유저들의 번호(ClientId)를 가져옵니다.
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            // 1. 스폰 위치 및 회전값 지정 (인원수보다 스폰 포인트가 적을 경우를 대비한 안전장치 포함)
            Vector3 spawnPos = Vector3.zero;
            Quaternion spawnRot = Quaternion.identity;

            if (_spawnPoints != null && _spawnPoints.Length > 0)
            {
                Transform point = _spawnPoints[index % _spawnPoints.Length];
                point.GetPositionAndRotation(out spawnPos, out spawnRot);
            }

            // 2. 서버의 씬(허공)에 캐릭터 껍데기(프리팹)를 생성합니다. (Instantiate)
            GameObject playerInstance = Instantiate(_playerPrefab, spawnPos, spawnRot);

            // 활성 씬은 대기실 복귀 과정에서 바뀔 수 있으므로 플레이어의 수명을 경기 씬에 명시적으로 묶는다.
            UnityEngine.SceneManagement.Scene gameScene = gameObject.scene;
            if (playerInstance.scene != gameScene)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    playerInstance,
                    gameScene
                );
            }

            // 3. 넷코드 매니저에게 "이 캐릭터의 주인은 clientId번 유저야!" 라고 호적에 등록합니다.
            if (playerInstance.TryGetComponent<NetworkObject>(out var netObj))
            {
                // 핵심: SpawnAsPlayerObject를 호출하는 순간!
                // 해당 클라이언트의 화면에 캐릭터가 나타나고, 그 유저에게 IsOwner 권한이 쥐어집니다.
                netObj.SpawnAsPlayerObject(clientId, true);

                if (playerInstance.TryGetComponent(out PlayerSpawnHandler spawnHandler))
                {
                    spawnHandler.SetSpawnPoint(spawnPos, spawnRot);
                    spawnHandler.ForceTeleportRpc(spawnPos, spawnRot);
                }
            }

            index++;
        }

        ArePlayersSpawned = true;
        OnPlayersSpawned?.Invoke();
        EditorLog.Log("모든 플레이어 스폰 완료 및 권한 양도 끝!");
    }

    private void UnregisterSceneLoadCallback()
    {
        if (!_loadCallbackRegistered || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnEveryPlayerLoadedScene;
        _loadCallbackRegistered = false;
    }

    public override void OnDestroy()
    {
        UnregisterSceneLoadCallback();

        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }
}
