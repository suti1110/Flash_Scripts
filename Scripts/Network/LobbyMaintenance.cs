using System;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using UnityEngine;

public sealed class LobbyMaintenance
{
    private readonly LobbySession _lobbySession;

    private int _heartbeatVersion;
    private int _pollVersion;

    public LobbyMaintenance(LobbySession lobbySession)
    {
        _lobbySession = lobbySession;
    }

    public void StartHeartbeat()
    {
        int version = ++_heartbeatVersion;
        HeartbeatLoopAsync(version);
    }

    public void StartPolling(
        Func<bool> isServer,
        Func<int> getMaxPlayers,
        Func<Task<bool>> tryMergeLobby
    )
    {
        int version = ++_pollVersion;
        PollLoopAsync(version, isServer, getMaxPlayers, tryMergeLobby);
    }

    public void Stop()
    {
        _heartbeatVersion++;
        _pollVersion++;
    }

    private async void HeartbeatLoopAsync(int version)
    {
        while (version == _heartbeatVersion && _lobbySession.HasLobby && Application.isPlaying)
        {
            await UnityRealtimeDelay.WaitAsync(25000);

            if (version != _heartbeatVersion || !_lobbySession.HasLobby || !Application.isPlaying)
                break;

            if (!_lobbySession.IsLocalPlayerHost)
                break;

            try
            {
                await _lobbySession.SendHeartbeatAsync();
            }
            catch (LobbyServiceException exception)
            {
                EditorLog.LogError($"Heartbeat 실패: {exception}");
                break;
            }
        }
    }

    private async void PollLoopAsync(
        int version,
        Func<bool> isServer,
        Func<int> getMaxPlayers,
        Func<Task<bool>> tryMergeLobby
    )
    {
        while (version == _pollVersion && _lobbySession.HasLobby && Application.isPlaying)
        {
            await UnityRealtimeDelay.WaitAsync(3000);

            if (version != _pollVersion || !_lobbySession.HasLobby || !Application.isPlaying)
                break;

            try
            {
                if (isServer() && _lobbySession.PlayerCount < getMaxPlayers())
                {
                    if (await tryMergeLobby())
                        break;
                }

                await _lobbySession.RefreshAsync();
            }
            catch (LobbyServiceException exception)
            {
                EditorLog.LogError($"Polling 실패: {exception}");
                break;
            }
        }
    }
}
// LobbyMaintenance은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
