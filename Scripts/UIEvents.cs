using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "UIEvent", menuName = "UIEvents/UIEvent")]
public class UIEvents : ScriptableObject
{
    // ========================================
    // 매치메이킹 버튼 클릭
    // ========================================
    public async void OnClickMatchMaking2Player()
    {
        EditorLog.Log("매치메이킹 버튼 클릭!");

        // Lobby 기반 매치메이킹 시작
        await RelayManager.Instance.StartMatchmaking(2);
    }

    public async void OnClickMatchMaking3Player()
    {
        EditorLog.Log("매치메이킹 버튼 클릭!");

        // Lobby 기반 매치메이킹 시작
        await RelayManager.Instance.StartMatchmaking(3);
    }

    public async void OnClickMatchMaking4Player()
    {
        EditorLog.Log("매치메이킹 버튼 클릭!");

        // Lobby 기반 매치메이킹 시작
        await RelayManager.Instance.StartMatchmaking(4);
    }

    // ========================================
    // 방 나가기 버튼 클릭
    // ========================================
    public async void OnClickLeave()
    {
        EditorLog.Log("방 나가기 버튼 클릭!");

        await RelayManager.Instance.LeaveLobby();
    }

    // ========================================
    // 기존 UI 헬퍼 메서드들
    // ========================================
    public void EnableObject(GameObject obj)
    {
        obj.SetActive(true);
    }

    public void DisableObject(GameObject obj)
    {
        obj.SetActive(false);
    }

    public void ButtonSound()
    {
        AudioManager.SfxPlay(AudioManager.Instance.Container.MouseClick);
    }
}
