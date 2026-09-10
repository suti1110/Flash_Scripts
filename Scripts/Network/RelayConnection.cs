using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;

public sealed class NetworkConnectionRejectedException : InvalidOperationException
{
    public string RejectionReason { get; }

    public NetworkConnectionRejectedException(string rejectionReason)
        : base(
            string.IsNullOrWhiteSpace(rejectionReason)
                ? "The Netcode host rejected or closed the connection."
                : $"The Netcode connection failed: {rejectionReason}"
        )
    {
        RejectionReason = rejectionReason ?? string.Empty;
    }
}

public sealed class RelayConnection
{
    private const string RelayConnectionType = "wss";
    private const int ClientConnectionTimeoutMilliseconds = 10000;
    private const int ShutdownTimeoutMilliseconds = 5000;

    public async Task<string> StartHostAsync(int maxPlayers)
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers - 1);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        UnityTransport transport = GetRelayTransport();
        transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));

        NetworkManager networkManager = GetNetworkManager();
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = Array.Empty<byte>();

        if (!networkManager.StartHost())
            throw new InvalidOperationException("Failed to start the Netcode host.");

        return joinCode;
    }

    public async Task StartClientAsync(string joinCode, string approvalPayload = "")
    {
        NetworkManager networkManager = GetNetworkManager();
        if (IsRunningOrShuttingDown(networkManager))
        {
            throw new InvalidOperationException(
                "The Netcode client cannot start before the previous session has fully stopped."
            );
        }

        JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        UnityTransport transport = GetRelayTransport();
        transport.SetRelayServerData(allocation.ToRelayServerData(RelayConnectionType));
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(
            approvalPayload ?? string.Empty
        );

        TaskCompletionSource<bool> connectionResult = new();
        string disconnectReason = string.Empty;

        void OnClientConnected(ulong clientId)
        {
            if (clientId == networkManager.LocalClientId)
                connectionResult.TrySetResult(true);
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (clientId != networkManager.LocalClientId)
                return;

            disconnectReason = networkManager.DisconnectReason;
            connectionResult.TrySetResult(false);
        }

        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;

        try
        {
            if (!networkManager.StartClient())
                throw new InvalidOperationException("Failed to start the Netcode client.");

            Task completedTask = await Task.WhenAny(
                connectionResult.Task,
                UnityRealtimeDelay.WaitAsync(ClientConnectionTimeoutMilliseconds)
            );

            if (completedTask != connectionResult.Task)
                throw new TimeoutException("Timed out while connecting to the Netcode host.");

            if (!await connectionResult.Task)
                throw new NetworkConnectionRejectedException(disconnectReason);
        }
        catch
        {
            await ShutdownAsync();
            throw;
        }
        finally
        {
            networkManager.OnClientConnectedCallback -= OnClientConnected;
            networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    public async Task StartLocalHostAsync(ushort port)
    {
        await ShutdownAsync();

        UnityTransport transport = GetTransport();
        transport.UseWebSockets = false;
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");

        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = false;
        NetworkManager.Singleton.NetworkConfig.ConnectionData = Array.Empty<byte>();

        if (!NetworkManager.Singleton.StartHost())
            throw new InvalidOperationException("Failed to start the local Netcode host.");
    }

    public void Shutdown()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();
    }

    public async Task ShutdownAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !IsRunningOrShuttingDown(networkManager))
            return;

        if (!networkManager.ShutdownInProgress)
            networkManager.Shutdown();

        int waitedMilliseconds = 0;
        while (networkManager != null && IsRunningOrShuttingDown(networkManager))
        {
            if (waitedMilliseconds >= ShutdownTimeoutMilliseconds)
            {
                throw new TimeoutException(
                    "Timed out while waiting for the previous Netcode session to shut down."
                );
            }

            const int pollIntervalMilliseconds = 16;
            await UnityRealtimeDelay.WaitAsync(pollIntervalMilliseconds);
            waitedMilliseconds += pollIntervalMilliseconds;
        }
    }

    private static UnityTransport GetTransport()
    {
        NetworkManager networkManager = GetNetworkManager();

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
            throw new InvalidOperationException(
                "UnityTransport is not attached to NetworkManager."
            );

        return transport;
    }

    private static UnityTransport GetRelayTransport()
    {
        UnityTransport transport = GetTransport();
        transport.UseWebSockets = string.Equals(
            RelayConnectionType,
            "wss",
            StringComparison.OrdinalIgnoreCase
        );
        return transport;
    }

    private static NetworkManager GetNetworkManager()
    {
        if (NetworkManager.Singleton == null)
            throw new InvalidOperationException("NetworkManager.Singleton is not available.");

        return NetworkManager.Singleton;
    }

    private static bool IsRunningOrShuttingDown(NetworkManager networkManager)
    {
        return networkManager.IsListening
            || networkManager.ShutdownInProgress
            || networkManager.IsClient
            || networkManager.IsServer;
    }
}
// RelayConnection은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
