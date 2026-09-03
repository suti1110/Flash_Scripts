using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public sealed class CustomRoomChat : NetworkBehaviour
{
    public const int MaximumMessageCharacters = 120;
    public const int MaximumMessageBytes = 360;
    public const int MaximumHistoryCount = 50;

    private const double MinimumSendIntervalSeconds = 0.5d;

    public static CustomRoomChat Instance { get; private set; }

    private readonly List<CustomRoomChatMessage> _serverHistory = new();
    private readonly List<CustomRoomChatMessage> _localHistory = new();
    private readonly Dictionary<ulong, double> _lastSendTimes = new();

    public IReadOnlyList<CustomRoomChatMessage> Messages => _localHistory;

    public event Action<CustomRoomChatMessage> MessageReceived;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        _localHistory.Clear();

        if (
            IsClient
            && !IsServer
            && RelayManager.Instance != null
            && RelayManager.Instance.IsCustomRoom
        )
            RequestHistoryRpc();
    }

    public override void OnNetworkDespawn()
    {
        _serverHistory.Clear();
        _localHistory.Clear();
        _lastSendTimes.Clear();

        if (Instance == this)
            Instance = null;
    }

    public void SendChatMessage(string message)
    {
        if (
            !IsSpawned
            || !IsClient
            || RelayManager.Instance == null
            || !RelayManager.Instance.IsCustomRoom
        )
            return;

        SendMessageRpc(message ?? string.Empty);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SendMessageRpc(string message, RpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (RelayManager.Instance == null || !RelayManager.Instance.IsCustomRoom)
            return;

        double now = Time.realtimeSinceStartupAsDouble;

        if (
            _lastSendTimes.TryGetValue(senderClientId, out double lastSendTime)
            && now - lastSendTime < MinimumSendIntervalSeconds
        )
            return;

        string sanitizedMessage = Sanitize(message);
        if (
            string.IsNullOrWhiteSpace(sanitizedMessage)
            || sanitizedMessage.Length > MaximumMessageCharacters
            || Encoding.UTF8.GetByteCount(sanitizedMessage) > MaximumMessageBytes
        )
            return;

        _lastSendTimes[senderClientId] = now;

        CustomRoomChatMessage chatMessage = new(
            $"Player {senderClientId + 1}",
            sanitizedMessage,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        );
        AddToHistory(_serverHistory, chatMessage);
        ReceiveMessageClientRpc(
            chatMessage.SenderName,
            chatMessage.Content,
            chatMessage.UnixTimeSeconds
        );
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestHistoryRpc(RpcParams rpcParams = default)
    {
        ulong[] targetClientIds = { rpcParams.Receive.SenderClientId };
        ClientRpcParams clientRpcParams = new()
        {
            Send = new ClientRpcSendParams { TargetClientIds = targetClientIds },
        };

        for (int i = 0; i < _serverHistory.Count; i++)
        {
            CustomRoomChatMessage message = _serverHistory[i];
            ReceiveMessageClientRpc(
                message.SenderName,
                message.Content,
                message.UnixTimeSeconds,
                clientRpcParams
            );
        }
    }

    [ClientRpc]
    private void ReceiveMessageClientRpc(
        string senderName,
        string content,
        long unixTimeSeconds,
        ClientRpcParams clientRpcParams = default
    )
    {
        CustomRoomChatMessage message = new(senderName, content, unixTimeSeconds);
        AddToHistory(_localHistory, message);
        MessageReceived?.Invoke(message);
    }

    private static void AddToHistory(
        List<CustomRoomChatMessage> history,
        CustomRoomChatMessage message
    )
    {
        history.Add(message);
        if (history.Count > MaximumHistoryCount)
            history.RemoveAt(0);
    }

    private static string Sanitize(string message)
    {
        string trimmed = message.Trim();
        StringBuilder builder = new(trimmed.Length);

        for (int i = 0; i < trimmed.Length; i++)
        {
            char character = trimmed[i];
            if (!char.IsControl(character))
                builder.Append(character);
        }

        return builder.ToString();
    }
}

public sealed class CustomRoomChatMessage
{
    public string SenderName { get; }
    public string Content { get; }
    public long UnixTimeSeconds { get; }

    public CustomRoomChatMessage(string senderName, string content, long unixTimeSeconds)
    {
        SenderName = senderName;
        Content = content;
        UnixTimeSeconds = unixTimeSeconds;
    }
}
// CustomRoomChat은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
