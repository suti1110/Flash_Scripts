using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneNavigation : MonoBehaviour
{
    [SerializeField]
    private OnlyOneUnityString _destinationScene;

    public void LoadScene()
    {
        if (!TryGetDestination(out string sceneName))
            return;

        SceneManager.LoadScene(sceneName);
    }

    public async void LeaveRoomAndLoadScene()
    {
        if (!TryGetDestination(out string sceneName))
            return;

        using IDisposable loading = NetworkLoadingPanel.Begin("Leaving room");

        try
        {
            RelayManager relayManager = RelayManager.Instance;
            if (
                relayManager != null
                && (
                    relayManager.IsCustomRoom
                    || !string.IsNullOrWhiteSpace(relayManager.CurrentRoomId)
                )
            )
            {
                await relayManager.LeaveLobby();
            }

            SceneManager.LoadScene(sceneName);
        }
        catch (Exception exception)
        {
            EditorLog.LogError($"Failed to leave the room and load {sceneName}: {exception}");
        }
    }

    private bool TryGetDestination(out string sceneName)
    {
        sceneName = _destinationScene;
        if (!string.IsNullOrWhiteSpace(sceneName))
            return true;

        EditorLog.LogError($"Destination scene is not assigned on {name}.");
        return false;
    }
}
// SceneNavigation은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
