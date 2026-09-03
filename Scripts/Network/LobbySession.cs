using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

public sealed class LobbySession
{
    private const string RelayCodeKey = "RelayCode";
    private const string VersionKey = "Version";
    private const string PlayerCountKey = "GameMode";
    private const string RoomTypeKey = "RoomType";
    private const string HasPasswordKey = "HasPassword";
    private const string CustomSettingsKey = "CustomSettings";
    private const string MatchmakingRoomType = "Matchmaking";
    private const string CustomRoomType = "Custom";

    public Lobby CurrentLobby { get; private set; }

    public bool HasLobby => CurrentLobby != null;
    public int PlayerCount => CurrentLobby?.Players.Count ?? 0;
    public string HostPlayerId => CurrentLobby?.HostId;
    public bool IsLocalPlayerHost =>
        CurrentLobby != null && CurrentLobby.HostId == AuthenticationService.Instance.PlayerId;

    public async Task<Lobby> FindAvailableAsync(int maxPlayers)
    {
        QueryLobbiesOptions options = CreateQueryOptions(maxPlayers, "1", 1);
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
        return response.Results.Count > 0 ? response.Results[0] : null;
    }

    public async Task<IReadOnlyList<Lobby>> FindMergeCandidatesAsync(
        int maxPlayers,
        int requiredSlots
    )
    {
        QueryLobbiesOptions options = CreateQueryOptions(maxPlayers, requiredSlots.ToString(), 5);
        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
        return response.Results;
    }

    public Task<Lobby> GetLobbyAsync(string lobbyId)
    {
        return LobbyService.Instance.GetLobbyAsync(lobbyId);
    }

    public async Task<Lobby> JoinAsync(Lobby lobby)
    {
        CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id);
        return CurrentLobby;
    }

    public async Task<Lobby> JoinByIdAsync(string lobbyId)
    {
        CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId);
        return CurrentLobby;
    }

    public async Task<Lobby> CreateAsync(string lobbyName, int maxPlayers, string relayCode)
    {
        CreateLobbyOptions options = new()
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                { RelayCodeKey, new DataObject(DataObject.VisibilityOptions.Public, relayCode) },
                {
                    VersionKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        Application.version,
                        DataObject.IndexOptions.S1
                    )
                },
                {
                    PlayerCountKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        maxPlayers.ToString(),
                        DataObject.IndexOptions.N1
                    )
                },
                {
                    RoomTypeKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        MatchmakingRoomType,
                        DataObject.IndexOptions.S2
                    )
                },
            },
        };

        CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        return CurrentLobby;
    }

    public async Task<Lobby> CreateCustomAsync(
        string lobbyName,
        int maxPlayers,
        string relayCode,
        bool isPublic,
        bool hasPassword,
        CustomRoomSettings settings
    )
    {
        CreateLobbyOptions options = new()
        {
            IsPrivate = !isPublic,
            Data = new Dictionary<string, DataObject>
            {
                { RelayCodeKey, new DataObject(DataObject.VisibilityOptions.Public, relayCode) },
                {
                    VersionKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        Application.version,
                        DataObject.IndexOptions.S1
                    )
                },
                {
                    RoomTypeKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        CustomRoomType,
                        DataObject.IndexOptions.S2
                    )
                },
                {
                    HasPasswordKey,
                    new DataObject(DataObject.VisibilityOptions.Public, hasPassword ? "1" : "0")
                },
                {
                    CustomSettingsKey,
                    new DataObject(
                        DataObject.VisibilityOptions.Public,
                        JsonUtility.ToJson(settings.Validated())
                    )
                },
            },
        };

        CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        return CurrentLobby;
    }

    public async Task<IReadOnlyList<Lobby>> FindPublicCustomRoomsAsync(int count = 100)
    {
        QueryLobbiesOptions options = new()
        {
            Count = Mathf.Clamp(count, 1, 100),
            Filters = new List<QueryFilter>
            {
                new(QueryFilter.FieldOptions.AvailableSlots, "1", QueryFilter.OpOptions.GE),
                new(QueryFilter.FieldOptions.S1, Application.version, QueryFilter.OpOptions.EQ),
                new(QueryFilter.FieldOptions.S2, CustomRoomType, QueryFilter.OpOptions.EQ),
            },
            Order = new List<QueryOrder> { new(false, QueryOrder.FieldOptions.Created) },
        };

        QueryResponse response = await LobbyService.Instance.QueryLobbiesAsync(options);
        List<Lobby> availableRooms = new(response.Results.Count);
        for (int i = 0; i < response.Results.Count; i++)
        {
            Lobby lobby = response.Results[i];
            if (lobby != null && !lobby.IsLocked)
                availableRooms.Add(lobby);
        }

        return availableRooms;
    }

    public async Task UpdateCustomSettingsAsync(CustomRoomSettings settings)
    {
        Lobby lobbyToUpdate = CurrentLobby;
        if (lobbyToUpdate == null)
            return;

        Lobby updatedLobby = await LobbyService.Instance.UpdateLobbyAsync(
            lobbyToUpdate.Id,
            new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    {
                        CustomSettingsKey,
                        new DataObject(
                            DataObject.VisibilityOptions.Public,
                            JsonUtility.ToJson(settings.Validated())
                        )
                    },
                },
            }
        );

        if (CurrentLobby != null && CurrentLobby.Id == lobbyToUpdate.Id)
            CurrentLobby = updatedLobby;
    }

    public async Task RefreshAsync()
    {
        Lobby lobbyToRefresh = CurrentLobby;
        if (lobbyToRefresh == null)
            return;

        Lobby refreshedLobby = await LobbyService.Instance.GetLobbyAsync(lobbyToRefresh.Id);
        if (CurrentLobby != null && CurrentLobby.Id == lobbyToRefresh.Id)
            CurrentLobby = refreshedLobby;
    }

    public async Task LockAsync()
    {
        await SetLockedAsync(true);
    }

    public async Task UnlockAsync()
    {
        await SetLockedAsync(false);
    }

    public Task SendHeartbeatAsync()
    {
        return CurrentLobby == null
            ? Task.CompletedTask
            : LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
    }

    public async Task RemoveLocalPlayerAsync()
    {
        Lobby lobbyToLeave = CurrentLobby;
        if (lobbyToLeave == null)
            return;

        await LobbyService.Instance.RemovePlayerAsync(
            lobbyToLeave.Id,
            AuthenticationService.Instance.PlayerId
        );

        if (CurrentLobby != null && CurrentLobby.Id == lobbyToLeave.Id)
            CurrentLobby = null;
    }

    public Task RemovePlayerAsync(string playerId)
    {
        return CurrentLobby == null
            ? Task.CompletedTask
            : LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, playerId);
    }

    public async Task DeleteAsync()
    {
        Lobby lobbyToDelete = CurrentLobby;
        if (lobbyToDelete == null)
            return;

        await LobbyService.Instance.DeleteLobbyAsync(lobbyToDelete.Id);

        if (CurrentLobby != null && CurrentLobby.Id == lobbyToDelete.Id)
            CurrentLobby = null;
    }

    public void Clear()
    {
        CurrentLobby = null;
    }

    public static string GetRelayCode(Lobby lobby)
    {
        return lobby != null && lobby.Data.TryGetValue(RelayCodeKey, out DataObject relayCode)
            ? relayCode.Value
            : null;
    }

    private async Task SetLockedAsync(bool isLocked)
    {
        Lobby lobbyToUpdate = CurrentLobby;
        if (lobbyToUpdate == null)
            return;

        Lobby updatedLobby = await LobbyService.Instance.UpdateLobbyAsync(
            lobbyToUpdate.Id,
            new UpdateLobbyOptions { IsLocked = isLocked }
        );

        if (CurrentLobby != null && CurrentLobby.Id == lobbyToUpdate.Id)
            CurrentLobby = updatedLobby;
    }

    public static bool IsCustomRoom(Lobby lobby)
    {
        return lobby != null
            && lobby.Data.TryGetValue(RoomTypeKey, out DataObject roomType)
            && string.Equals(roomType.Value, CustomRoomType, System.StringComparison.Ordinal);
    }

    public static bool HasPassword(Lobby lobby)
    {
        return lobby != null
            && lobby.Data.TryGetValue(HasPasswordKey, out DataObject hasPassword)
            && hasPassword.Value == "1";
    }

    public static bool IsCompatibleVersion(Lobby lobby)
    {
        return lobby != null
            && lobby.Data.TryGetValue(VersionKey, out DataObject version)
            && string.Equals(version.Value, Application.version, System.StringComparison.Ordinal);
    }

    public static CustomRoomSettings GetCustomRoomSettings(Lobby lobby)
    {
        if (
            lobby == null
            || !lobby.Data.TryGetValue(CustomSettingsKey, out DataObject settingsData)
            || string.IsNullOrWhiteSpace(settingsData.Value)
        )
            return CustomRoomSettings.Default;

        CustomRoomSettings settings = JsonUtility
            .FromJson<CustomRoomSettings>(settingsData.Value)
            .Validated();

        if (
            settingsData.Value.IndexOf(
                "\"_throwPower\"",
                System.StringComparison.Ordinal
            ) >= 0
        )
        {
            return settings;
        }

        // 새 필드가 없던 기존 로비는 역직렬화 시 0이 되므로 최소값이 아니라 기획 기본값으로 마이그레이션한다.
        return new CustomRoomSettings(
            settings.GravityMultiplier,
            settings.MoveSpeedMultiplier,
            settings.JumpForceMultiplier,
            settings.AttackDamage,
            settings.MaximumHealth,
            settings.KnockbackMultiplier,
            CustomRoomSettings.DefaultThrowPower
        );
    }

    public bool ContainsPlayer(string playerId)
    {
        if (CurrentLobby?.Players == null || string.IsNullOrWhiteSpace(playerId))
            return false;

        for (int i = 0; i < CurrentLobby.Players.Count; i++)
        {
            if (
                string.Equals(
                    CurrentLobby.Players[i].Id,
                    playerId,
                    System.StringComparison.Ordinal
                )
            )
                return true;
        }

        return false;
    }

    private static QueryLobbiesOptions CreateQueryOptions(
        int maxPlayers,
        string minimumAvailableSlots,
        int count
    )
    {
        return new QueryLobbiesOptions
        {
            Count = count,
            Filters = new List<QueryFilter>
            {
                new(
                    QueryFilter.FieldOptions.AvailableSlots,
                    minimumAvailableSlots,
                    QueryFilter.OpOptions.GE
                ),
                new(QueryFilter.FieldOptions.S1, Application.version, QueryFilter.OpOptions.EQ),
                new(QueryFilter.FieldOptions.N1, maxPlayers.ToString(), QueryFilter.OpOptions.EQ),
                new(QueryFilter.FieldOptions.S2, MatchmakingRoomType, QueryFilter.OpOptions.EQ),
            },
            Order = new List<QueryOrder> { new(true, QueryOrder.FieldOptions.Created) },
        };
    }
}
// LobbySession은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
