using TMPro;
using UnityEngine;

public sealed class CustomRoomReadyPlayerView : MonoBehaviour
{
    [SerializeField]
    private TMP_Text _playerNameText;

    [SerializeField]
    private TMP_Text _readyStateText;

    [SerializeField]
    private Color _readyColor = new(0.2f, 0.8f, 0.35f, 1f);

    [SerializeField]
    private Color _notReadyColor = new(0.75f, 0.75f, 0.75f, 1f);

    public void Bind(CustomRoomReadyEntry player)
    {
        if (_playerNameText != null)
        {
            string playerName = $"Player {player.ClientId + 1}";
            _playerNameText.text = player.IsHost ? $"{playerName} (Host)" : playerName;
        }

        if (_readyStateText != null)
        {
            _readyStateText.text = player.IsReady ? "Ready" : "Not Ready";
            _readyStateText.color = player.IsReady ? _readyColor : _notReadyColor;
        }
    }
}
// CustomRoomReadyPlayerView은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
