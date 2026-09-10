using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum MatchingState
{
    None,
    FindingMatch,
    PendingPlayer,
    GameStart,
    SelectMode,
    SelectMap,
}

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

    [SerializeField, Range(1, 65535), InspectorName("연습 모드 포트")]
    private int _practicePort = 7777;

    [Header("로비 설정")]
    [InspectorName("로비 이름")]
    public string LobbyName = "My Game Room";

    public string TempJoinCode { get; private set; } = string.Empty;
    public string GameModeId { get; private set; }
    public string GameModeDisplayName { get; private set; }
    public string MapName { get; private set; }
    public string MapDisplayName { get; private set; }
    public bool IsPracticeMode { get; private set; }
    public bool IsCustomRoom { get; private set; }
    public string CurrentRoomId => _lobbySession?.CurrentLobby?.Id ?? string.Empty;
    public CustomRoomSettings CurrentRoomSettings =>
        IsSpawned ? _roomSettings.Value : _configuredRoomSettings;
    public CustomRoomMapSelection CurrentCustomRoomMapSelection =>
        IsSpawned ? _customRoomMapSelection.Value : _configuredMapSelection;
    public event Action<CustomRoomSettings> RoomSettingsChanged;
    public event Action<CustomRoomMapSelection> CustomRoomMapSelectionChanged;
    public event Action CustomRoomLeft;

    private readonly Dictionary<ulong, string> _clientToPlayerId = new();
    private readonly HashSet<ulong> _pendingPlayerIdReports = new();
    private int _playerIdReportVersion;
    private readonly List<SO_GameModeDefinition> _onlineModeBuffer = new();
    private readonly List<SO_MapDefinition> _onlineMapBuffer = new();
    private readonly List<GameObject> _hiddenCustomRoomRoots = new();
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

    private CustomRoomSettings _configuredRoomSettings = CustomRoomSettings.Default;
    private CustomRoomMapSelection _configuredMapSelection = CustomRoomMapSelection.None;
    private byte[] _customRoomPasswordHash = Array.Empty<byte>();
    private Vector3 _defaultGravity;

    private UnityServicesSession _servicesSession;
    private LobbySession _lobbySession;
    private LobbyMaintenance _lobbyMaintenance;
    private RelayConnection _relayConnection;
    private MatchFlowCoordinator _matchFlow;

    private bool _isIntentionalDisconnect;
    private bool _networkCallbacksRegistered;
    private bool _isStartingGame;
    private bool _isMigrating;
    private bool _isHandlingTransportFailure;
    private IDisposable _customRoomReturnLoading;

    private MatchingState _matchingState;
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

    public async Task StartMatchmaking(int targetPlayerCount)
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
            MatchingState = MatchingState.None;
            ResetPracticeState();
            EditorLog.LogError($"매치메이킹 실패: {exception}");
            _relayConnection.Shutdown();
        }
    }

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

    public async Task<IReadOnlyList<CustomRoomSummary>> GetPublicCustomRoomsAsync()
    {
        await _servicesSession.EnsureInitializedAsync();
        IReadOnlyList<Lobby> lobbies = await _lobbySession.FindPublicCustomRoomsAsync();
        List<CustomRoomSummary> rooms = new(lobbies.Count);

        for (int i = 0; i < lobbies.Count; i++)
            rooms.Add(CreateCustomRoomSummary(lobbies[i]));

        return rooms;
    }

    public async Task JoinCustomRoomByIdAsync(string roomId, string password)
    {
        if (string.IsNullOrWhiteSpace(roomId))
            throw new ArgumentException("Room ID is required.", nameof(roomId));
        if ((password ?? string.Empty).Length > 64)
            throw new ArgumentException("Password must be 64 characters or fewer.", nameof(password));

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
                await _relayConnection.StartLocalHostAsync((ushort)_practicePort);
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

    public void SetIntentionalDisconnect()
    {
        _isIntentionalDisconnect = true;
        _matchFlow.CancelPendingSceneLoad();
    }

    public async Task LeaveLobby()
    {
        bool wasCustomRoom = IsCustomRoom;
        SetIntentionalDisconnect();
        StopLobbyMaintenance();
        MatchingState = MatchingState.None;

        if (_lobbySession.HasLobby)
        {
            try
            {
                await _lobbySession.RemoveLocalPlayerAsync();
                EditorLog.Log("Lobby에서 나갔습니다.");
            }
            catch (LobbyServiceException exception)
            {
                if (exception.Reason == LobbyExceptionReason.LobbyNotFound)
                {
                    _lobbySession.Clear();
                    EditorLog.Log("방이 이미 삭제되어 로컬 Lobby 상태만 정리합니다.");
                }
                else
                {
                    EditorLog.LogError($"Lobby 나가기 실패: {exception}");
                }
            }
        }

        _relayConnection.Shutdown();
        ResetPracticeState();

        if (wasCustomRoom)
            CustomRoomLeft?.Invoke();
    }

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

    private async Task JoinExistingLobbyAsync(Lobby lobby)
    {
        Lobby updatedLobby = await _lobbySession.GetLobbyAsync(lobby.Id);
        if (updatedLobby.AvailableSlots <= 0)
        {
            EditorLog.LogWarning("방이 막 꽉 찼습니다. 다시 검색합니다...");
            await StartMatchmaking(MaxPlayers);
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

            bool currentLobbyIsOlder =
                currentLobby.Created < targetLobby.Created
                || (
                    currentLobby.Created == targetLobby.Created
                    && string.Compare(currentLobby.Id, targetLobby.Id, StringComparison.Ordinal) < 0
                );

            if (currentLobbyIsOlder)
                return false;

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

    private void StartHeartbeat()
    {
        _lobbyMaintenance.StartHeartbeat();
    }

    private void StartPolling()
    {
        _lobbyMaintenance.StartPolling(() => IsServer, () => MaxPlayers, TryMergeLobbyAsync);
    }

    private void StopLobbyMaintenance()
    {
        _lobbyMaintenance?.Stop();
    }

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

    public override void OnNetworkDespawn()
    {
        _roomSettings.OnValueChanged -= OnRoomSettingsChanged;
        _customRoomMapSelection.OnValueChanged -= OnCustomRoomMapSelectionChanged;
        ApplyRoomSettings(CustomRoomSettings.Default);
        UnregisterNetworkCallbacks();
        base.OnNetworkDespawn();
    }

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

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ReportPlayerIdRpc(string authPlayerId, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        if (!_pendingPlayerIdReports.Add(clientId))
            return;

        int reportVersion = _playerIdReportVersion;
        _ = ValidateAndRegisterPlayerIdAsync(clientId, authPlayerId, reportVersion);
    }

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

    private async Task StartGameAsync()
    {
        if (_isStartingGame || !_lobbySession.HasLobby)
            return;

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

    [ClientRpc]
    private void UpdateMatchingStateClientRpc(MatchingState state)
    {
        MatchingState = state;
    }

    [ClientRpc]
    private void StartModeSelectionClientRpc(string modeId, string modeDisplayName)
    {
        GameModeId = modeId;
        GameModeDisplayName = modeDisplayName;
        MapName = null;
        MapDisplayName = null;
        MatchingState = MatchingState.SelectMode;
    }

    [ClientRpc]
    private void StartMapSelectionClientRpc(string mapName, string mapDisplayName)
    {
        MapName = mapName;
        MapDisplayName = mapDisplayName;
        MatchingState = MatchingState.SelectMap;
    }

    [ClientRpc]
    private void ShowCustomRoomStartCountdownClientRpc(int remainingSeconds)
    {
        string unit = remainingSeconds == 1 ? "second" : "seconds";
        MessageOnUI.ShowMessage($"The game starts in {remainingSeconds} {unit}.");
    }

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

    private async Task UnloadNetworkSceneAsync(
        Scene scene,
        Action onServerSceneUnloaded = null
    )
    {
        NetworkManager networkManager = NetworkManager;
        NetworkSceneManager networkSceneManager = networkManager.SceneManager;
        string targetSceneName = scene.name;
        var targetSceneHandle = scene.handle;
        TaskCompletionSource<List<ulong>> completion = new();
        TaskCompletionSource<bool> serverUnloadCompletion = new();
        TaskCompletionSource<bool> allClientUnloadsCompleted = new();
        HashSet<ulong> pendingClientIds = new(networkManager.ConnectedClientsIds);

        EditorLog.Log(
            $"[CustomRoomReturn][Server] NGO 씬 언로드 준비. Scene={targetSceneName}, "
                + $"Handle={targetSceneHandle}, PendingClients=[{string.Join(", ", pendingClientIds)}]"
        );

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

    private void OnRoomSettingsChanged(
        CustomRoomSettings previousSettings,
        CustomRoomSettings newSettings
    )
    {
        _configuredRoomSettings = newSettings.Validated();
        ApplyRoomSettings(_configuredRoomSettings);
        RoomSettingsChanged?.Invoke(_configuredRoomSettings);
    }

    private void OnCustomRoomMapSelectionChanged(
        CustomRoomMapSelection previousSelection,
        CustomRoomMapSelection newSelection
    )
    {
        ApplyCustomRoomMapSelection(newSelection);
    }

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

    private void ApplyRoomSettings(CustomRoomSettings settings)
    {
        Physics.gravity = _defaultGravity * settings.Validated().GravityMultiplier;
    }

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

    private static byte[] HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return Array.Empty<byte>();

        using SHA256 sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    }

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
