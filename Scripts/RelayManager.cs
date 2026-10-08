using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

// UI가 표시할 매칭 진행 단계다. 실제 네트워크 접속 여부는 NetworkManager에서 따로 확인한다.
// 일반 매칭의 정상 순서: FindingMatch → PendingPlayer → GameStart → SelectMode → SelectMap.
// 사용자 정의 방은 직접 고른 맵을 사용하므로 모드·맵 추첨 단계를 거치지 않는다.
public enum MatchingState
{
    // 매칭 전 또는 세션 정리 후의 기본 상태.
    None,
    // 로비를 검색하거나 생성·참가하는 중.
    FindingMatch,
    // 방에 접속하여 다른 플레이어 또는 호스트의 시작 요청을 기다리는 중.
    PendingPlayer,
    // 새 참가를 막고 게임 시작 절차를 진행하는 중.
    GameStart,
    // 서버가 선택한 게임 모드를 클라이언트에 표시하는 중.
    SelectMode,
    // 서버가 선택한 맵을 표시하고 경기 씬으로 넘어가는 중.
    SelectMap,
}

/// <summary>
/// 일반 매칭, 사용자 정의 방, 연습 모드의 세션 진입과 종료를 조율한다.
/// Lobby는 방 목록·참가자 정보를 관리하고, Relay는 실제 게임 통신을 중계한다.
/// 두 서비스는 따로 성공하거나 실패할 수 있으므로 중간 실패 시 양쪽을 함께 정리해야 한다.
/// </summary>
/// <remarks>
/// 읽는 순서: StartMatchmaking / CreateCustomRoomAsync / JoinCustomRoomByIdAsync /
/// StartPracticeModeAsync → 접속 콜백 → StartGameAsync → 복귀 또는 세션 종료.
/// 호스트는 서버와 로컬 클라이언트를 함께 실행한다. IsServer 분기는 서버 권한 작업이고,
/// ClientRpc는 서버가 접속한 클라이언트들에게 실행을 요청하는 메서드다.
/// 씬이 바뀌어도 유지되는 객체이므로 이벤트 구독과 이전 세션 상태를 반드시 정리한다.
/// </remarks>
public class RelayManager : NetworkBehaviour
{
    public const string IncorrectRoomPasswordReason = "Incorrect room password.";

    private const int MigrationAttemptCount = 3;
    private const int MigrationRetryDelayMilliseconds = 500;

    public static RelayManager Instance { get; private set; }

    [Header("게임 설정")]
    public int MaxPlayers { get; private set; } = 4;

    [InspectorName("게임 시작 대기 시간")]
    public int GameStartTerm = 3;

    [Min(0f), InspectorName("모드 선택 연출 시간")]
    public float ModeSelectionDuration = 4f;

    [Min(0f), InspectorName("모드 결과 표시 시간")]
    public float ModeResultDisplayDuration = 2f;

    [Min(0f), InspectorName("맵 선택 연출 시간")]
    public float MapSelectionDuration = 4f;

    [Min(0f), InspectorName("맵 로딩 추가 대기 시간")]
    public float MapLoadDelay = 2f;

    [SerializeField, InspectorName("맵 카탈로그")]
    private SO_MapCatalog _mapCatalog;

    [SerializeField, InspectorName("메인 메뉴 씬")]
    private OnlyOneUnityString _mainMenuSceneName;

    [SerializeField, InspectorName("사용자 정의 방 씬")]
    private OnlyOneUnityString _customRoomSceneName;

    [Header("로비 설정")]
    [InspectorName("로비 이름")]
    public string LobbyName = "My Game Room";

    // UI와 다른 시스템이 읽는 현재 세션 정보. 표시 이름과 실제 식별자/씬 이름을 구분한다.
    public string TempJoinCode { get; private set; } = string.Empty;
    public string GameModeId { get; private set; }
    public string GameModeDisplayName { get; private set; }
    public string MapName { get; private set; }
    public string MapDisplayName { get; private set; }
    public bool IsPracticeMode { get; private set; }
    public bool IsCustomRoom { get; private set; }
    public string CurrentRoomId => _lobbySession?.CurrentLobby?.Id ?? string.Empty;
    // 네트워크 스폰 전에는 로컬 준비 값을, 스폰 후에는 서버가 동기화한 값을 사용한다.
    public CustomRoomSettings CurrentRoomSettings =>
        IsSpawned ? _roomSettings.Value : _configuredRoomSettings;
    public CustomRoomMapSelection CurrentCustomRoomMapSelection =>
        IsSpawned ? _customRoomMapSelection.Value : _configuredMapSelection;
    // 네트워크 데이터 변경을 UI에 전달한다. CustomRoomLeft는 방 이탈 화면 처리를 요청한다.
    public event Action<CustomRoomSettings> RoomSettingsChanged;
    public event Action<CustomRoomMapSelection> CustomRoomMapSelectionChanged;
    public event Action CustomRoomLeft;

    // NGO의 접속 ID(ulong)와 Unity 인증/Lobby의 플레이어 ID(string)는 서로 다른 값이다.
    // 연결이 끊긴 플레이어를 Lobby에서도 제거하기 위해 서버가 두 ID의 대응 관계를 보관한다.
    private readonly Dictionary<ulong, string> _clientToPlayerId = new();
    // 동일 클라이언트의 ID 검증 요청이 동시에 여러 번 실행되는 것을 막는다.
    private readonly HashSet<ulong> _pendingPlayerIdReports = new();
    // await 중 세션이 바뀌면 이전 세션의 ID 검증 결과를 버리기 위한 세대 번호다.
    private int _playerIdReportVersion;
    // 카탈로그 검색 결과를 재사용하는 임시 목록이다. 확정된 선택 값과는 별개다.
    private readonly List<SO_GameModeDefinition> _onlineModeBuffer = new();
    private readonly List<SO_MapDefinition> _onlineMapBuffer = new();
    // 경기 중 숨긴 대기실 루트만 기록하여 복귀 시 원래 켜져 있던 객체만 다시 켠다.
    private readonly List<GameObject> _hiddenCustomRoomRoots = new();
    // 방 설정과 선택 맵은 서버만 수정하며, NGO가 현재 값을 모든 클라이언트에 동기화한다.
    private readonly NetworkVariable<CustomRoomSettings> _roomSettings = new(
        CustomRoomSettings.Default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly NetworkVariable<CustomRoomMapSelection> _customRoomMapSelection = new(
        CustomRoomMapSelection.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // 호스트 시작 전에도 설정이 필요하므로 NetworkVariable과 별도로 준비 값을 보관한다.
    private CustomRoomSettings _configuredRoomSettings = CustomRoomSettings.Default;
    private CustomRoomMapSelection _configuredMapSelection = CustomRoomMapSelection.None;
    // 호스트가 비교에 사용하는 비밀번호 해시. 접속 payload 자체는 UTF-8 비밀번호다.
    private byte[] _customRoomPasswordHash = Array.Empty<byte>();
    // 방에서 변경한 중력은 세션 정리 시 Awake에서 저장한 기준값으로 복원한다.
    private Vector3 _defaultGravity;

    // 인증 초기화, Lobby API, 주기 유지 작업, Relay 접속, 씬 로딩의 실제 구현을 위임한다.
    // 이 클래스는 각 작업을 어떤 순서로 실행하고 실패 시 어디로 복구할지 결정한다.
    private UnityServicesSession _servicesSession;
    private LobbySession _lobbySession;
    private LobbyMaintenance _lobbyMaintenance;
    private RelayConnection _relayConnection;
    private MatchFlowCoordinator _matchFlow;

    // Cancel 정리가 끝나기 전에 새 매치메이킹이 시작되면 삭제 중인 Lobby를 다시 찾을 수 있다.
    // Lobby 생성/참가와 이탈을 하나의 전환 구간으로 직렬화하여 두 작업이 겹치지 않게 한다.
    private readonly SemaphoreSlim _matchmakingTransition = new(1, 1);

    // 방 이동·나가기 때문에 끊은 연결을 호스트의 비정상 이탈로 오인하지 않게 한다.
    private bool _isIntentionalDisconnect;
    // 스폰/재접속 때 같은 콜백을 중복 구독하지 않게 한다.
    private bool _networkCallbacksRegistered;
    // 접속 콜백 또는 시작 버튼이 반복되어도 게임 시작 시퀀스는 한 번만 실행한다.
    private bool _isStartingGame;
    // 다른 방으로 이동하는 동안 중복 병합을 막는다.
    private bool _isMigrating;
    // 전송 오류가 연속으로 발생해도 복구 작업을 중복 실행하지 않는다.
    private bool _isHandlingTransportFailure;
    // 복귀 중 로딩 UI의 수명 토큰. Dispose하면 해당 로딩 표시를 종료한다.
    private IDisposable _customRoomReturnLoading;

    private MatchingState _matchingState;
    // 값이 실제로 바뀔 때만 알린다. 이 속성 자체는 NetworkVariable이 아니므로
    // 모든 피어에 알려야 하는 진행 단계는 UpdateMatchingStateClientRpc 등을 거친다.
    public MatchingState MatchingState
    {
        get => _matchingState;
        private set
        {
            if (value == _matchingState)
                return;

            _matchingState = value;
            OnMatchingStateChanged?.Invoke(value);
        }
    }

    public event Action<MatchingState> OnMatchingStateChanged;

    // 씬 전환에도 유지되는 단일 관리자를 만들고, 세션 작업을 담당할 하위 객체를 준비한다.
    private void Awake()
    {
        Application.runInBackground = true;
        _defaultGravity = Physics.gravity;

        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _servicesSession = new UnityServicesSession();
        _lobbySession = new LobbySession();
        _lobbyMaintenance = new LobbyMaintenance(_lobbySession);
        _relayConnection = new RelayConnection();
        _matchFlow = new MatchFlowCoordinator();
    }

    /// <summary>
    /// 지정 인원으로 일반 매칭을 시작한다. 진행 중인 매칭 진입/이탈 작업이 끝난 뒤 실행한다.
    /// 검색·접속 실패는 내부에서 정리하고 MatchingState를 None으로 되돌린다.
    /// </summary>
    public async Task StartMatchmaking(int targetPlayerCount)
    {
        await _matchmakingTransition.WaitAsync();
        try
        {
            await StartMatchmakingCoreAsync(targetPlayerCount);
        }
        finally
        {
            _matchmakingTransition.Release();
        }
    }

    // 전환 잠금을 이미 확보한 호출자가 사용하는 본체다.
    // 방이 검색 직후 꽉 찬 경우에도 이 메서드로 재검색하여 같은 잠금을 다시 기다리지 않는다.
    private async Task StartMatchmakingCoreAsync(int targetPlayerCount)
    {
        try
        {
            if (IsPracticeMode)
            {
                SetIntentionalDisconnect();
                await _relayConnection.ShutdownAsync();
            }

            ResetPracticeState();
            ValidateOnlineCatalog(targetPlayerCount);
            await _servicesSession.EnsureInitializedAsync();

            MaxPlayers = targetPlayerCount;
            MatchingState = MatchingState.FindingMatch;
            EditorLog.Log($"{MaxPlayers}인용 매치메이킹 시작...");

            Lobby availableLobby = await _lobbySession.FindAvailableAsync(MaxPlayers);
            if (availableLobby != null)
            {
                EditorLog.Log($"기존 방 발견! {availableLobby.Name}");
                await JoinExistingLobbyAsync(availableLobby);
            }
            else
            {
                EditorLog.Log("대기 중인 방 없음. 새 방 생성...");
                await CreateLobbyWithRelayAsync();
            }
        }
        catch (Exception exception)
        {
            // Lobby 참가까지 성공한 뒤 Relay 접속이 실패할 수 있다. 이때 가입 흔적을 남기면
            // 죽은 Relay 코드를 가진 방이 인원만 찬 채 Query에 계속 노출되므로 함께 정리한다.
            SetIntentionalDisconnect();
            StopLobbyMaintenance();
            await ReleaseCurrentLobbyAsync();

            try
            {
                await _relayConnection.ShutdownAsync();
            }
            catch (Exception cleanupException)
            {
                EditorLog.LogWarning($"매치메이킹 실패 후 네트워크 정리 실패: {cleanupException}");
            }

            MatchingState = MatchingState.None;
            ResetPracticeState();
            EditorLog.LogError($"매치메이킹 실패: {exception}");
        }
    }

    /// <summary>
    /// 이전 세션을 정리한 뒤 호스트로 사용자 정의 방을 만든다.
    /// Relay 호스트 시작 → Lobby에 방 공개 → 대기 순서이며, 실패하면 정리 후 예외를 호출자에게 전달한다.
    /// </summary>
    public async Task CreateCustomRoomAsync(
        string roomName,
        string password,
        int maxPlayers,
        CustomRoomSettings settings,
        bool isPublic = true
    )
    {
        if (string.IsNullOrWhiteSpace(roomName))
            throw new ArgumentException("Room name is required.", nameof(roomName));
        if (maxPlayers < 2 || maxPlayers > 4)
            throw new ArgumentOutOfRangeException(nameof(maxPlayers), "Player count must be 2-4.");
        if ((password ?? string.Empty).Length > 64)
            throw new ArgumentException("Password must be 64 characters or fewer.", nameof(password));

        try
        {
            SetIntentionalDisconnect();
            StopLobbyMaintenance();
            await ReleaseCurrentLobbyAsync();
            await _relayConnection.ShutdownAsync();
            ResetPracticeState();
            await _servicesSession.EnsureInitializedAsync();

            LobbyName = roomName.Trim();
            MaxPlayers = maxPlayers;
            IsCustomRoom = true;
            _configuredRoomSettings = settings.Validated();
            _customRoomPasswordHash = HashPassword(password);
            MatchingState = MatchingState.FindingMatch;

            TempJoinCode = await _relayConnection.StartHostAsync(MaxPlayers);
            if (IsServer)
                _roomSettings.Value = _configuredRoomSettings;

            Lobby lobby = await _lobbySession.CreateCustomAsync(
                LobbyName,
                MaxPlayers,
                TempJoinCode,
                isPublic,
                _customRoomPasswordHash.Length > 0,
                _configuredRoomSettings
            );

            EditorLog.Log($"Custom room created. Room ID: {lobby.Id}");
            MatchingState = MatchingState.PendingPlayer;
            StartHeartbeat();
            StartPolling();
        }
        catch
        {
            SetIntentionalDisconnect();
            await ReleaseCurrentLobbyAsync();
            await _relayConnection.ShutdownAsync();
            MatchingState = MatchingState.None;
            ResetPracticeState();
            throw;
        }
    }

    /// <summary>공개 사용자 정의 방 목록을 조회하여 UI에 필요한 요약 정보로 변환한다. 방에 참가하지는 않는다.</summary>
    public async Task<IReadOnlyList<CustomRoomSummary>> GetPublicCustomRoomsAsync()
    {
        await _servicesSession.EnsureInitializedAsync();
        IReadOnlyList<Lobby> lobbies = await _lobbySession.FindPublicCustomRoomsAsync();
        List<CustomRoomSummary> rooms = new(lobbies.Count);

        for (int i = 0; i < lobbies.Count; i++)
            rooms.Add(CreateCustomRoomSummary(lobbies[i]));

        return rooms;
    }

    /// <summary>
    /// Lobby에 먼저 참가해 방 종류·버전·잠금 상태를 확인하고, 방의 Relay 코드로 실제 접속한다.
    /// Relay 접속이 실패하면 Lobby 참가 기록도 제거하고 예외를 호출자에게 전달한다.
    /// </summary>
    public async Task JoinCustomRoomByIdAsync(string roomId, string password)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room ID is required.", nameof(roomId));
        if ((password ?? string.Empty).Length > 64)
            throw new ArgumentException("Password must be 64 characters or fewer.", nameof(password));

        // 실제로 참가가 완료된 경우에만 실패 처리에서 자신의 Lobby 참가 기록을 제거한다.
        bool joinedLobby = false;
        try
        {
            SetIntentionalDisconnect();
            StopLobbyMaintenance();
            await ReleaseCurrentLobbyAsync();
            await _relayConnection.ShutdownAsync();
            ResetPracticeState();
            await _servicesSession.EnsureInitializedAsync();

            MatchingState = MatchingState.FindingMatch;
            Lobby joined = await _lobbySession.JoinByIdAsync(roomId.Trim());
            joinedLobby = true;
            if (!LobbySession.IsCustomRoom(joined))
                throw new InvalidOperationException("The requested room is not a custom room.");
            if (!LobbySession.IsCompatibleVersion(joined))
                throw new InvalidOperationException("The room uses a different game version.");
            if (joined.IsLocked)
                throw new InvalidOperationException("The game in this room has already started.");
            MaxPlayers = joined.MaxPlayers;
            LobbyName = joined.Name;
            IsCustomRoom = true;
            _configuredRoomSettings = LobbySession.GetCustomRoomSettings(joined);

            string relayCode = LobbySession.GetRelayCode(joined);
            if (string.IsNullOrWhiteSpace(relayCode))
                throw new InvalidOperationException("The room has no Relay connection code.");

            await _relayConnection.StartClientAsync(relayCode, password);
            MatchingState = MatchingState.PendingPlayer;
            StartPolling();
        }
        catch
        {
            SetIntentionalDisconnect();
            await _relayConnection.ShutdownAsync();

            if (joinedLobby && _lobbySession.HasLobby)
            {
                try
                {
                    await _lobbySession.RemoveLocalPlayerAsync();
                }
                catch
                {
                    _lobbySession.Clear();
                }
            }

            MatchingState = MatchingState.None;
            ResetPracticeState();
            throw;
        }
    }

    // 서비스 응답 전체를 UI에 넘기지 않고 방 목록 표시에 필요한 항목만 추린다.
    private static CustomRoomSummary CreateCustomRoomSummary(Lobby lobby)
    {
        return new CustomRoomSummary(
            lobby.Id,
            lobby.Name,
            lobby.Players?.Count ?? 0,
            lobby.MaxPlayers,
            LobbySession.HasPassword(lobby),
            LobbySession.GetCustomRoomSettings(lobby)
        );
    }

    /// <summary>
    /// 사용자 정의 방 호스트가 게임 시작 전에 설정을 변경한다.
    /// 접속자는 NetworkVariable로, 방 목록 조회자는 Lobby 데이터로 변경을 확인한다.
    /// 설정이 바뀌면 참가자에게 다시 준비를 확인받는다.
    /// </summary>
    public async Task UpdateCustomRoomSettingsAsync(CustomRoomSettings settings)
    {
        if (!IsCustomRoom || !IsServer || !_lobbySession.IsLocalPlayerHost)
            throw new InvalidOperationException("Only the custom room host can change settings.");
        if (_isStartingGame)
            throw new InvalidOperationException("Room settings cannot change after the game starts.");

        _configuredRoomSettings = settings.Validated();
        _roomSettings.Value = _configuredRoomSettings;
        await _lobbySession.UpdateCustomSettingsAsync(_configuredRoomSettings);
        if (CustomRoomReadyState.Instance != null)
            CustomRoomReadyState.Instance.ResetGuestReadyStates();
    }

    /// <summary>호스트 권한, 맵 선택, 전원 준비를 확인한 뒤 사용자 정의 방의 게임 시작을 요청한다.</summary>
    public Task StartCustomRoomGameAsync()
    {
        if (!IsCustomRoom || !IsServer || !_lobbySession.IsLocalPlayerHost)
            throw new InvalidOperationException("Only the custom room host can start the game.");
        if (!CurrentCustomRoomMapSelection.HasSelection)
            throw new InvalidOperationException("Select a mode and map before starting.");
        if (CustomRoomReadyState.Instance == null || !CustomRoomReadyState.Instance.AreAllPlayersReady)
            throw new InvalidOperationException("All players must be ready before starting.");

        return StartGameAsync();
    }

    /// <summary>
    /// 연결을 유지한 채 경기 씬을 정리하고 기존 대기실로 복귀한다. 실제 전환은 서버만 수행한다.
    /// 복귀 준비 RPC → 로비 잠금 해제 → 경기 플레이어 제거 → 경기 씬 언로드 → 대기실 표시 순서다.
    /// </summary>
    public async Task ReturnToCustomRoomAsync()
    {
        if (!IsCustomRoom || !_lobbySession.HasLobby)
            throw new InvalidOperationException("The custom room session is no longer available.");
        if (!IsServer)
            return;
        if (_customRoomSceneName == null || string.IsNullOrWhiteSpace(_customRoomSceneName))
            throw new InvalidOperationException("The custom room scene is not assigned.");

        _matchFlow.CancelPendingSceneLoad();
        _isStartingGame = false;

        EditorLog.Log(
            $"[CustomRoomReturn][Server] 복귀 시작. LocalClient={NetworkManager.LocalClientId}, "
                + $"ActiveScene={SceneManager.GetActiveScene().name}, Map={MapName}"
        );

        PrepareCustomRoomReturnClientRpc();

        if (CustomRoomReadyState.Instance != null)
            CustomRoomReadyState.Instance.ResetGuestReadyStates();

        try
        {
            await _lobbySession.UnlockAsync();
        }
        catch (Exception exception)
        {
            EditorLog.LogWarning($"사용자 정의 방 잠금 해제 실패: {exception}");
        }

        UpdateMatchingStateClientRpc(MatchingState.PendingPlayer);

        Scene gameScene = GameManager.Instance != null
            ? GameManager.Instance.gameObject.scene
            : SceneManager.GetSceneByName(MapName);
        EditorLog.Log(
            $"[CustomRoomReturn][Server] 경기 씬 확인. Scene={gameScene.name}, "
                + $"Handle={gameScene.handle}, Valid={gameScene.IsValid()}, Loaded={gameScene.isLoaded}"
        );
        if (!gameScene.IsValid() || !gameScene.isLoaded)
        {
            ShowCustomRoomSceneClientRpc();
            throw new InvalidOperationException("The custom room game scene is not loaded.");
        }

        DespawnMatchPlayerObjects();
        await UnloadNetworkSceneAsync(gameScene, ShowCustomRoomSceneClientRpc);
    }

    // 경기용 PlayerObject를 서버에서 먼저 제거한다. 씬 언로드 전에 제거 여부도 확인하여
    // 다음 경기 시작 시 이전 플레이어 객체가 남는 문제를 드러낸다.
    private void DespawnMatchPlayerObjects()
    {
        NetworkManager networkManager = NetworkManager;
        if (networkManager == null || !networkManager.IsServer)
            throw new InvalidOperationException("Only the server can despawn match players.");

        List<NetworkObject> playerObjects = new();
        foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
        {
            if (networkObject != null && networkObject.IsSpawned && networkObject.IsPlayerObject)
                playerObjects.Add(networkObject);
        }

        EditorLog.Log(
            $"[CustomRoomReturn][Server] PlayerObject Despawn 시작. Count={playerObjects.Count}"
        );

        // Despawn이 SpawnedObjectsList를 변경하므로 복사본을 순회한다.
        for (int i = 0; i < playerObjects.Count; i++)
        {
            EditorLog.Log(
                $"[CustomRoomReturn][Server] PlayerObject Despawn. "
                    + $"NetworkObjectId={playerObjects[i].NetworkObjectId}, "
                    + $"OwnerClientId={playerObjects[i].OwnerClientId}, "
                    + $"Scene={playerObjects[i].gameObject.scene.name}"
            );
            playerObjects[i].Despawn(true);
        }

        for (int i = 0; i < networkManager.ConnectedClientsIds.Count; i++)
        {
            ulong clientId = networkManager.ConnectedClientsIds[i];
            if (
                networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                && client.PlayerObject != null
                && client.PlayerObject.IsSpawned
            )
            {
                throw new InvalidOperationException(
                    $"PlayerObject for client {clientId} remained spawned after match cleanup."
                );
            }
        }


        EditorLog.Log("[CustomRoomReturn][Server] PlayerObject Despawn 검증 완료.");
    }

    /// <summary>
    /// Lobby를 만들지 않고 1인 연습 세션을 시작한다. 기존 온라인 세션은 먼저 종료한다.
    /// 일반 플랫폼은 로컬 호스트를, WebGL은 Relay 호스트를 사용하며 맵은 NGO로 로딩한다.
    /// </summary>
    public async Task StartPracticeModeAsync(SO_MapDefinition map)
    {
        if (map == null)
            throw new ArgumentNullException(nameof(map));
        if (!map.AvailableInPractice)
            throw new InvalidOperationException(
                $"{map.DisplayName} 맵은 연습 모드에서 비활성화되어 있습니다."
            );
        if (!map.HasScene)
            throw new InvalidOperationException(
                $"{map.DisplayName} 맵에 Scene이 설정되지 않았습니다."
            );

        SetIntentionalDisconnect();
        StopLobbyMaintenance();
        MatchingState = MatchingState.None;
        _isStartingGame = false;

        await ReleaseCurrentLobbyAsync();
        await _relayConnection.ShutdownAsync();

        IsPracticeMode = true;
        IsCustomRoom = false;
        _configuredRoomSettings = CustomRoomSettings.Default;
        ApplyRoomSettings(_configuredRoomSettings);
        MaxPlayers = 1;
        TempJoinCode = string.Empty;
        GameModeId = map.Mode != null ? map.Mode.Id : string.Empty;
        GameModeDisplayName = map.Mode != null ? map.Mode.DisplayName : string.Empty;
        MapName = map.SceneName;
        MapDisplayName = map.DisplayName;

        try
        {
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                // Browsers cannot listen on a loopback socket. Keep practice networked through
                // Relay/WSS without publishing a Lobby, reserving one unused client slot.
                await _servicesSession.EnsureInitializedAsync();
                await _relayConnection.StartHostAsync(2);
            }
            else
            {
                await _relayConnection.StartLocalHostAsync();
            }

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsServer || !networkManager.IsListening)
                throw new InvalidOperationException("로컬 Netcode Host가 시작되지 않았습니다.");

            SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
                map.SceneName,
                LoadSceneMode.Single
            );
            if (status != SceneEventProgressStatus.Started)
            {
                throw new InvalidOperationException(
                    $"연습 맵 로딩을 시작하지 못했습니다. Scene: {map.SceneName}, Status: {status}"
                );
            }
        }
        catch
        {
            SetIntentionalDisconnect();
            ResetPracticeState();
            await _relayConnection.ShutdownAsync();
            throw;
        }
    }

    /// <summary>현재 인원으로 플레이할 수 있는 온라인 모드의 표시 이름을 반환한다.</summary>
    public string[] GetAvailableGameModeDisplayNames()
    {
        if (_mapCatalog == null)
            return Array.Empty<string>();

        _mapCatalog.CollectOnlineModes(MaxPlayers, _onlineModeBuffer);
        string[] displayNames = new string[_onlineModeBuffer.Count];
        for (int i = 0; i < _onlineModeBuffer.Count; i++)
            displayNames[i] = _onlineModeBuffer[i].DisplayName;

        return displayNames;
    }

    /// <summary>현재 선택된 모드와 인원에 맞는 온라인 맵의 표시 이름을 반환한다.</summary>
    public string[] GetAvailableMapDisplayNames()
    {
        if (_mapCatalog == null || string.IsNullOrWhiteSpace(GameModeId))
            return Array.Empty<string>();

        _mapCatalog.CollectOnlineMaps(GameModeId, MaxPlayers, _onlineMapBuffer);
        string[] displayNames = new string[_onlineMapBuffer.Count];
        for (int i = 0; i < _onlineMapBuffer.Count; i++)
            displayNames[i] = _onlineMapBuffer[i].DisplayName;

        return displayNames;
    }

    /// <summary>맵 룰렛에서 사용할 후보와 이미 준비된 썸네일을 반환한다.</summary>
    public SO_MapDefinition[] GetAvailableMaps()
    {
        if (_mapCatalog == null || string.IsNullOrWhiteSpace(GameModeId))
            return Array.Empty<SO_MapDefinition>();

        _mapCatalog.CollectOnlineMaps(GameModeId, MaxPlayers, _onlineMapBuffer);
        return _onlineMapBuffer.ToArray();
    }

    /// <summary>
    /// 호스트가 방 인원에 맞는 맵을 확정하고 모드 ID와 씬 이름을 동기화한다.
    /// 선택이 실제로 바뀌면 참가자의 준비 상태를 해제한다.
    /// </summary>
    public void SetCustomRoomMapSelection(SO_MapDefinition map)
    {
        if (!IsCustomRoom || !IsServer || !_lobbySession.IsLocalPlayerHost)
            throw new InvalidOperationException("Only the custom room host can select a map.");
        if (_isStartingGame)
            throw new InvalidOperationException("The map cannot change after the game starts.");
        if (map == null || map.Mode == null)
            throw new ArgumentException("A valid map is required.", nameof(map));
        if (_mapCatalog == null)
            throw new InvalidOperationException("The map catalog is not assigned.");

        _mapCatalog.CollectOnlineMaps(map.Mode, MaxPlayers, _onlineMapBuffer);
        if (!_onlineMapBuffer.Contains(map))
            throw new InvalidOperationException("The selected map is not available for this room.");

        bool changed = !string.Equals(GameModeId, map.Mode.Id, StringComparison.Ordinal)
            || !string.Equals(MapName, map.SceneName, StringComparison.Ordinal);

        GameModeId = map.Mode.Id;
        GameModeDisplayName = map.Mode.DisplayName;
        MapName = map.SceneName;
        MapDisplayName = map.DisplayName;

        _configuredMapSelection = new CustomRoomMapSelection(map.Mode.Id, map.SceneName);
        _customRoomMapSelection.Value = _configuredMapSelection;

        if (changed && CustomRoomReadyState.Instance != null)
            CustomRoomReadyState.Instance.ResetGuestReadyStates();
    }

    // 접속을 만들기 전에 해당 인원으로 시작 가능한 모드가 있는지 확인한다.
    private void ValidateOnlineCatalog(int playerCount)
    {
        if (_mapCatalog == null)
            throw new InvalidOperationException(
                "RelayManager에 맵 카탈로그가 설정되지 않았습니다."
            );

        _mapCatalog.CollectOnlineModes(playerCount, _onlineModeBuffer);
        if (_onlineModeBuffer.Count == 0)
        {
            throw new InvalidOperationException(
                $"{playerCount}명이 이용할 수 있는 온라인 게임 모드가 맵 카탈로그에 없습니다."
            );
        }
    }

    /// <summary>예정된 연결 종료임을 표시하고, 이전 세션에서 예약한 씬 로딩을 취소한다.</summary>
    public void SetIntentionalDisconnect()
    {
        _isIntentionalDisconnect = true;
        _matchFlow.CancelPendingSceneLoad();
    }

    /// <summary>매칭 진입과 겹치지 않도록 전환 잠금을 확보한 뒤 현재 방에서 나간다.</summary>
    public async Task LeaveLobby()
    {
        await _matchmakingTransition.WaitAsync();
        try
        {
            await LeaveLobbyCoreAsync();
        }
        finally
        {
            _matchmakingTransition.Release();
        }
    }

    // 방 이탈의 본체. 정리 중에는 기존 상태를 유지하고, finally에서 상태와 UI 이탈 알림을 마무리한다.
    private async Task LeaveLobbyCoreAsync()
    {
        bool wasCustomRoom = IsCustomRoom;
        SetIntentionalDisconnect();
        StopLobbyMaintenance();

        try
        {
            // Host의 Cancel은 자신만 제거하는 요청에 의존하지 않고 Lobby를 명시적으로 삭제한다.
            // 참가자는 기존처럼 자신의 Player만 제거한다.
            await ReleaseCurrentLobbyAsync();
            await _relayConnection.ShutdownAsync();
            EditorLog.Log("Lobby와 네트워크 세션을 종료했습니다.");
        }
        finally
        {
            // 새 매치메이킹 버튼은 Lobby 삭제와 NetworkManager 종료가 모두 끝난 뒤에만
            // None 상태를 관찰하므로 이전 방을 즉시 다시 조회할 수 없다.
            ResetPracticeState();
            MatchingState = MatchingState.None;

            if (wasCustomRoom)
                CustomRoomLeft?.Invoke();
        }
    }

    /// <summary>현재 Lobby를 삭제한다. 이미 없는 방은 로컬 기록을 비우고, 다른 서비스 오류는 로그로 남긴다.</summary>
    public async Task DeleteLobby()
    {
        if (!_lobbySession.HasLobby)
            return;

        try
        {
            await _lobbySession.DeleteAsync();
            EditorLog.Log("Lobby 삭제 완료");
        }
        catch (LobbyServiceException exception)
        {
            if (exception.Reason == LobbyExceptionReason.LobbyNotFound)
                _lobbySession.Clear();
            else
                EditorLog.LogError($"Lobby 삭제 실패: {exception}");
        }
    }

    /// <summary>
    /// 로비 유지 작업과 네트워크 세션을 종료한다.
    /// deleteLobby가 true이면 방 삭제를, false이면 자신의 참가 기록 제거를 요청한다.
    /// </summary>
    public async Task CloseSessionAsync(bool deleteLobby)
    {
        SetIntentionalDisconnect();
        StopLobbyMaintenance();
        MatchingState = MatchingState.None;

        if (deleteLobby)
            await DeleteLobby();
        else if (_lobbySession.HasLobby)
        {
            try
            {
                await _lobbySession.RemoveLocalPlayerAsync();
            }
            catch (LobbyServiceException exception)
            {
                if (exception.Reason == LobbyExceptionReason.LobbyNotFound)
                    _lobbySession.Clear();
                else
                    EditorLog.LogError($"Lobby 정리 실패: {exception}");
            }
        }

        try
        {
            await _relayConnection.ShutdownAsync();
        }
        finally
        {
            ResetPracticeState();
        }
    }

    // 호스트는 방을 삭제하고 참가자는 자신만 제거한다. 서비스 정리에 실패해도
    // 로컬 Lobby 기록을 비워 이후 작업이 오래된 방을 계속 사용하는 것을 막는다.
    private async Task ReleaseCurrentLobbyAsync()
    {
        if (!_lobbySession.HasLobby)
            return;

        try
        {
            if (_lobbySession.IsLocalPlayerHost)
                await _lobbySession.DeleteAsync();
            else
                await _lobbySession.RemoveLocalPlayerAsync();
        }
        catch (Exception exception)
        {
            _lobbySession.Clear();
            EditorLog.LogWarning($"기존 Lobby 정리 실패: {exception}");
        }
    }

    // 이름과 달리 연습 모드뿐 아니라 모든 세션의 로컬 선택·설정·플레이어 대응 정보를 초기화한다.
    // Lobby 탈퇴나 네트워크 종료는 하지 않으므로 필요한 종료 작업은 호출자가 따로 수행한다.
    private void ResetPracticeState()
    {
        IsPracticeMode = false;
        IsCustomRoom = false;
        TempJoinCode = string.Empty;
        GameModeId = null;
        GameModeDisplayName = null;
        MapName = null;
        MapDisplayName = null;
        _configuredMapSelection = CustomRoomMapSelection.None;
        _isStartingGame = false;
        _clientToPlayerId.Clear();
        _pendingPlayerIdReports.Clear();
        _playerIdReportVersion++;
        _configuredRoomSettings = CustomRoomSettings.Default;
        _customRoomPasswordHash = Array.Empty<byte>();
        ApplyRoomSettings(_configuredRoomSettings);
    }

    // 검색 결과는 이미 오래된 정보일 수 있으므로 최신 빈자리를 확인한 뒤 참가한다.
    // Lobby 참가와 Relay 접속은 별도 단계이며, 실패 시 정리는 매칭 본체가 담당한다.
    private async Task JoinExistingLobbyAsync(Lobby lobby)
    {
        Lobby updatedLobby = await _lobbySession.GetLobbyAsync(lobby.Id);
        if (updatedLobby.AvailableSlots <= 0)
        {
            EditorLog.LogWarning("방이 막 꽉 찼습니다. 다시 검색합니다...");
            // 이미 매치메이킹 전환 잠금을 보유한 내부 재검색이므로 공개 진입점을 다시 호출하지 않는다.
            await StartMatchmakingCoreAsync(MaxPlayers);
            return;
        }

        Lobby joinedLobby = await _lobbySession.JoinAsync(updatedLobby);
        MaxPlayers = joinedLobby.MaxPlayers;

        string relayCode = LobbySession.GetRelayCode(joinedLobby);
        if (string.IsNullOrWhiteSpace(relayCode))
            throw new InvalidOperationException("Lobby에 RelayCode가 없습니다.");

        EditorLog.Log($"Lobby 참가 성공! {joinedLobby.Players.Count}/{joinedLobby.MaxPlayers}");
        MatchingState = MatchingState.PendingPlayer;

        await _relayConnection.StartClientAsync(relayCode);
        StartPolling();
    }

    // 접속 가능한 Relay 코드를 먼저 확보하고 그 코드를 가진 Lobby를 만든다.
    // 호스트는 방 생존 신호(Heartbeat)와 목록 갱신(Polling)을 모두 시작한다.
    private async Task CreateLobbyWithRelayAsync()
    {
        TempJoinCode = await _relayConnection.StartHostAsync(MaxPlayers);
        Lobby lobby = await _lobbySession.CreateAsync(LobbyName, MaxPlayers, TempJoinCode);

        EditorLog.Log(
            $"Lobby 생성 완료! Lobby Code: {lobby.LobbyCode} / Relay Code: {TempJoinCode}"
        );
        MatchingState = MatchingState.PendingPlayer;

        StartHeartbeat();
        StartPolling();
    }

    // 인원이 부족한 일반 매칭 방의 서버가 Polling 중 호출한다.
    // true는 대기 작업을 다른 방 참가/복구 흐름에 넘겼다는 뜻이며, 기존 Polling은 종료한다.
    // 여기서의 migration은 대기 방 병합이다. 경기 중 끊긴 호스트를 승계하는 기능은 아니다.
    private async Task<bool> TryMergeLobbyAsync()
    {
        Lobby currentLobby = _lobbySession.CurrentLobby;
        if (
            _isMigrating
            || IsCustomRoom
            || !IsServer
            || currentLobby == null
            || currentLobby.Players.Count >= MaxPlayers
        )
            return false;

        int currentPlayerCount = currentLobby.Players.Count;

        try
        {
            IReadOnlyList<Lobby> candidates = await _lobbySession.FindMergeCandidatesAsync(
                MaxPlayers,
                currentPlayerCount
            );

            Lobby targetLobby = null;
            foreach (Lobby candidate in candidates)
            {
                if (candidate.Id != currentLobby.Id)
                {
                    targetLobby = candidate;
                    break;
                }
            }

            if (targetLobby == null)
                return false;

            // 두 방이 서로에게 동시에 이동하지 않도록 더 오래된 방을 남긴다.
            // 생성 시각이 같으면 ID 비교로 어느 방이 남을지 동일하게 결정한다.
            bool currentLobbyIsOlder =
                currentLobby.Created < targetLobby.Created
                || (
                    currentLobby.Created == targetLobby.Created
                    && string.Compare(currentLobby.Id, targetLobby.Id, StringComparison.Ordinal) < 0
                );

            if (currentLobbyIsOlder)
                return false;

            // 여러 방의 이동 요청이 동시에 몰리는 것을 줄이고, 이동 직전 빈자리를 재확인한다.
            await UnityRealtimeDelay.WaitAsync(UnityEngine.Random.Range(500, 1500));

            Lobby confirmedTarget;
            try
            {
                confirmedTarget = await _lobbySession.GetLobbyAsync(targetLobby.Id);
            }
            catch (LobbyServiceException exception)
            {
                if (exception.Reason == LobbyExceptionReason.LobbyNotFound)
                    return false;
                throw;
            }

            if (confirmedTarget.AvailableSlots < currentPlayerCount)
                return false;

            string targetRelayCode = LobbySession.GetRelayCode(confirmedTarget);
            if (string.IsNullOrWhiteSpace(targetRelayCode))
                return false;

            EditorLog.Log(
                $"대장 방({confirmedTarget.Name}) 발견! {currentPlayerCount}명이 다 함께 이사합니다."
            );

            _isMigrating = true;
            StopLobbyMaintenance();

            // 현재 호스트는 아래에서 직접 이동한다. 나머지 클라이언트에게는 RPC로 새 주소를 알린다.
            if (currentPlayerCount > 1)
                MigrateClientsClientRpc(targetRelayCode, confirmedTarget.Id);

            SetIntentionalDisconnect();
            await _relayConnection.ShutdownAsync();
            await DeleteLobby();

            await JoinMigratedLobbyAsync(targetRelayCode, confirmedTarget.Id);
            _isMigrating = false;
            return true;
        }
        catch (Exception exception)
        {
            if (!_isMigrating)
            {
                EditorLog.LogError($"Merge 검색 실패: {exception}");
                return false;
            }

            EditorLog.LogError($"Lobby 이전 실패. 매치메이킹으로 복구합니다: {exception}");
            await RecoverFromMigrationAsync();
            return true;
        }
    }

    // 방 이전 중 일시적인 접속 실패는 최대 MigrationAttemptCount회 시도한다.
    // Lobby 참가에 성공했다면 같은 Lobby에 중복 참가하지 않고 Relay 접속만 다시 시도한다.
    private async Task JoinMigratedLobbyAsync(string newRelayCode, string newLobbyId)
    {
        Lobby joinedLobby = null;
        Exception lastException = null;

        for (int attempt = 1; attempt <= MigrationAttemptCount; attempt++)
        {
            try
            {
                joinedLobby ??= await _lobbySession.JoinByIdAsync(newLobbyId);
                MaxPlayers = joinedLobby.MaxPlayers;
                MatchingState = MatchingState.PendingPlayer;

                await _relayConnection.StartClientAsync(newRelayCode);
                StartPolling();
                return;
            }
            // 방이 꽉 찼거나 잠겼거나 삭제된 경우는 동일 대상에 재시도해도 해결되지 않는다.
            catch (LobbyServiceException exception)
                when (exception.Reason == LobbyExceptionReason.LobbyFull
                    || exception.Reason == LobbyExceptionReason.LobbyLocked
                    || exception.Reason == LobbyExceptionReason.LobbyNotFound
                )
            {
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                await TryShutdownAfterMigrationFailureAsync();

                if (attempt >= MigrationAttemptCount)
                    break;

                EditorLog.LogWarning(
                    $"Lobby 이전 재시도 {attempt}/{MigrationAttemptCount} 실패. 다시 시도합니다: {exception.Message}"
                );
                await UnityRealtimeDelay.WaitAsync(MigrationRetryDelayMilliseconds * attempt);
            }
        }

        throw new InvalidOperationException(
            "Lobby migration failed after retrying.",
            lastException
        );
    }

    // 재시도 전에 실패한 네트워크 연결을 종료한다. 정리 오류는 기록하되 원래 복구 흐름을 이어간다.
    private async Task TryShutdownAfterMigrationFailureAsync()
    {
        try
        {
            await _relayConnection.ShutdownAsync();
        }
        catch (Exception exception)
        {
            EditorLog.LogWarning($"이전 재시도 중 NetworkManager 종료 실패: {exception}");
        }
    }

    // 이전 대상에 접속하지 못했거나 대기 중 전송이 실패하면 양쪽 세션을 정리하고 새 매칭으로 돌아간다.
    private async Task RecoverFromMigrationAsync()
    {
        SetIntentionalDisconnect();
        StopLobbyMaintenance();
        await TryShutdownAfterMigrationFailureAsync();

        if (_lobbySession.HasLobby)
        {
            try
            {
                if (_lobbySession.IsLocalPlayerHost)
                    await _lobbySession.DeleteAsync();
                else
                    await _lobbySession.RemoveLocalPlayerAsync();
            }
            catch (Exception exception)
            {
                EditorLog.LogWarning($"이전 실패 후 Lobby 정리 실패: {exception}");
                _lobbySession.Clear();
            }
        }

        _isMigrating = false;
        await StartMatchmaking(MaxPlayers);
    }

    // 이전 방 서버가 전송한다. 서버 자신은 직접 이동하므로 게스트 클라이언트만 이 RPC에서 이동한다.
    [ClientRpc]
    private void MigrateClientsClientRpc(string newRelayCode, string newLobbyId)
    {
        if (IsServer || _isMigrating)
            return;

        _isMigrating = true;
        EditorLog.Log($"방장이 새 방으로 이사합니다. 새 Relay 코드: {newRelayCode}");
        SetIntentionalDisconnect();
        StopLobbyMaintenance();
        _ = ExecuteMigrationAsync(newRelayCode, newLobbyId);
    }

    // RPC는 Task를 기다릴 수 없으므로 비동기 이동을 분리한다. 실패하면 새 매칭으로 복구한다.
    private async Task ExecuteMigrationAsync(string newRelayCode, string newLobbyId)
    {
        try
        {
            await _relayConnection.ShutdownAsync();
            await JoinMigratedLobbyAsync(newRelayCode, newLobbyId);
            _isMigrating = false;
        }
        catch (Exception exception)
        {
            EditorLog.LogError($"클라이언트 Lobby 이전 실패: {exception}");
            await RecoverFromMigrationAsync();
        }
    }

    // LobbyMaintenance가 호스트의 Lobby 생존 신호를 주기적으로 전송한다.
    private void StartHeartbeat()
    {
        _lobbyMaintenance.StartHeartbeat();
    }

    // Lobby를 주기적으로 갱신하고, 서버의 일반 매칭 방은 인원이 부족하면 병합을 검사한다.
    private void StartPolling()
    {
        _lobbyMaintenance.StartPolling(() => IsServer, () => MaxPlayers, TryMergeLobbyAsync);
    }

    // 종료·이동 후 이전 Lobby에 대한 주기 작업이 계속 요청을 보내지 않게 한다.
    private void StopLobbyMaintenance()
    {
        _lobbyMaintenance?.Stop();
    }

    // 이 NetworkBehaviour가 네트워크에 스폰되면 설정 동기화와 접속 콜백을 연결한다.
    // 서버는 준비 값을 배포하고, 게스트는 자신의 인증 ID를 서버에 보고한다.
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isIntentionalDisconnect = false;
        _roomSettings.OnValueChanged += OnRoomSettingsChanged;
        _customRoomMapSelection.OnValueChanged += OnCustomRoomMapSelectionChanged;

        if (IsServer)
        {
            _roomSettings.Value = _configuredRoomSettings;
            _customRoomMapSelection.Value = _configuredMapSelection;
        }

        ApplyRoomSettings(_roomSettings.Value);
        ApplyCustomRoomMapSelection(_customRoomMapSelection.Value);
        RegisterNetworkCallbacks();

        if (IsServer && !IsPracticeMode)
        {
            _clientToPlayerId[NetworkManager.Singleton.LocalClientId] = _servicesSession.PlayerId;
        }
        else if (!IsServer && !IsPracticeMode)
        {
            ReportPlayerIdRpc(_servicesSession.PlayerId);
        }
    }

    // 네트워크에서 제거될 때 구독을 해제하고 방 전용 중력 설정을 기본값으로 돌린다.
    public override void OnNetworkDespawn()
    {
        _roomSettings.OnValueChanged -= OnRoomSettingsChanged;
        _customRoomMapSelection.OnValueChanged -= OnCustomRoomMapSelectionChanged;
        ApplyRoomSettings(CustomRoomSettings.Default);
        UnregisterNetworkCallbacks();
        base.OnNetworkDespawn();
    }

    // 연결 종료·전송 실패는 각 피어가 처리한다. 접속 승인과 인원 충족 판단은 서버만 처리한다.
    private void RegisterNetworkCallbacks()
    {
        if (_networkCallbacksRegistered || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.ConnectionApprovalCallback += ApprovalCheck;
        }

        _networkCallbacksRegistered = true;
    }

    // 재접속 시 중복 호출되거나 파괴된 객체가 콜백을 받지 않도록 구독을 제거한다.
    private void UnregisterNetworkCallbacks()
    {
        if (!_networkCallbacksRegistered || NetworkManager.Singleton == null)
            return;

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.ConnectionApprovalCallback -= ApprovalCheck;
        _networkCallbacksRegistered = false;
    }

    // NGO 이벤트 콜백. 매칭 검색/플레이어 대기 중의 예상치 못한 전송 실패만 새 매칭으로 복구한다.
    // 의도한 종료와 이미 진행 중인 복구는 무시하며, 경기 중 오류는 이 경로에서 처리하지 않는다.
    private async void OnTransportFailure()
    {
        if (
            _isHandlingTransportFailure
            || _isIntentionalDisconnect
            || (
                MatchingState != MatchingState.FindingMatch
                && MatchingState != MatchingState.PendingPlayer
            )
        )
            return;

        _isHandlingTransportFailure = true;
        EditorLog.LogError("Relay 전송 실패를 감지했습니다. 새 할당으로 매치메이킹을 복구합니다.");

        try
        {
            await RecoverFromMigrationAsync();
        }
        catch (Exception exception)
        {
            MatchingState = MatchingState.None;
            EditorLog.LogError($"Relay 전송 실패 복구 중 예외가 발생했습니다: {exception}");
        }
        finally
        {
            _isHandlingTransportFailure = false;
        }
    }

    // 서버가 NGO 접속을 허용할지 결정한다. 사용자 정의 방의 비밀번호와 실제 접속 인원을 확인한다.
    // Lobby 목록상의 빈자리 확인과는 별개로, 네트워크 접속 시점에 다시 검사한다.
    private void ApprovalCheck(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response
    )
    {
        int currentPlayers = NetworkManager.Singleton.ConnectedClients.Count;
        response.Pending = false;

        if (
            request.ClientNetworkId != NetworkManager.ServerClientId
            && IsCustomRoom
            && _customRoomPasswordHash.Length > 0
            && !PasswordMatches(request.Payload)
        )
        {
            response.Approved = false;
            response.Reason = IncorrectRoomPasswordReason;
            return;
        }

        if (currentPlayers >= MaxPlayers)
        {
            response.Approved = false;
            response.Reason = "방이 꽉 찼습니다.";
            return;
        }

        response.Approved = true;
    }

    // 클라이언트가 자신의 Lobby ID를 서버에 보고한다. 접속 ID는 호출 인자가 아닌 RPC 송신자에서 얻는다.
    // 보고한 문자열은 바로 신뢰하지 않고 아래 비동기 메서드에서 최신 Lobby 명단과 대조한다.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportPlayerIdRpc(string authPlayerId, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!_pendingPlayerIdReports.Add(clientId))
            return;

        int reportVersion = _playerIdReportVersion;
        _ = ValidateAndRegisterPlayerIdAsync(clientId, authPlayerId, reportVersion);
    }

    // Lobby를 갱신한 뒤 현재 세션·접속 상태와 ID 중복 여부를 검사하여 대응 관계를 등록한다.
    // Lobby 명단과의 대조이며, 이 메서드가 인증 토큰 자체를 검증하는 것은 아니다.
    private async Task ValidateAndRegisterPlayerIdAsync(
        ulong clientId,
        string authPlayerId,
        int reportVersion
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(authPlayerId) || !_lobbySession.HasLobby)
                return;

            // 호스트가 가진 Lobby 사본은 Relay 접속보다 늦게 갱신될 수 있으므로
            // 현재 명단을 다시 받은 뒤 클라이언트가 주장한 ID를 검증한다.
            await _lobbySession.RefreshAsync();

            NetworkManager networkManager = NetworkManager.Singleton;
            if (
                !IsServer
                || reportVersion != _playerIdReportVersion
                || networkManager == null
                || !networkManager.ConnectedClients.ContainsKey(clientId)
                || _clientToPlayerId.ContainsKey(clientId)
                || !_lobbySession.ContainsPlayer(authPlayerId)
                || string.Equals(
                    authPlayerId,
                    _lobbySession.HostPlayerId,
                    StringComparison.Ordinal
                )
                || _clientToPlayerId.ContainsValue(authPlayerId)
            )
            {
                EditorLog.LogWarning($"클라이언트 {clientId}번의 Lobby ID 등록을 거부했습니다.");
                return;
            }

            _clientToPlayerId.Add(clientId, authPlayerId);
            EditorLog.Log($"클라이언트 {clientId}번의 Lobby ID를 검증하여 등록했습니다.");
        }
        catch (Exception exception)
        {
            EditorLog.LogWarning(
                $"클라이언트 {clientId}번의 Lobby ID를 검증하지 못했습니다: {exception.Message}"
            );
        }
        finally
        {
            if (reportVersion == _playerIdReportVersion)
                _pendingPlayerIdReports.Remove(clientId);
        }
    }

    // 일반 매칭은 서버의 실제 NGO 접속 인원이 목표에 도달하면 자동 시작한다.
    // 연습 모드와 사용자 정의 방은 각각 별도 시작 경로를 사용한다.
    private void OnClientConnected(ulong clientId)
    {
        if (
            IsServer
            && !IsPracticeMode
            && !IsCustomRoom
            && !_isStartingGame
            && NetworkManager.Singleton.ConnectedClients.Count >= MaxPlayers
        )
        {
            _ = StartGameAsync();
        }
    }

    // 서버의 공통 시작 흐름: Lobby 잠금 → 진행 상태 배포 → 모드별 시작 시퀀스.
    // 일반 매칭은 모드·맵을 추첨하고, 사용자 정의 방은 미리 선택된 맵을 사용한다.
    private async Task StartGameAsync()
    {
        if (_isStartingGame || !_lobbySession.HasLobby)
            return;

        // await 사이에 세션 상태가 달라져도 시작 당시 종류에 맞는 실패 복구를 선택한다.
        bool isCustomRoomStart = IsCustomRoom;
        _isStartingGame = true;

        try
        {
            await _lobbySession.LockAsync();
            UpdateMatchingStateClientRpc(MatchingState.GameStart);

            if (IsCustomRoom)
            {
                await StartCustomRoomGameSequenceAsync();
                return;
            }

            await UnityRealtimeDelay.WaitAsync(Mathf.Max(0, GameStartTerm) * 1000);

            if (_mapCatalog == null)
                throw new InvalidOperationException(
                    "RelayManager에 맵 카탈로그가 설정되지 않았습니다."
                );

            SO_MapDefinition selectedMap = null;
            SO_GameModeDefinition selectedMode = SelectOnlineMode();
            if (selectedMode == null)
            {
                throw new InvalidOperationException("온라인에서 사용할 게임 모드가 없습니다.");
            }

            StartModeSelectionClientRpc(selectedMode.Id, selectedMode.DisplayName);
            await UnityRealtimeDelay.WaitAsync(
                TimeSpan.FromSeconds(
                    Mathf.Max(0f, ModeSelectionDuration)
                        + Mathf.Max(0f, ModeResultDisplayDuration)
                )
            );

            selectedMap = SelectOnlineMap(selectedMode);
            if (selectedMap == null)
            {
                throw new InvalidOperationException(
                    $"{selectedMode.DisplayName} 모드에서 사용할 수 있는 맵이 없습니다."
                );
            }

            StartMapSelectionClientRpc(selectedMap.SceneName, selectedMap.DisplayName);
            await _matchFlow.LoadNetworkSceneAsync(
                selectedMap.SceneName,
                MapSelectionDuration + MapLoadDelay,
                LoadSceneMode.Single
            );
        }
        catch (Exception exception)
        {
            _isStartingGame = false;
            await RecoverFromGameStartFailureAsync(isCustomRoomStart);
            EditorLog.LogError($"게임 시작 시퀀스 실패: {exception}");

            if (isCustomRoomStart)
                throw;
        }
    }

    // 일반 매칭 시작 실패는 세션을 닫고 메인 메뉴로 돌아간다.
    // 사용자 정의 방은 부분 로딩 씬을 정리하고 대기실·로비 잠금을 복원하여 다시 시작할 수 있게 한다.
    private async Task RecoverFromGameStartFailureAsync(bool wasCustomRoomStart)
    {
        if (!wasCustomRoomStart)
        {
            UpdateMatchingStateClientRpc(MatchingState.None);
            await CloseSessionAsync(true);
            SceneManager.LoadScene(_mainMenuSceneName);
            return;
        }

        if (wasCustomRoomStart)
        {
            Scene gameScene = SceneManager.GetSceneByName(MapName);
            if (gameScene.IsValid() && gameScene.isLoaded)
            {
                try
                {
                    await UnloadNetworkSceneAsync(gameScene);
                }
                catch (Exception exception)
                {
                    EditorLog.LogWarning($"Failed to unload the partially loaded game scene: {exception}");
                }
            }

            ShowCustomRoomSceneClientRpc();
        }

        if (_lobbySession.HasLobby && _lobbySession.IsLocalPlayerHost)
        {
            try
            {
                await _lobbySession.UnlockAsync();
            }
            catch (Exception exception)
            {
                EditorLog.LogWarning($"Failed to unlock the lobby after game start failure: {exception}");
            }
        }

        UpdateMatchingStateClientRpc(MatchingState.PendingPlayer);
    }

    // 현재 인원으로 가능한 모드 중 가중치 추첨을 한다. 가중치가 클수록 선택될 확률이 높다.
    // 전체 가중치 범위에서 뽑은 값을 각 후보의 가중치만큼 빼며 해당 구간의 후보를 찾는다.
    private SO_GameModeDefinition SelectOnlineMode()
    {
        if (_mapCatalog == null)
            return null;

        _mapCatalog.CollectOnlineModes(MaxPlayers, _onlineModeBuffer);
        if (_onlineModeBuffer.Count == 0)
            return null;

        int totalWeight = 0;
        for (int i = 0; i < _onlineModeBuffer.Count; i++)
            totalWeight += _onlineModeBuffer[i].OnlineSelectionWeight;

        int selection = UnityEngine.Random.Range(0, totalWeight);
        for (int i = 0; i < _onlineModeBuffer.Count; i++)
        {
            selection -= _onlineModeBuffer[i].OnlineSelectionWeight;
            if (selection < 0)
                return _onlineModeBuffer[i];
        }

        return _onlineModeBuffer[_onlineModeBuffer.Count - 1];
    }

    // 선택된 모드와 현재 인원에 맞는 맵 중에서 모드 추첨과 같은 가중치 방식으로 고른다.
    private SO_MapDefinition SelectOnlineMap(SO_GameModeDefinition mode)
    {
        _mapCatalog.CollectOnlineMaps(mode, MaxPlayers, _onlineMapBuffer);
        if (_onlineMapBuffer.Count == 0)
            return null;

        int totalWeight = 0;
        for (int i = 0; i < _onlineMapBuffer.Count; i++)
            totalWeight += _onlineMapBuffer[i].OnlineSelectionWeight;

        int selection = UnityEngine.Random.Range(0, totalWeight);
        for (int i = 0; i < _onlineMapBuffer.Count; i++)
        {
            selection -= _onlineMapBuffer[i].OnlineSelectionWeight;
            if (selection < 0)
                return _onlineMapBuffer[i];
        }

        return _onlineMapBuffer[_onlineMapBuffer.Count - 1];
    }

    // 동기화된 모드 ID·씬 이름을 로컬 카탈로그의 실제 맵 정의로 다시 찾는다.
    private SO_MapDefinition GetSelectedCustomRoomMap()
    {
        if (
            _mapCatalog == null
            || string.IsNullOrWhiteSpace(GameModeId)
            || string.IsNullOrWhiteSpace(MapName)
        )
            return null;

        _mapCatalog.CollectOnlineMaps(GameModeId, MaxPlayers, _onlineMapBuffer);
        for (int i = 0; i < _onlineMapBuffer.Count; i++)
        {
            SO_MapDefinition map = _onlineMapBuffer[i];
            if (string.Equals(map.SceneName, MapName, StringComparison.Ordinal))
                return map;
        }

        return null;
    }

    // 서버가 정한 진행 단계를 각 클라이언트의 로컬 속성과 UI 이벤트에 반영한다.
    [ClientRpc]
    private void UpdateMatchingStateClientRpc(MatchingState state)
    {
        MatchingState = state;
    }

    // 서버의 추첨 결과를 공유한다. 클라이언트가 다시 추첨하지 않으며 이전 맵 선택은 비운다.
    [ClientRpc]
    private void StartModeSelectionClientRpc(string modeId, string modeDisplayName)
    {
        GameModeId = modeId;
        GameModeDisplayName = modeDisplayName;
        MapName = null;
        MapDisplayName = null;
        MatchingState = MatchingState.SelectMode;
    }

    // 실제 로딩에 사용할 씬 이름과 UI 표시 이름을 배포하고 맵 선택 연출 단계로 전환한다.
    [ClientRpc]
    private void StartMapSelectionClientRpc(string mapName, string mapDisplayName)
    {
        MapName = mapName;
        MapDisplayName = mapDisplayName;
        MatchingState = MatchingState.SelectMap;
    }

    // 카운트다운 시간 계산은 서버가 하고, 각 클라이언트는 전달된 남은 시간을 표시한다.
    [ClientRpc]
    private void ShowCustomRoomStartCountdownClientRpc(int remainingSeconds)
    {
        string unit = remainingSeconds == 1 ? "second" : "seconds";
        MessageOnUI.ShowMessage($"The game starts in {remainingSeconds} {unit}.");
    }

    // 대기실 씬을 삭제하지 않고 활성 루트만 숨겨 경기 후 같은 대기실을 복원할 수 있게 한다.
    // 각 피어의 씬 객체는 로컬 객체이므로 모두 자신의 루트를 따로 기록하고 숨긴다.
    [ClientRpc]
    private void HideCustomRoomSceneClientRpc()
    {
        Scene customRoomScene = SceneManager.GetSceneByName(_customRoomSceneName);
        if (!customRoomScene.IsValid() || !customRoomScene.isLoaded)
            return;

        _hiddenCustomRoomRoots.Clear();
        GameObject[] roots = customRoomScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null || !root.activeSelf)
                continue;

            _hiddenCustomRoomRoots.Add(root);
            root.SetActive(false);
        }
    }

    // 서버가 카운트다운 후 경기 씬을 Additive로 로딩한다.
    // Additive는 기존 대기실 씬을 유지한 채 경기 씬을 추가하는 방식이며, 로딩 콜백에서 대기실을 숨긴다.
    private async Task StartCustomRoomGameSequenceAsync()
    {
        SO_MapDefinition selectedMap = GetSelectedCustomRoomMap();
        if (selectedMap?.Mode == null)
            throw new InvalidOperationException("Select a mode and map before starting.");

        int countdownSeconds = Mathf.Max(0, GameStartTerm);
        for (int remainingSeconds = countdownSeconds; remainingSeconds > 0; remainingSeconds--)
        {
            ShowCustomRoomStartCountdownClientRpc(remainingSeconds);
            await UnityRealtimeDelay.WaitAsync(1000);
        }

        await _matchFlow.LoadNetworkSceneAsync(
            selectedMap.SceneName,
            0f,
            LoadSceneMode.Additive,
            HideCustomRoomSceneClientRpc
        );
    }

    // 각 피어에서 복귀 로딩 UI를 표시하고 활성 씬을 대기실로 옮긴다.
    // 경기 씬은 이후 서버의 네트워크 언로드 요청으로 제거되며, 여기서는 아직 대기실 루트를 켜지 않는다.
    [ClientRpc]
    private void PrepareCustomRoomReturnClientRpc()
    {
        ulong localClientId = NetworkManager != null
            ? NetworkManager.LocalClientId
            : Unity.Netcode.NetworkManager.ServerClientId;
        EditorLog.Log(
            $"[CustomRoomReturn][Peer {localClientId}] 복귀 준비 RPC 수신. "
                + $"ActiveScene={SceneManager.GetActiveScene().name}"
        );

        _customRoomReturnLoading?.Dispose();
        _customRoomReturnLoading = NetworkLoadingPanel.Begin("Returning to room");

        Scene customRoomScene = SceneManager.GetSceneByName(_customRoomSceneName);
        if (customRoomScene.IsValid() && customRoomScene.isLoaded)
            SceneManager.SetActiveScene(customRoomScene);

        EditorLog.Log(
            $"[CustomRoomReturn][Peer {localClientId}] Custom Room 활성 씬 전환 시도 완료. "
                + $"Scene={customRoomScene.name}, Valid={customRoomScene.IsValid()}, "
                + $"Loaded={customRoomScene.isLoaded}, ActiveScene={SceneManager.GetActiveScene().name}"
        );

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 숨겨 두었던 대기실 루트를 복원하고 로딩 UI·커서·배경음을 대기실 상태로 돌린다.
    [ClientRpc]
    private void ShowCustomRoomSceneClientRpc()
    {
        ulong localClientId = NetworkManager != null
            ? NetworkManager.LocalClientId
            : Unity.Netcode.NetworkManager.ServerClientId;
        EditorLog.Log(
            $"[CustomRoomReturn][Peer {localClientId}] 대기실 표시 RPC 수신. "
                + $"HiddenRootCount={_hiddenCustomRoomRoots.Count}, "
                + $"ActiveScene={SceneManager.GetActiveScene().name}"
        );

        int restoredRootCount = 0;
        for (int i = 0; i < _hiddenCustomRoomRoots.Count; i++)
        {
            if (_hiddenCustomRoomRoots[i] != null)
            {
                _hiddenCustomRoomRoots[i].SetActive(true);
                restoredRootCount++;
            }
        }

        _hiddenCustomRoomRoots.Clear();
        _customRoomReturnLoading?.Dispose();
        _customRoomReturnLoading = null;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        SceneToBGM.ApplyForScene(_customRoomSceneName);

        EditorLog.Log(
            $"[CustomRoomReturn][Peer {localClientId}] 대기실 표시 완료. "
                + $"RestoredRootCount={restoredRootCount}, "
                + $"ActiveScene={SceneManager.GetActiveScene().name}"
        );
    }

    // 서버에서 NGO 씬 언로드를 시작하고 이벤트를 Task로 연결하여 완료를 기다린다.
    // 서버 로컬 언로드 완료와 전체 피어 완료는 서로 다른 시점이므로 두 단계로 나누어 기다린다.
    // onServerSceneUnloaded는 서버 씬 제거 직후 호출하며, 남은 클라이언트 검증은 그 뒤에 계속한다.
    private async Task UnloadNetworkSceneAsync(
        Scene scene,
        Action onServerSceneUnloaded = null
    )
    {
        NetworkManager networkManager = NetworkManager;
        NetworkSceneManager networkSceneManager = networkManager.SceneManager;
        // 씬이 제거되면 Scene 구조체에서 이름을 안전하게 읽을 수 없으므로 미리 식별 정보를 저장한다.
        string targetSceneName = scene.name;
        var targetSceneHandle = scene.handle;
        // 완료 신호 세 종류: NGO의 전체 결과, 서버 자신의 완료, 개별 완료를 모은 전원 완료.
        // TaskCompletionSource는 콜백이 도착했을 때 기다리던 Task를 완료시키는 연결 장치다.
        TaskCompletionSource<List<ulong>> completion = new();
        TaskCompletionSource<bool> serverUnloadCompletion = new();
        TaskCompletionSource<bool> allClientUnloadsCompleted = new();
        HashSet<ulong> pendingClientIds = new(networkManager.ConnectedClientsIds);

        EditorLog.Log(
            $"[CustomRoomReturn][Server] NGO 씬 언로드 준비. Scene={targetSceneName}, "
                + $"Handle={targetSceneHandle}, PendingClients=[{string.Join(", ", pendingClientIds)}]"
        );

        // 피어 하나의 언로드 완료 통지. 다른 씬의 이벤트는 무시하고 대기 명단에서 해당 피어를 뺀다.
        void HandleUnloadComplete(ulong clientId, string sceneName)
        {
            bool sceneStructValid = scene.IsValid();
            string sceneStructName = sceneStructValid ? scene.name : "<invalid>";

            EditorLog.Log(
                $"[CustomRoomReturn][Server] OnUnloadComplete 수신. Client={clientId}, "
                    + $"EventScene={sceneName}, TargetScene={targetSceneName}, "
                    + $"SceneStructName={sceneStructName}, SceneStructValid={sceneStructValid}"
            );

            if (!string.Equals(sceneName, targetSceneName, StringComparison.Ordinal))
                return;

            pendingClientIds.Remove(clientId);
            EditorLog.Log(
                $"[CustomRoomReturn][Server] 피어 언로드 완료 반영. Client={clientId}, "
                    + $"PendingClients=[{string.Join(", ", pendingClientIds)}]"
            );
            if (clientId == Unity.Netcode.NetworkManager.ServerClientId)
                serverUnloadCompletion.TrySetResult(true);
            if (pendingClientIds.Count == 0)
                allClientUnloadsCompleted.TrySetResult(true);
        }

        // NGO가 전체 언로드 작업을 집계한 결과. 시간 초과한 클라이언트 목록을 보관한다.
        void HandleUnloadCompleted(
            string sceneName,
            LoadSceneMode loadSceneMode,
            List<ulong> clientsCompleted,
            List<ulong> clientsTimedOut
        )
        {
            EditorLog.Log(
                $"[CustomRoomReturn][Server] OnUnloadEventCompleted 수신. "
                    + $"EventScene={sceneName}, TargetScene={targetSceneName}, "
                    + $"Completed=[{string.Join(", ", clientsCompleted ?? new List<ulong>())}], "
                    + $"TimedOut=[{string.Join(", ", clientsTimedOut ?? new List<ulong>())}]"
            );

            if (string.Equals(sceneName, targetSceneName, StringComparison.Ordinal))
            {
                completion.TrySetResult(
                    clientsTimedOut == null
                        ? new List<ulong>()
                        : new List<ulong>(clientsTimedOut)
                );
            }
        }

        // 시작 직후 이벤트가 와도 놓치지 않도록 언로드 요청 전에 구독한다.
        networkSceneManager.OnUnloadComplete += HandleUnloadComplete;
        networkSceneManager.OnUnloadEventCompleted += HandleUnloadCompleted;
        try
        {
            SceneEventProgressStatus status = networkSceneManager.UnloadScene(scene);
            EditorLog.Log(
                $"[CustomRoomReturn][Server] NetworkSceneManager.UnloadScene 호출 결과. "
                    + $"Scene={targetSceneName}, Status={status}"
            );
            if (status != SceneEventProgressStatus.Started)
            {
                throw new InvalidOperationException(
                    $"Failed to unload the custom room game scene. Status: {status}"
                );
            }

            // NGO가 사용하는 씬 이벤트 제한시간보다 먼저 포기하면 언로드 진행 중에 대기실이 노출될 수 있다.
            int timeoutSeconds = Mathf.Max(1, networkManager.NetworkConfig.LoadSceneTimeOut) + 5;
            Task timeoutTask = UnityRealtimeDelay.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds));
            Task serverCompletedTask = await Task.WhenAny(
                serverUnloadCompletion.Task,
                timeoutTask
            );
            if (serverCompletedTask != serverUnloadCompletion.Task)
                throw new TimeoutException("The server timed out while unloading the game scene.");

            await serverUnloadCompletion.Task;

            bool serverSceneStructValid = scene.IsValid();
            bool serverSceneStructLoaded = serverSceneStructValid && scene.isLoaded;

            EditorLog.Log(
                $"[CustomRoomReturn][Server] 서버 로컬 씬 언로드 완료. "
                    + $"Scene={targetSceneName}, SceneStructValid={serverSceneStructValid}, "
                    + $"SceneStructLoaded={serverSceneStructLoaded}"
            );

            // 서버의 경기 씬이 실제로 사라진 시점부터 대기실을 노출한다.
            // 전체 피어 완료 이벤트는 이후의 실패 클라이언트 검증에만 사용한다.
            EditorLog.Log("[CustomRoomReturn][Server] 대기실 표시 RPC 전송 시작.");
            onServerSceneUnloaded?.Invoke();
            EditorLog.Log("[CustomRoomReturn][Server] 대기실 표시 RPC 전송 완료.");

            // 전체 집계 이벤트, 개별 완료로 확인한 전원 완료, 제한시간 중 먼저 도착한 신호를 사용한다.
            // 제한시간은 서버 완료 대기에서 만든 같은 Task를 사용하므로 작업 전체에 적용된다.
            Task completedTask = await Task.WhenAny(
                completion.Task,
                allClientUnloadsCompleted.Task,
                timeoutTask
            );
            List<ulong> clientsTimedOut = completedTask == completion.Task
                ? await completion.Task
                : completedTask == allClientUnloadsCompleted.Task
                    ? new List<ulong>()
                    : new List<ulong>(pendingClientIds);
            string completionSource = completedTask == completion.Task
                ? "OnUnloadEventCompleted"
                : completedTask == allClientUnloadsCompleted.Task
                    ? "AllOnUnloadComplete"
                    : "Timeout";
            EditorLog.Log(
                $"[CustomRoomReturn][Server] 전체 피어 언로드 대기 종료. "
                    + $"Source={completionSource}, TimedOut=[{string.Join(", ", clientsTimedOut)}], "
                    + $"PendingClients=[{string.Join(", ", pendingClientIds)}]"
            );
            for (int i = 0; i < clientsTimedOut.Count; i++)
            {
                ulong clientId = clientsTimedOut[i];
                if (clientId == Unity.Netcode.NetworkManager.ServerClientId)
                {
                    throw new TimeoutException(
                        "The server timed out while unloading the custom room game scene."
                    );
                }

                if (!networkManager.ConnectedClients.ContainsKey(clientId))
                    continue;

                // 경기 씬을 유지한 클라이언트를 대기실에 남기면 피어별 활성 씬이 달라지므로 연결을 종료한다.
                networkManager.DisconnectClient(
                    clientId,
                    "Timed out while returning to the custom room."
                );
                EditorLog.LogWarning(
                    $"클라이언트 {clientId}가 경기 씬 언로드에 실패하여 연결을 종료했습니다."
                );
            }

            if (scene.IsValid() && scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "The custom room game scene remained loaded after the unload event completed."
                );
            }

            EditorLog.Log(
                $"[CustomRoomReturn][Server] 복귀용 씬 언로드 처리 완료. "
                    + $"Scene={targetSceneName}, Handle={targetSceneHandle}"
            );
        }
        finally
        {
            // 성공·시간 초과·예외 모두에서 해제하여 다음 언로드 이벤트를 이전 작업이 받지 않게 한다.
            if (networkSceneManager != null)
            {
                networkSceneManager.OnUnloadComplete -= HandleUnloadComplete;
                networkSceneManager.OnUnloadEventCompleted -= HandleUnloadCompleted;
            }

            EditorLog.Log(
                $"[CustomRoomReturn][Server] 씬 언로드 이벤트 구독 해제. Scene={targetSceneName}"
            );
        }
    }

    // 연결 종료 이벤트는 서버와 게스트에서 역할이 다르다.
    // 서버는 떠난 참가자의 Lobby 기록을 정리하고, 게스트는 자신의 비정상 종료에 대해 화면을 복구한다.
    private async void OnClientDisconnected(ulong clientId)
    {
        if (IsServer && _lobbySession.HasLobby)
        {
            if (_clientToPlayerId.TryGetValue(clientId, out string authPlayerId))
            {
                try
                {
                    await _lobbySession.RemovePlayerAsync(authPlayerId);
                    _clientToPlayerId.Remove(clientId);
                    EditorLog.Log("Lobby 서버에서 연결 종료 플레이어를 정리했습니다.");
                }
                catch (LobbyServiceException exception)
                {
                    EditorLog.LogError($"Lobby 플레이어 정리 실패: {exception}");
                }
            }

            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || clientId != networkManager.LocalClientId)
            return;

        if (_isIntentionalDisconnect)
        {
            _isIntentionalDisconnect = false;
            EditorLog.Log("정상적인 연결 종료입니다.");
            return;
        }

        // 세션 초기화 전에 대기실에 있었는지 경기 중이었는지 저장하여 이탈 화면 처리를 구분한다.
        bool wasCustomRoom = IsCustomRoom;
        Scene customRoomScene = SceneManager.GetSceneByName(_customRoomSceneName);
        bool wasCustomRoomMatch =
            wasCustomRoom
            && customRoomScene.IsValid()
            && customRoomScene.isLoaded
            && (
                _hiddenCustomRoomRoots.Count > 0
                || SceneManager.GetActiveScene().handle != customRoomScene.handle
            );
        EditorLog.LogWarning(
            wasCustomRoom
                ? "The custom room host disconnected. Leaving the room."
                : "서버 연결이 비정상적으로 종료되어 메인 화면으로 돌아갑니다."
        );
        MessageOnUI.ShowMessage("The host has left the room.", MessageType.Warning);

        if (_lobbySession.HasLobby)
        {
            try
            {
                await _lobbySession.RemoveLocalPlayerAsync();
            }
            catch
            {
                _lobbySession.Clear();
            }
        }

        StopLobbyMaintenance();
        _matchFlow.CancelPendingSceneLoad();
        _relayConnection.Shutdown();

        if (wasCustomRoom)
        {
            ResetPracticeState();

            if (wasCustomRoomMatch)
            {
                _customRoomReturnLoading?.Dispose();
                _customRoomReturnLoading = null;
                _hiddenCustomRoomRoots.Clear();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SceneManager.LoadScene(_mainMenuSceneName);
            }
            else
            {
                CustomRoomLeft?.Invoke();
            }

            return;
        }

        ResetPracticeState();
        UnityEngine.SceneManagement.SceneManager.LoadScene(_mainMenuSceneName);
    }

    // NetworkVariable의 변경 통지를 로컬 물리 설정과 UI 이벤트에 반영한다.
    private void OnRoomSettingsChanged(
        CustomRoomSettings previousSettings,
        CustomRoomSettings newSettings
    )
    {
        _configuredRoomSettings = newSettings.Validated();
        ApplyRoomSettings(_configuredRoomSettings);
        RoomSettingsChanged?.Invoke(_configuredRoomSettings);
    }

    // 서버에서 전달된 선택 값을 로컬 선택 정보와 UI에 반영하는 진입점이다.
    private void OnCustomRoomMapSelectionChanged(
        CustomRoomMapSelection previousSelection,
        CustomRoomMapSelection newSelection
    )
    {
        ApplyCustomRoomMapSelection(newSelection);
    }

    // 네트워크에는 모드 ID와 씬 이름만 전달된다. 표시 이름은 각 피어의 카탈로그에서 찾아 채운다.
    // 선택이 없거나 카탈로그에서 찾지 못하면 표시 이름은 비운 상태로 변경 이벤트를 보낸다.
    private void ApplyCustomRoomMapSelection(CustomRoomMapSelection selection)
    {
        _configuredMapSelection = selection;
        GameModeId = selection.HasSelection ? selection.ModeId : null;
        GameModeDisplayName = null;
        MapName = selection.HasSelection ? selection.MapSceneName : null;
        MapDisplayName = null;

        if (selection.HasSelection && _mapCatalog != null)
        {
            IReadOnlyList<SO_MapDefinition> maps = _mapCatalog.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                SO_MapDefinition map = maps[i];
                if (
                    map == null
                    || map.Mode == null
                    || !string.Equals(map.Mode.Id, selection.ModeId, StringComparison.Ordinal)
                    || !string.Equals(
                        map.SceneName,
                        selection.MapSceneName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                GameModeDisplayName = map.Mode.DisplayName;
                MapDisplayName = map.DisplayName;
                break;
            }
        }

        CustomRoomMapSelectionChanged?.Invoke(selection);
    }

    // 이전에 적용한 중력에 다시 곱하지 않고, 항상 초기 중력을 기준으로 배율을 적용한다.
    private void ApplyRoomSettings(CustomRoomSettings settings)
    {
        Physics.gravity = _defaultGravity * settings.Validated().GravityMultiplier;
    }

    // 접속 요청의 UTF-8 비밀번호를 해시한 뒤 호스트의 저장 해시와 비교한다.
    // 같은 길이에서는 첫 불일치에 반환하지 않고 모든 바이트 차이를 누적한다.
    private bool PasswordMatches(byte[] payload)
    {
        byte[] receivedHash = HashPassword(
            payload == null ? string.Empty : Encoding.UTF8.GetString(payload)
        );
        if (receivedHash.Length != _customRoomPasswordHash.Length)
            return false;

        int difference = 0;
        for (int i = 0; i < receivedHash.Length; i++)
            difference |= receivedHash[i] ^ _customRoomPasswordHash[i];

        return difference == 0;
    }

    // 빈 비밀번호는 비밀번호 없는 방을 뜻한다. 나머지는 비교용 SHA-256 해시로 변환한다.
    private static byte[] HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return Array.Empty<byte>();

        using SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    }

    // 앱 종료 시 호스트는 방 삭제를, 게스트는 탈퇴를 시도한다.
    // Unity 종료 콜백의 비동기 작업이 프로세스 종료 전에 끝난다는 보장은 없으므로 최선의 정리 시도다.
    private async void OnApplicationQuit()
    {
        SetIntentionalDisconnect();
        StopLobbyMaintenance();

        if (IsServer)
            await DeleteLobby();
        else if (_lobbySession.HasLobby)
        {
            try
            {
                await _lobbySession.RemoveLocalPlayerAsync();
            }
            catch
            {
                _lobbySession.Clear();
            }
        }

        _relayConnection.Shutdown();
    }

    // 객체가 파괴될 때 로딩 UI와 이벤트·예약 작업을 정리한다. Lobby API 정리는 다른 종료 경로가 담당한다.
    public override void OnDestroy()
    {
        _customRoomReturnLoading?.Dispose();
        _customRoomReturnLoading = null;
        StopLobbyMaintenance();
        _matchFlow?.CancelPendingSceneLoad();
        UnregisterNetworkCallbacks();

        if (Instance == this)
            Instance = null;

        base.OnDestroy();
    }
}
// RelayManager은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
