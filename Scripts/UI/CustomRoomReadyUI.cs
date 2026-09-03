using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomRoomReadyUI : MonoBehaviour
{
    [SerializeField]
    private Transform _playerListRoot;

    [SerializeField]
    private CustomRoomReadyPlayerView _playerViewPrefab;

    [SerializeField]
    private Button _readyButton;

    [SerializeField]
    private TMP_Text _readyButtonText;

    [Header("Ready Button Visuals")]
    [SerializeField]
    private Image _readyButtonGraphic;

    [SerializeField]
    private Sprite _readySprite;

    [SerializeField]
    private Color _readyColor = Color.white;

    [SerializeField]
    private Sprite _cancelReadySprite;

    [SerializeField]
    private Color _cancelReadyColor = Color.white;

    [SerializeField]
    private Button _startGameButton;

    private readonly List<CustomRoomReadyPlayerView> _playerViews = new();
    private CustomRoomReadyState _readyState;

    private void OnEnable()
    {
        if (_readyButton != null)
            _readyButton.onClick.AddListener(ToggleReady);

        TryBindReadyState();
    }

    private void Update()
    {
        if (_readyState == null)
            TryBindReadyState();
    }

    private void OnDisable()
    {
        if (_readyButton != null)
            _readyButton.onClick.RemoveListener(ToggleReady);

        UnbindReadyState();
    }

    private void ToggleReady()
    {
        if (
            _readyState == null
            || !_readyState.TryGetLocalPlayer(out CustomRoomReadyEntry localPlayer)
            || localPlayer.IsHost
        )
            return;

        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        _readyState.SetLocalReady(!localPlayer.IsReady);
    }

    private void TryBindReadyState()
    {
        CustomRoomReadyState readyState = CustomRoomReadyState.Instance;
        if (readyState == null || readyState == _readyState)
            return;

        UnbindReadyState();
        _readyState = readyState;
        _readyState.ReadyStatesChanged += Render;
        Render();
    }

    private void UnbindReadyState()
    {
        if (_readyState != null)
            _readyState.ReadyStatesChanged -= Render;

        _readyState = null;
    }

    private void Render()
    {
        if (_readyState == null)
            return;

        EnsurePlayerViewCount(_readyState.PlayerCount);
        for (int i = 0; i < _playerViews.Count; i++)
        {
            if (i < _readyState.PlayerCount)
            {
                _playerViews[i].Bind(_readyState.GetPlayer(i));
                _playerViews[i].gameObject.SetActive(true);
            }
            else
            {
                _playerViews[i].gameObject.SetActive(false);
            }
        }

        bool isHost = false;
        bool isReady = false;
        if (_readyState.TryGetLocalPlayer(out CustomRoomReadyEntry localPlayer))
        {
            isHost = localPlayer.IsHost;
            isReady = localPlayer.IsReady;
        }

        if (_readyButton != null)
            _readyButton.gameObject.SetActive(!isHost);
        if (_readyButtonText != null)
            _readyButtonText.text = isReady ? "Cancel Ready" : "Ready";
        UpdateReadyButtonVisual(isReady);
        if (_startGameButton != null)
            _startGameButton.interactable = isHost && _readyState.AreAllPlayersReady;
    }

    private void UpdateReadyButtonVisual(bool isReady)
    {
        Image graphic = _readyButtonGraphic;
        if (graphic == null && _readyButton != null)
            graphic = _readyButton.image;
        if (graphic == null)
            return;

        graphic.sprite = isReady ? _cancelReadySprite : _readySprite;
        graphic.color = isReady ? _cancelReadyColor : _readyColor;
    }

    private void EnsurePlayerViewCount(int count)
    {
        if (_playerListRoot == null || _playerViewPrefab == null)
            return;

        while (_playerViews.Count < count)
        {
            CustomRoomReadyPlayerView view = Instantiate(
                _playerViewPrefab,
                _playerListRoot
            );
            _playerViews.Add(view);
        }
    }
}
// CustomRoomReadyUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
