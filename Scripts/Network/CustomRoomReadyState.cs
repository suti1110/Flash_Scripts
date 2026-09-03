using System;
using Unity.Netcode;
using UnityEngine;

public sealed class CustomRoomReadyState : NetworkBehaviour
{
    public static CustomRoomReadyState Instance { get; private set; }

    private readonly NetworkList<CustomRoomReadyEntry> _players = new();

    public int PlayerCount => _players.Count;
    public bool AreAllPlayersReady
    {
        get
        {
            for (int i = 0; i < _players.Count; i++)
            {
                if (!_players[i].IsHost && !_players[i].IsReady)
                    return false;
            }

            return _players.Count > 0;
        }
    }

    public event Action ReadyStatesChanged;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        _players.OnListChanged += HandleListChanged;

        if (IsServer && RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom)
        {
            _players.Clear();
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                AddPlayer(clientId);

            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        ReadyStatesChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _players.OnListChanged -= HandleListChanged;

        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        if (Instance == this)
            Instance = null;

        base.OnNetworkDespawn();
    }

    public CustomRoomReadyEntry GetPlayer(int index)
    {
        return _players[index];
    }

    public bool TryGetLocalPlayer(out CustomRoomReadyEntry player)
    {
        ulong localClientId =
            NetworkManager != null ? NetworkManager.LocalClientId : ulong.MaxValue;
        return TryGetPlayer(localClientId, out player);
    }

    public void SetLocalReady(bool isReady)
    {
        if (
            !IsSpawned
            || !IsClient
            || RelayManager.Instance == null
            || !RelayManager.Instance.IsCustomRoom
        )
            return;

        SetReadyRpc(isReady);
    }

    public void ResetGuestReadyStates()
    {
        if (!IsServer)
            return;

        for (int i = 0; i < _players.Count; i++)
        {
            CustomRoomReadyEntry player = _players[i];
            if (!player.IsHost && player.IsReady)
                _players[i] = new CustomRoomReadyEntry(player.ClientId, false, false);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetReadyRpc(bool isReady, RpcParams rpcParams = default)
    {
        if (RelayManager.Instance == null || !RelayManager.Instance.IsCustomRoom)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (senderClientId == NetworkManager.ServerClientId)
            return;

        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].ClientId != senderClientId)
                continue;

            _players[i] = new CustomRoomReadyEntry(senderClientId, isReady, false);
            return;
        }
    }

    private bool TryGetPlayer(ulong clientId, out CustomRoomReadyEntry player)
    {
        for (int i = 0; i < _players.Count; i++)
        {
            if (_players[i].ClientId != clientId)
                continue;

            player = _players[i];
            return true;
        }

        player = default;
        return false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (RelayManager.Instance != null && RelayManager.Instance.IsCustomRoom)
            AddPlayer(clientId);
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        for (int i = _players.Count - 1; i >= 0; i--)
        {
            if (_players[i].ClientId == clientId)
                _players.RemoveAt(i);
        }
    }

    private void AddPlayer(ulong clientId)
    {
        if (TryGetPlayer(clientId, out _))
            return;

        bool isHost = clientId == NetworkManager.ServerClientId;
        _players.Add(new CustomRoomReadyEntry(clientId, isHost, isHost));
    }

    private void HandleListChanged(NetworkListEvent<CustomRoomReadyEntry> _)
    {
        ReadyStatesChanged?.Invoke();
    }
}

public struct CustomRoomReadyEntry : INetworkSerializable, IEquatable<CustomRoomReadyEntry>
{
    public ulong ClientId { get; private set; }
    public bool IsReady { get; private set; }
    public bool IsHost { get; private set; }

    public CustomRoomReadyEntry(ulong clientId, bool isReady, bool isHost)
    {
        ClientId = clientId;
        IsReady = isReady;
        IsHost = isHost;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        ulong clientId = ClientId;
        bool isReady = IsReady;
        bool isHost = IsHost;

        serializer.SerializeValue(ref clientId);
        serializer.SerializeValue(ref isReady);
        serializer.SerializeValue(ref isHost);

        if (serializer.IsReader)
        {
            ClientId = clientId;
            IsReady = isReady;
            IsHost = isHost;
        }
    }

    public bool Equals(CustomRoomReadyEntry other)
    {
        return ClientId == other.ClientId && IsReady == other.IsReady && IsHost == other.IsHost;
    }
}
// CustomRoomReadyState은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
