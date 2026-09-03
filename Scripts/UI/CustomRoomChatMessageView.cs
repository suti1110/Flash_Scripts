using TMPro;
using UnityEngine;

public sealed class CustomRoomChatMessageView : MonoBehaviour
{
    [SerializeField]
    private TMP_Text _messageText;

    [SerializeField]
    private Color _senderColor = Color.yellow;

    [SerializeField]
    private Color _contentColor = Color.white;

    [SerializeField]
    private bool _boldSender = true;

    public void Bind(CustomRoomChatMessage message)
    {
        if (_messageText == null)
            return;

        string senderColor = ColorUtility.ToHtmlStringRGBA(_senderColor);
        string contentColor = ColorUtility.ToHtmlStringRGBA(_contentColor);
        string sender = $"<color=#{senderColor}>{message.SenderName}</color>";

        if (_boldSender)
            sender = $"<b>{sender}</b>";

        _messageText.text =
            $"{sender}: <color=#{contentColor}><noparse>{message.Content}</noparse></color>";
    }
}
// CustomRoomChatMessageView은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
