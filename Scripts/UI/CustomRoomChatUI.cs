using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomRoomChatUI : MonoBehaviour
{
    [SerializeField]
    private TMP_InputField _messageInput;

    [SerializeField]
    private Button _sendButton;

    [SerializeField]
    private RectTransform _messageRoot;

    [SerializeField]
    private CustomRoomChatMessageView _messagePrefab;

    [SerializeField]
    private ScrollRect _scrollRect;

    private readonly List<CustomRoomChatMessageView> _messageViews = new();
    private CustomRoomChat _chat;

    private void Awake()
    {
        if (_messageInput != null)
            _messageInput.characterLimit = CustomRoomChat.MaximumMessageCharacters;
    }

    private void OnEnable()
    {
        if (_sendButton != null)
            _sendButton.onClick.AddListener(SendCurrentMessage);
        if (_messageInput != null)
            _messageInput.onSubmit.AddListener(HandleSubmit);

        TryBindChat();
    }

    private void Update()
    {
        if (_chat == null)
            TryBindChat();
    }

    private void OnDisable()
    {
        if (_sendButton != null)
            _sendButton.onClick.RemoveListener(SendCurrentMessage);
        if (_messageInput != null)
            _messageInput.onSubmit.RemoveListener(HandleSubmit);

        UnbindChat();
    }

    public void SendCurrentMessage()
    {
        if (_chat == null || _messageInput == null)
            return;

        string message = _messageInput.text;
        if (string.IsNullOrWhiteSpace(message))
            return;

        _chat.SendChatMessage(message);
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        _messageInput.SetTextWithoutNotify(string.Empty);
        _messageInput.ActivateInputField();
    }

    private void HandleSubmit(string _)
    {
        SendCurrentMessage();
    }

    private void TryBindChat()
    {
        CustomRoomChat chat = CustomRoomChat.Instance;
        if (chat == null || chat == _chat)
            return;

        UnbindChat();
        _chat = chat;
        _chat.MessageReceived += HandleMessageReceived;
        RenderHistory();
    }

    private void UnbindChat()
    {
        if (_chat != null)
            _chat.MessageReceived -= HandleMessageReceived;

        _chat = null;
    }

    private void RenderHistory()
    {
        ClearMessageViews();
        if (_chat == null)
            return;

        IReadOnlyList<CustomRoomChatMessage> messages = _chat.Messages;
        for (int i = 0; i < messages.Count; i++)
            AddMessage(messages[i]);
    }

    private void HandleMessageReceived(CustomRoomChatMessage message)
    {
        AddMessage(message);
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.ChatReceive);
    }

    private void AddMessage(CustomRoomChatMessage message)
    {
        if (_messagePrefab == null || _messageRoot == null)
            return;

        CustomRoomChatMessageView view = Instantiate(_messagePrefab, _messageRoot);
        view.Bind(message);
        _messageViews.Add(view);

        if (_messageViews.Count > CustomRoomChat.MaximumHistoryCount)
        {
            Destroy(_messageViews[0].gameObject);
            _messageViews.RemoveAt(0);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_messageRoot);
        if (_scrollRect != null)
            _scrollRect.verticalNormalizedPosition = 0f;
    }

    private void ClearMessageViews()
    {
        for (int i = 0; i < _messageViews.Count; i++)
        {
            if (_messageViews[i] != null)
                Destroy(_messageViews[i].gameObject);
        }

        _messageViews.Clear();
    }
}
// CustomRoomChatUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
