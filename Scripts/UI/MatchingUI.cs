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

    private Tween _textTweener;
    private MapRouletteView _mapRoulette;
    private bool _started;
    private int _lastPendingPlayerCount = -1;
    private int _lastPendingMaxPlayers = -1;

    private void Start()
    {
        _started = true;
        if (RelayManager.Instance != null)
        {
            RelayManager.Instance.OnMatchingStateChanged += OnMatchingStateChanged;

            OnMatchingStateChanged(RelayManager.Instance.MatchingState);
        }
    }

    private void OnEnable()
    {
        if (_started && RelayManager.Instance != null)
            OnMatchingStateChanged(RelayManager.Instance.MatchingState);
    }

    private void OnDestroy()
    {
        if (RelayManager.Instance != null)
            RelayManager.Instance.OnMatchingStateChanged -= OnMatchingStateChanged;

        _textTweener?.Kill();
    }

    private void OnDisable()
    {
        _textTweener?.Kill();
        if (_mapRoulette != null)
            _mapRoulette.Hide();
    }

    private void OnMatchingStateChanged(MatchingState state)
    {
        _pendingPlayerCountText.gameObject.SetActive(false);
        _cancelButton.SetActive(false);
        _isHostText.SetActive(RelayManager.Instance.IsHost);
        _textTweener?.Kill();
        _textTweener = null;
        _matchingText.gameObject.SetActive(true);
        if (_mapRoulette != null)
            _mapRoulette.Hide();
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
                _textTweener = CreateModeSelectionTween(
                    modeNames, targetMode, RelayManager.Instance.ModeSelectionDuration);
                break;
            }

            case MatchingState.SelectMap:
            {
                SO_MapDefinition[] maps = RelayManager.Instance.GetAvailableMaps();
                string targetMap = RelayManager.Instance.MapName;

                if (maps.Length == 0 || string.IsNullOrWhiteSpace(targetMap))
                {
                    _matchingText.text = _noneText;
                    break;
                }

                if (_mapRoulette == null)
                {
                    var view = new GameObject("Map Roulette", typeof(RectTransform));
                    view.transform.SetParent(transform, false);
                    _mapRoulette = view.AddComponent<MapRouletteView>();
                    _mapRoulette.Initialize(_matchingText.font);
                }
                _matchingText.gameObject.SetActive(false);
                _textTweener = _mapRoulette.Play(
                    maps, targetMap, RelayManager.Instance.GameModeDisplayName,
                    RelayManager.Instance.MapSelectionDuration, _rouletteSpinCount);
                if (_textTweener == null)
                {
                    _matchingText.gameObject.SetActive(true);
                    _matchingText.text = RelayManager.Instance.MapDisplayName;
                }
                break;
            }
        }
    }

    // 모드는 기존처럼 이름을 빠르게 교체하다가 서버가 정한 결과에 멈춥니다.
    private Tweener CreateModeSelectionTween(string[] options, string target, float duration)
    {
        int index = -1;
        int targetIndex = System.Array.IndexOf(options, target);
        if (targetIndex < 0)
            targetIndex = 0;
        int finalSpins = (_rouletteSpinCount / options.Length) * options.Length + targetIndex;

        return DOTween.To(
            () => 0f,
            value =>
            {
                int nextIndex = Mathf.FloorToInt(value) % options.Length;
                if (index == nextIndex)
                    return;
                if (index >= 0 && AudioManager.Instance != null)
                    AudioManager.SfxPlay(AudioManager.Instance.Container.Roulette);
                index = nextIndex;
                _matchingText.text = $"Mode\n<size=150%>{options[index]}</size>";
            },
            finalSpins,
            Mathf.Max(0, duration)
        )
            .SetEase(Ease.OutExpo)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() =>
            {
                _matchingText.text = $"Mode\n<color=yellow><size=150%>{target}</size></color>";
                if (AudioManager.Instance != null)
                    AudioManager.SfxPlay(AudioManager.Instance.Container.Select);
                _matchingText.transform.DOPunchScale(Vector3.one * 0.3f, 0.5f, 5, 1f)
                    .SetUpdate(true).SetLink(gameObject);
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
