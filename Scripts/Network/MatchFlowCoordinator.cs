using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class MatchFlowCoordinator
{
    private int _scheduledLoadVersion;

    public Task LoadNetworkSceneAsync(
        string sceneName,
        float delaySeconds,
        LoadSceneMode loadSceneMode = LoadSceneMode.Single,
        Action beforeLoad = null,
        Action onLoadFailed = null
    )
    {
        int loadVersion = ++_scheduledLoadVersion;
        return LoadSceneAsync(
            sceneName,
            delaySeconds,
            loadSceneMode,
            beforeLoad,
            onLoadFailed,
            loadVersion
        );
    }

    public void CancelPendingSceneLoad()
    {
        _scheduledLoadVersion++;
    }

    private async Task LoadSceneAsync(
        string sceneName,
        float delaySeconds,
        LoadSceneMode loadSceneMode,
        Action beforeLoad,
        Action onLoadFailed,
        int loadVersion
    )
    {
        await UnityRealtimeDelay.WaitAsync(TimeSpan.FromSeconds(Mathf.Max(0f, delaySeconds)));

        if (loadVersion != _scheduledLoadVersion || !Application.isPlaying)
            throw new OperationCanceledException("The scheduled network scene load was cancelled.");

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer || !networkManager.IsListening)
            throw new InvalidOperationException("The network server is not ready to load a scene.");

        TaskCompletionSource<bool> completion = new();

        void HandleLoadCompleted(
            string loadedSceneName,
            LoadSceneMode completedLoadMode,
            System.Collections.Generic.List<ulong> clientsCompleted,
            System.Collections.Generic.List<ulong> clientsTimedOut
        )
        {
            if (!string.Equals(loadedSceneName, sceneName, StringComparison.Ordinal))
                return;

            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                completion.TrySetException(
                    new TimeoutException(
                        $"{clientsTimedOut.Count} client(s) timed out while loading {sceneName}."
                    )
                );
                return;
            }

            completion.TrySetResult(true);
        }

        networkManager.SceneManager.OnLoadEventCompleted += HandleLoadCompleted;
        try
        {
            beforeLoad?.Invoke();
            SceneEventProgressStatus status = networkManager.SceneManager.LoadScene(
                sceneName,
                loadSceneMode
            );
            if (status != SceneEventProgressStatus.Started)
                throw new InvalidOperationException(
                    $"Failed to start the network scene load. Scene={sceneName}, Status={status}"
                );

            Task completedTask = await Task.WhenAny(
                completion.Task,
                UnityRealtimeDelay.WaitAsync(30000)
            );
            if (completedTask != completion.Task)
                throw new TimeoutException(
                    $"Timed out while loading the network scene {sceneName}."
                );

            await completion.Task;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            onLoadFailed?.Invoke();
            throw;
        }
        finally
        {
            if (networkManager != null && networkManager.SceneManager != null)
                networkManager.SceneManager.OnLoadEventCompleted -= HandleLoadCompleted;
        }
    }
}
// MatchFlowCoordinator은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
