using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum MessageType
{
    Message,
    Warning,
}

public class MessageOnUI : MonoBehaviour
{
    public static MessageOnUI Instance { get; private set; }

    [SerializeField]
    private Image _messagePanel;

    [SerializeField]
    private TMP_Text _messageText;

    [System.Serializable]
    private struct TypeColorPair
    {
        public MessageType Type;
        public MessageColor Color;
    }

    [System.Serializable]
    private struct MessageColor
    {
        public Color PanelColor;
        public Color TextColor;
        public Color TextOutlineColor;
    }

    [SerializeField]
    private TypeColorPair[] _typeColorPairs;

    private readonly Dictionary<MessageType, MessageColor> _messageColors = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        foreach (var pair in _typeColorPairs)
        {
            _messageColors[pair.Type] = pair.Color;
        }
    }

    private Sequence _sequence;

    private Sequence PanelSummon()
    {
        if (_sequence != null && _sequence.IsActive())
        {
            _sequence.Kill();
        }

        _sequence = DOTween.Sequence();
        _messagePanel.gameObject.SetActive(true);
        _sequence.Append(
            _messagePanel.transform.DOScale(1.2f, 0.3f).SetEase(Ease.OutBack).From(0.2f)
        );
        _sequence.Append(_messagePanel.transform.DOScale(1f, 0.2f).SetEase(Ease.InBack));
        _sequence.AppendInterval(2f);
        _sequence.Append(_messagePanel.DOFade(0f, 0.5f));
        _sequence.Join(_messageText.DOFade(0f, 0.5f));
        _sequence.AppendCallback(() =>
        {
            _messagePanel.gameObject.SetActive(false);
        });
        return _sequence;
    }

    public static void ShowMessage(string message, MessageType type = MessageType.Message)
    {
        if (Instance == null)
        {
            EditorLog.LogWarning($"MessageOnUI가 없어 메시지를 표시하지 못했습니다: {message}");
            return;
        }

        if (Instance._messageColors.TryGetValue(type, out var colors))
        {
            Instance._messagePanel.color = colors.PanelColor;
            Instance._messageText.color = colors.TextColor;
            Instance._messageText.outlineColor = colors.TextOutlineColor;
        }
        Instance._messageText.text = message;
        SO_SFXContainer container = AudioManager.Instance?.Container;
        AudioManager.SfxPlay(
            type == MessageType.Warning ? container?.Warning : container?.Notification
        );
        Instance.PanelSummon();
    }
}
// MessageOnUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
