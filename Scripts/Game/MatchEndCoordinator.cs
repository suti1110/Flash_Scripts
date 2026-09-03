using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine.SceneManagement;

public sealed class MatchEndCoordinator
{
    private readonly string _mainMenuSceneName;
    private bool _isFinishing;

    public MatchEndCoordinator(string mainMenuSceneName)
    {
        _mainMenuSceneName = mainMenuSceneName;
    }

    public void FinishMatch()
    {
        if (_isFinishing)
            return;

        _isFinishing = true;

        if (GameManager.Instance != null)
        {
            GameManager.Instance.FreezeAllPlayers();
            GameManager.Instance.SetGameKind(GameKind.None);
        }

        if (RelayManager.Instance != null && !RelayManager.Instance.IsCustomRoom)
            RelayManager.Instance.SetIntentionalDisconnect();

        WaitAction.Wait(3f, ReturnToMainMenuAsync);
    }

    private async void ReturnToMainMenuAsync()
    {
        RelayManager relayManager = RelayManager.Instance;
        if (relayManager != null && relayManager.IsCustomRoom)
        {
            if (!relayManager.IsServer)
                return;

            EditorLog.Log("게임 종료! 사용자 정의 방으로 돌아갑니다.");

            try
            {
                await relayManager.ReturnToCustomRoomAsync();
            }
            catch (System.Exception exception)
            {
                EditorLog.LogError($"사용자 정의 방 복귀 실패: {exception}");
            }

            return;
        }

        EditorLog.Log("게임 종료! 메인 화면으로 돌아갑니다.");

        if (relayManager != null)
        {
            await relayManager.CloseSessionAsync(relayManager.IsServer);
        }
        else if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        await Task.Delay(500);
        SceneManager.LoadScene(_mainMenuSceneName);
    }
}
// MatchEndCoordinator은 경기 규칙과 진행 상태 중 하나의 독립된 게임플레이 책임을 담당한다.
// 모드별 정책을 분리하여 공통 경기 흐름이 구체적인 모드 구현에 직접 의존하지 않도록 한다.
