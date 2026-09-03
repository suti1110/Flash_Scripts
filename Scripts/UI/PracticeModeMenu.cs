using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PracticeModeMenu : MonoBehaviour
{
    [Header("데이터")]
    [SerializeField, InspectorName("맵 카탈로그")]
    private SO_MapCatalog _mapCatalog;

    [SerializeField, InspectorName("기본 맵 이미지")]
    private Sprite _defaultMapThumbnail;

    [Header("모드 목록")]
    [SerializeField, InspectorName("모드 버튼 프리팹")]
    private PracticeSelectionButton _modeButtonPrefab;

    [SerializeField, InspectorName("모드 버튼 부모")]
    private Transform _modeButtonContainer;

    [Header("맵 목록")]
    [SerializeField, InspectorName("맵 버튼 프리팹")]
    private PracticeSelectionButton _mapButtonPrefab;

    [SerializeField, InspectorName("맵 버튼 부모")]
    private Transform _mapButtonContainer;

    private readonly List<SO_GameModeDefinition> _modeBuffer = new();
    private readonly List<SO_MapDefinition> _mapBuffer = new();
    private readonly List<PracticeSelectionButton> _modeButtons = new();
    private readonly List<PracticeSelectionButton> _mapButtons = new();

    private SO_GameModeDefinition _selectedMode;
    private bool _isLoading;

    private void OnEnable()
    {
        Rebuild();
    }

    public void Rebuild()
    {
        ClearButtons(_modeButtons);
        ClearButtons(_mapButtons);
        _selectedMode = null;
        _isLoading = false;

        if (
            _mapCatalog == null
            || _modeButtonPrefab == null
            || _mapButtonPrefab == null
            || _modeButtonContainer == null
            || _mapButtonContainer == null
        )
        {
            Debug.LogError("PracticeModeMenu의 데이터 또는 UI 참조가 설정되지 않았습니다.", this);
            return;
        }

        _mapCatalog.CollectPracticeModes(_modeBuffer);
        for (int i = 0; i < _modeBuffer.Count; i++)
        {
            SO_GameModeDefinition mode = _modeBuffer[i];
            PracticeSelectionButton button = Instantiate(_modeButtonPrefab, _modeButtonContainer);
            button.Bind(mode.DisplayName, mode.Icon, () => SelectMode(mode));
            _modeButtons.Add(button);
        }

        if (_modeBuffer.Count > 0)
            SelectMode(_modeBuffer[0]);
    }

    private void SelectMode(SO_GameModeDefinition mode)
    {
        if (_isLoading || mode == null)
            return;

        _selectedMode = mode;
        ClearButtons(_mapButtons);
        _mapCatalog.CollectPracticeMaps(mode, _mapBuffer);

        for (int i = 0; i < _mapBuffer.Count; i++)
        {
            SO_MapDefinition map = _mapBuffer[i];
            Sprite thumbnail = map.Thumbnail != null ? map.Thumbnail : _defaultMapThumbnail;
            PracticeSelectionButton button = Instantiate(_mapButtonPrefab, _mapButtonContainer);
            button.Bind(map.DisplayName, thumbnail, () => StartPracticeMode(map));
            button.SetInteractable(map.HasScene);
            _mapButtons.Add(button);
        }

        UpdateModeButtonState();
    }

    private async void StartPracticeMode(SO_MapDefinition map)
    {
        if (_isLoading || map == null)
            return;

        _isLoading = true;
        SetAllButtonsInteractable(false);
        using IDisposable loading = NetworkLoadingPanel.Begin("Starting practice mode");

        try
        {
            if (RelayManager.Instance == null)
                throw new InvalidOperationException("RelayManager.Instance가 없습니다.");

            await RelayManager.Instance.StartPracticeModeAsync(map);
        }
        catch (Exception exception)
        {
            _isLoading = false;
            UpdateModeButtonState();
            for (int i = 0; i < _mapButtons.Count; i++)
                _mapButtons[i].SetInteractable(_mapBuffer[i].HasScene);

            EditorLog.LogError($"연습 모드 시작 실패: {exception}");
        }
    }

    private void UpdateModeButtonState()
    {
        for (int i = 0; i < _modeButtons.Count; i++)
        {
            bool isSelected = i < _modeBuffer.Count && _modeBuffer[i] == _selectedMode;
            _modeButtons[i].SetInteractable(!_isLoading && !isSelected);
        }
    }

    private void SetAllButtonsInteractable(bool interactable)
    {
        for (int i = 0; i < _modeButtons.Count; i++)
            _modeButtons[i].SetInteractable(interactable);
        for (int i = 0; i < _mapButtons.Count; i++)
            _mapButtons[i].SetInteractable(interactable);
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
// PracticeModeMenu은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
