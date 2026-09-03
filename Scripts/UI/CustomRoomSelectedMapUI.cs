using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomRoomSelectedMapUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField]
    private SO_MapCatalog _mapCatalog;

    [SerializeField]
    private Sprite _defaultMapThumbnail;

    [Header("Display")]
    [SerializeField]
    private Image _mapThumbnail;

    [Header("Host Controls")]
    [SerializeField]
    private Button _changeMapButton;

    [SerializeField]
    private GameObject _mapSelectionPanel;

    private RelayManager _relayManager;

    private void OnEnable()
    {
        if (_changeMapButton != null)
            _changeMapButton.onClick.AddListener(OpenMapSelectionPanel);

        TryBindRelayManager();
    }

    private void Update()
    {
        if (_relayManager == null)
            TryBindRelayManager();
    }

    private void OnDisable()
    {
        if (_changeMapButton != null)
            _changeMapButton.onClick.RemoveListener(OpenMapSelectionPanel);

        UnbindRelayManager();
    }

    private void OpenMapSelectionPanel()
    {
        if (_relayManager == null || !_relayManager.IsCustomRoom || !_relayManager.IsHost)
            return;

        if (_mapSelectionPanel != null)
        {
            AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
            _mapSelectionPanel.SetActive(true);
        }
    }

    private void TryBindRelayManager()
    {
        RelayManager relayManager = RelayManager.Instance;
        if (relayManager == null || relayManager == _relayManager)
            return;

        UnbindRelayManager();
        _relayManager = relayManager;
        _relayManager.CustomRoomMapSelectionChanged += Render;
        SetChangeMapInteractable(_relayManager.IsCustomRoom && _relayManager.IsHost);
        Render(_relayManager.CurrentCustomRoomMapSelection);
    }

    private void UnbindRelayManager()
    {
        if (_relayManager != null)
            _relayManager.CustomRoomMapSelectionChanged -= Render;

        _relayManager = null;
    }

    private void Render(CustomRoomMapSelection selection)
    {
        SO_MapDefinition map = FindMap(selection);
        if (_mapThumbnail != null)
            _mapThumbnail.sprite = map != null && map.Thumbnail != null
                ? map.Thumbnail
                : _defaultMapThumbnail;
    }

    private SO_MapDefinition FindMap(CustomRoomMapSelection selection)
    {
        if (!selection.HasSelection || _mapCatalog == null)
            return null;

        for (int i = 0; i < _mapCatalog.Maps.Count; i++)
        {
            SO_MapDefinition map = _mapCatalog.Maps[i];
            if (
                map != null
                && map.Mode != null
                && string.Equals(map.Mode.Id, selection.ModeId, StringComparison.Ordinal)
                && string.Equals(
                    map.SceneName,
                    selection.MapSceneName,
                    StringComparison.Ordinal
                )
            )
            {
                return map;
            }
        }

        return null;
    }

    private void SetChangeMapInteractable(bool isInteractable)
    {
        if (_changeMapButton != null)
            _changeMapButton.interactable = isInteractable;
    }
}
// CustomRoomSelectedMapUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
