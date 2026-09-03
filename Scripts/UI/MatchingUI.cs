using DG.Tweening;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class MatchingUI : MonoBehaviour
{
    [SerializeField]
    private TMP_Text _matchingText;

    [SerializeField]
    private TMP_Text _pendingPlayerCountText;

    [SerializeField]
    private GameObject _cancelButton;

    [SerializeField]
    private GameObject _isHostText;

    [SerializeField]
    private string _noneText;

    [SerializeField]
    private string _findingMatchText;

    [SerializeField]
    private string _pendingPlayerText;

    [SerializeField]
    private string _gameStartText;

    [SerializeField]
    private int _rouletteSpinCount = 100;

    private Tweener _textTweener;
    private int _lastPendingPlayerCount = -1;
    private int _lastPendingMaxPlayers = -1;

    private void Start()
    {
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnMatchingStateChanged += OnMatchingStateChanged;

            OnMatchingStateChanged(RelayManager.Instance.MatchingState);
        }
    }

    private void OnDestroy()
    {
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnMatchingStateChanged -= OnMatchingStateChanged;

        _textTweener?.Kill();
    }

    private void OnMatchingStateChanged(MatchingState state)
    {
        _pendingPlayerCountText.gameObject.SetActive(false);
        _cancelButton.SetActive(false);
        _isHostText.SetActive(RelayManager.Instance.IsHost);
        _textTweener?.Kill();
        _textTweener = null;
        _lastPendingPlayerCount = -1;
        _lastPendingMaxPlayers = -1;

        switch (state)
        {
            case MatchingState.None:
                _matchingText.text = _noneText;
                break;

            case MatchingState.FindingMatch:
                _cancelButton.SetActive(true);
                _textTweener = CreateWaitingTextTween(_findingMatchText);
                break;

            case MatchingState.PendingPlayer:
                _cancelButton.SetActive(true);
                _pendingPlayerCountText.gameObject.SetActive(true);
                _textTweener = CreateWaitingTextTween(_pendingPlayerText);
                break;

            case MatchingState.GameStart:
                _textTweener = CreateCountdownTween(
                    _gameStartText,
                    RelayManager.Instance.GameStartTerm
                );
                break;

            case MatchingState.SelectMode:
            {
                string[] modeNames = RelayManager.Instance.GetAvailableGameModeDisplayNames();
                string targetMode = RelayManager.Instance.GameModeDisplayName;

                if (modeNames.Length == 0 || string.IsNullOrWhiteSpace(targetMode))
                {
                    _matchingText.text = _noneText;
                    break;
                }

                _textTweener = CreateSelectionTween(
                    "Mode",
                    modeNames,
                    targetMode,
                    RelayManager.Instance.ModeSelectionDuration
                );
                break;
            }

            case MatchingState.SelectMap:
            {
                string[] mapNames = RelayManager.Instance.GetAvailableMapDisplayNames();
                string targetMap = RelayManager.Instance.MapDisplayName;

                if (mapNames.Length == 0 || string.IsNullOrWhiteSpace(targetMap))
                {
                    _matchingText.text = _noneText;
                    break;
                }

                _textTweener = CreateSelectionTween(
                    "Map",
                    mapNames,
                    targetMap,
                    RelayManager.Instance.MapSelectionDuration
                );
                break;
            }
        }
    }

    private Tweener CreateSelectionTween(
        string title,
        string[] options,
        string target,
        float duration
    )
    {
        int index = -1;
        int targetIndex = System.Array.IndexOf(options, target);
        if (targetIndex < 0)
            targetIndex = 0;

        int finalSpins = (_rouletteSpinCount / options.Length) * options.Length + targetIndex;

        return DOTween
            .To(
                getter: () => 0f,
                setter: value =>
                {
                    int nextIndex = Mathf.FloorToInt(value) % options.Length;
                    if (index == nextIndex)
                        return;

                    if (index >= 0)
                        AudioManager.SfxPlay(AudioManager.Instance.Container.Roulette);

                    index = nextIndex;
                    _matchingText.text = $"{title}\n<size=150%>{options[index]}</size>";
                },
                endValue: finalSpins,
                duration: duration
            )
            .SetEase(Ease.OutExpo)
            .OnComplete(() =>
            {
                _matchingText.text = $"{title}\n<color=yellow><size=150%>{target}</size></color>";

                AudioManager.SfxPlay(AudioManager.Instance.Container.Select);
                _matchingText.transform.DOPunchScale(Vector3.one * 0.3f, 0.5f, 5, 1f);
            });
    }

    private Tweener CreateWaitingTextTween(string baseText)
    {
        string[] frames = new string[5];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = i == 0 ? baseText : baseText + new string('.', i);

        int currentFrame = -1;
        return DOTween
            .To(
                () => 0f,
                value =>
                {
                    int nextFrame = Mathf.Clamp(Mathf.FloorToInt(value), 0, frames.Length - 1);
                    if (currentFrame == nextFrame)
                        return;

                    currentFrame = nextFrame;
                    _matchingText.text = frames[currentFrame];
                },
                frames.Length - 1,
                1f
            )
            .SetLoops(-1, LoopType.Restart)
            .SetEase(Ease.Linear);
    }

    private Tweener CreateCountdownTween(string baseText, int duration)
    {
        string[] frames = new string[duration + 1];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = $"{baseText}...{i}";

        int currentSecond = -1;
        return DOTween
            .To(
                () => (float)duration,
                value =>
                {
                    int nextSecond = Mathf.Clamp(Mathf.CeilToInt(value), 0, duration);
                    if (currentSecond == nextSecond)
                        return;

                    currentSecond = nextSecond;
                    _matchingText.text = frames[currentSecond];
                },
                0f,
                duration
            )
            .SetEase(Ease.Linear);
    }

    private void Update()
    {
        if (!_pendingPlayerCountText.gameObject.activeSelf)
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return;

        int playerCount = networkManager.ConnectedClients.Count;
        int maxPlayers = RelayManager.Instance.MaxPlayers;
        if (playerCount == _lastPendingPlayerCount && maxPlayers == _lastPendingMaxPlayers)
            return;

        _lastPendingPlayerCount = playerCount;
        _lastPendingMaxPlayers = maxPlayers;
        _pendingPlayerCountText.SetText("({0:0}/{1:0})", playerCount, maxPlayers);
    }
}
// MatchingUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
