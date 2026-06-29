using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[System.Flags]
public enum GameKind
{
    None = 0,
    Race = 1 << 0,
    Battle = 1 << 1,
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("플레이어 설정")]
    [Tooltip("스폰할 플레이어 캐릭터 프리팹 (NetworkObject 필수)")]
    [SerializeField]
    private GameObject _playerPrefab;

    [Header("스폰 포인트 설정")]
    [Tooltip("플레이어들이 태어날 위치 배열 (1P, 2P, 3P, 4P)")]
    [SerializeField]
    private Transform[] _spawnPoints;

    public GameKind GameKind { get; set; }

    public readonly List<Player> players = new();

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
            Destroy(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // 절대 규칙: 오직 방장(서버)만이 캐릭터 스폰 권한을 가집니다.
        if (IsServer)
        {
            // 모든 플레이어가 로딩이 완료되었을 때
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnEveryPlayerLoadedScene;
        }
    }

    private void OnEveryPlayerLoadedScene(
        string sceneName,
        UnityEngine.SceneManagement.LoadSceneMode loadSceneMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut
    )
    {
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnEveryPlayerLoadedScene;

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

            // 3. 넷코드 매니저에게 "이 캐릭터의 주인은 clientId번 유저야!" 라고 호적에 등록합니다.
            if (playerInstance.TryGetComponent<NetworkObject>(out var netObj))
            {
                // 핵심: SpawnAsPlayerObject를 호출하는 순간!
                // 해당 클라이언트의 화면에 캐릭터가 나타나고, 그 유저에게 IsOwner 권한이 쥐어집니다.
                netObj.SpawnAsPlayerObject(clientId);

                if (playerInstance.TryGetComponent(out PlayerSpawnHandler spawnHandler))
                {
                    spawnHandler.ForceTeleportRpc(spawnPos, spawnRot);
                }
            }

            players.Add(playerInstance.GetComponent<Player>());

            index++;
        }

        EditorLog.Log("모든 플레이어 스폰 완료 및 권한 양도 끝!");
    }
}
