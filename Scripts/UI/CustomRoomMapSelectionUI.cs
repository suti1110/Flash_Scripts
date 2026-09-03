using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CustomRoomMapSelectionUI : MonoBehaviour
{
    [Header("Data")]
    [SerializeField]
    private SO_MapCatalog _mapCatalog;

    [SerializeField]
    private Sprite _defaultMapThumbnail;

    [Header("Mode List")]
    [SerializeField]
    private PracticeSelectionButton _modeButtonPrefab;

    [SerializeField]
    private Transform _modeButtonContainer;

    [Header("Map List")]
    [SerializeField]
    private PracticeSelectionButton _mapButtonPrefab;

    [SerializeField]
    private Transform _mapButtonContainer;

    [SerializeField]
    private bool _closeAfterMapSelection = true;

    private readonly List<SO_GameModeDefinition> _modeBuffer = new();
    private readonly List<SO_MapDefinition> _mapBuffer = new();
    private readonly List<PracticeSelectionButton> _modeButtons = new();
    private readonly List<PracticeSelectionButton> _mapButtons = new();

    private SO_GameModeDefinition _selectedMode;
    private SO_MapDefinition _selectedMap;

    private void OnEnable()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        ClearButtons(_modeButtons);
        ClearButtons(_mapButtons);
        _selectedMode = null;
        _selectedMap = null;

        RelayManager relayManager = RelayManager.Instance;
        if (
            relayManager == null
            || !relayManager.IsCustomRoom
            || !relayManager.IsHost
        )
            return;

        if (
            _mapCatalog == null
            || _modeButtonPrefab == null
            || _mapButtonPrefab == null
            || _modeButtonContainer == null
            || _mapButtonContainer == null
        )
        {
            EditorLog.LogError("Custom room map selection UI is not configured.");
            return;
        }

        _mapCatalog.CollectOnlineModes(relayManager.MaxPlayers, _modeBuffer);
        for (int i = 0; i < _modeBuffer.Count; i++)
        {
            SO_GameModeDefinition mode = _modeBuffer[i];
            PracticeSelectionButton button = Instantiate(
                _modeButtonPrefab,
                _modeButtonContainer
            );
            button.Bind(mode.DisplayName, mode.Icon, () => SelectMode(mode));
            _modeButtons.Add(button);
        }

        SO_GameModeDefinition initialMode = FindMode(relayManager.GameModeId);
        if (initialMode == null && _modeBuffer.Count > 0)
            initialMode = _modeBuffer[0];
        if (initialMode != null)
            SelectMode(initialMode);
    }

    private void SelectMode(SO_GameModeDefinition mode)
    {
        if (mode == null || RelayManager.Instance == null)
            return;

        _selectedMode = mode;
        _selectedMap = null;
        ClearButtons(_mapButtons);
        _mapCatalog.CollectOnlineMaps(mode, RelayManager.Instance.MaxPlayers, _mapBuffer);

        for (int i = 0; i < _mapBuffer.Count; i++)
        {
            SO_MapDefinition map = _mapBuffer[i];
            Sprite thumbnail = map.Thumbnail != null ? map.Thumbnail : _defaultMapThumbnail;
            PracticeSelectionButton button = Instantiate(
                _mapButtonPrefab,
                _mapButtonContainer
            );
            button.Bind(map.DisplayName, thumbnail, () => SelectMap(map, true));
            _mapButtons.Add(button);
        }

        SO_MapDefinition initialMap = FindCurrentMap(mode);
        if (initialMap != null)
            SelectMap(initialMap, false);

        UpdateButtonStates();
    }

    private void SelectMap(SO_MapDefinition map, bool selectedByUser)
    {
        if (map == null || RelayManager.Instance == null)
            return;

        try
        {
            RelayManager.Instance.SetCustomRoomMapSelection(map);
            _selectedMap = map;
            UpdateButtonStates();
            if (selectedByUser && _closeAfterMapSelection)
                gameObject.SetActive(false);
        }
        catch (Exception exception)
        {
            MessageOnUI.ShowMessage(exception.Message, MessageType.Warning);
            EditorLog.LogError($"Custom room map selection failed: {exception}");
        }
    }

    private SO_GameModeDefinition FindMode(string modeId)
    {
        for (int i = 0; i < _modeBuffer.Count; i++)
        {
            if (string.Equals(_modeBuffer[i].Id, modeId, StringComparison.Ordinal))
                return _modeBuffer[i];
        }

        return null;
    }

    private SO_MapDefinition FindCurrentMap(SO_GameModeDefinition mode)
    {
        RelayManager relayManager = RelayManager.Instance;
        if (
            relayManager == null
            || !string.Equals(relayManager.GameModeId, mode.Id, StringComparison.Ordinal)
        )
            return null;

        for (int i = 0; i < _mapBuffer.Count; i++)
        {
            if (
                string.Equals(
                    _mapBuffer[i].SceneName,
                    relayManager.MapName,
                    StringComparison.Ordinal
                )
            )
                return _mapBuffer[i];
        }

        return null;
    }

    private void UpdateButtonStates()
    {
        for (int i = 0; i < _modeButtons.Count; i++)
            _modeButtons[i].SetInteractable(_modeBuffer[i] != _selectedMode);
        for (int i = 0; i < _mapButtons.Count; i++)
            _mapButtons[i].SetInteractable(_mapBuffer[i] != _selectedMap);
    }

    private static void ClearButtons(List<PracticeSelectionButton> buttons)
    {
        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i] != null)
                Destroy(buttons[i].gameObject);
        }

        buttons.Clear();
    }
}
// CustomRoomMapSelectionUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
