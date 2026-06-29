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

    [SerializeField]
    private float _rouletteSpinDuration = 4f;

    private Tweener _textTweener;

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

        switch (state)
        {
            case MatchingState.None:
                _matchingText.text = _noneText;
                break;

            case MatchingState.FindingMatch:
                _cancelButton.SetActive(true);
                string baseText = _findingMatchText;
                _textTweener = DOTween
                    .To(
                        () => 0f,
                        x =>
                        {
                            int dotCount = Mathf.FloorToInt(x);
                            _matchingText.text = baseText + new string('.', dotCount);
                        },
                        4f,
                        1f
                    )
                    .SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear);
                ; // 대기 상태 표현
                break;

            case MatchingState.PendingPlayer:
                _cancelButton.SetActive(true);
                _pendingPlayerCountText.gameObject.SetActive(true);
                baseText = _pendingPlayerText;
                _textTweener = DOTween
                    .To(
                        () => 0f,
                        x =>
                        {
                            int dotCount = Mathf.FloorToInt(x);
                            _matchingText.text = baseText + new string('.', dotCount);
                        },
                        4f,
                        1f
                    )
                    .SetLoops(-1, LoopType.Restart)
                    .SetEase(Ease.Linear); // 대기 상태 표현
                break;

            case MatchingState.GameStart:
                baseText = _gameStartText;
                _textTweener = DOTween
                    .To(
                        getter: () => RelayManager.Instance.GameStartTerm,
                        setter: x =>
                        {
                            _matchingText.text = baseText + $"...{x}";
                        },
                        endValue: 0,
                        duration: RelayManager.Instance.GameStartTerm
                    )
                    .SetEase(Ease.Linear);
                break;

            case MatchingState.SelectMode:
                string[] mapNames = RelayManager.Instance.AvailableMode;
                string targetMap = RelayManager.Instance.MapName;

                int index = 0;

                // 방장이 고른 정답 맵이 배열에서 몇 번째(Index)인지 찾습니다.
                int targetIndex = System.Array.IndexOf(mapNames, targetMap);

                // N바퀴 근처를 돌면서, 정확히 targetIndex 칸에서 멈추도록 목표치를 계산합니다.
                int finalSpins =
                    (_rouletteSpinCount / mapNames.Length) * mapNames.Length + targetIndex;

                _textTweener = DOTween
                    .To(
                        getter: () => 0f,
                        setter: x =>
                        {
                            int temp = Mathf.FloorToInt(x) % mapNames.Length;

                            if (index != temp)
                            {
                                AudioManager.SfxPlay(AudioManager.Instance.Container.Roulette);
                            }

                            index = temp;

                            _matchingText.text = $"Mode\n<size=150%>{mapNames[index]}</size>";
                        },
                        endValue: finalSpins,
                        duration: _rouletteSpinDuration
                    )
                    .SetEase(Ease.OutExpo)
                    .OnComplete(() =>
                    {
                        _matchingText.text =
                            $"Mode\n<color=yellow><size=150%>{targetMap}</size></color>";

                        AudioManager.SfxPlay(AudioManager.Instance.Container.Select);

                        _matchingText.transform.DOPunchScale(Vector3.one * 0.3f, 0.5f, 5, 1f);

                        if (RelayManager.Instance.IsServer)
                        {
                            // 당첨 2초 후 씬 이동
                            WaitAction.Wait(
                                2f,
                                () =>
                                {
                                    NetworkManager.Singleton.SceneManager.LoadScene(
                                        targetMap,
                                        UnityEngine.SceneManagement.LoadSceneMode.Single
                                    );
                                }
                            );
                        }
                    });

                break;
        }
    }

    private void Update()
    {
        if (_pendingPlayerCountText.gameObject.activeSelf)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            {
                _pendingPlayerCountText.text =
                    $"({NetworkManager.Singleton.ConnectedClients.Count}/{RelayManager.Instance.MaxPlayers})";
            }
        }
    }
}
