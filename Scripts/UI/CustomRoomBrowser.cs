using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomRoomBrowser : MonoBehaviour
{
    private const int RoomsPerPage = 6;

    [Header("Room Grid")]
    [SerializeField]
    private CustomRoomListItem _roomItemPrefab;

    [SerializeField]
    private Transform _gridRoot;

    [SerializeField]
    private Button _previousPageButton;

    [SerializeField]
    private Button _nextPageButton;

    [SerializeField]
    private TMP_Text _pageText;

    [SerializeField]
    private GameObject _emptyRoomMessage;

    [Header("Join")]
    [SerializeField]
    private CustomRoomMenu _customRoomMenu;

    private readonly List<CustomRoomSummary> _rooms = new();
    private readonly List<CustomRoomListItem> _items = new();
    private int _pageIndex;
    private int _refreshVersion;

    private void OnEnable()
    {
        RefreshRooms();
    }

    private void OnDisable()
    {
        _refreshVersion++;
    }

    public async void RefreshRooms()
    {
        int version = ++_refreshVersion;
        using IDisposable loading = NetworkLoadingPanel.Begin("Loading rooms");

        try
        {
            RelayManager relayManager = RelayManager.Instance;
            if (relayManager == null)
                throw new InvalidOperationException("RelayManager is not available.");

            IReadOnlyList<CustomRoomSummary> rooms =
                await relayManager.GetPublicCustomRoomsAsync();
            if (version != _refreshVersion || this == null)
                return;

            _rooms.Clear();
            for (int i = 0; i < rooms.Count; i++)
                _rooms.Add(rooms[i]);

            _pageIndex = Mathf.Clamp(_pageIndex, 0, GetLastPageIndex());
            RenderPage();
        }
        catch (Exception exception)
        {
            if (version != _refreshVersion || this == null)
                return;

            _rooms.Clear();
            _pageIndex = 0;
            RenderPage();
            MessageOnUI.ShowMessage(exception.Message, MessageType.Warning);
            EditorLog.LogError($"Custom room search failed: {exception}");
        }
    }

    public void ShowPreviousPage()
    {
        if (_pageIndex <= 0)
            return;

        _pageIndex--;
        RenderPage();
    }

    public void ShowNextPage()
    {
        if (_pageIndex >= GetLastPageIndex())
            return;

        _pageIndex++;
        RenderPage();
    }

    private void RenderPage()
    {
        EnsureItemCount();

        int firstRoomIndex = _pageIndex * RoomsPerPage;
        for (int i = 0; i < _items.Count; i++)
        {
            int roomIndex = firstRoomIndex + i;
            if (roomIndex < _rooms.Count)
                _items[i].Bind(_rooms[roomIndex], SelectRoom);
            else
                _items[i].Clear();
        }

        int pageCount = Mathf.Max(1, GetLastPageIndex() + 1);
        if (_pageText != null)
            _pageText.SetText("{0}/{1}", _pageIndex + 1, pageCount);
        if (_previousPageButton != null)
            _previousPageButton.interactable = _pageIndex > 0;
        if (_nextPageButton != null)
            _nextPageButton.interactable = _pageIndex < GetLastPageIndex();
        if (_emptyRoomMessage != null)
            _emptyRoomMessage.SetActive(_rooms.Count == 0);
    }

    private void SelectRoom(CustomRoomSummary room)
    {
        if (_customRoomMenu == null)
            return;

        _customRoomMenu.RequestJoin(room);
    }

    private void EnsureItemCount()
    {
        if (_roomItemPrefab == null || _gridRoot == null)
            return;

        while (_items.Count < RoomsPerPage)
        {
            CustomRoomListItem item = Instantiate(_roomItemPrefab, _gridRoot);
            _items.Add(item);
        }
    }

    private int GetLastPageIndex()
    {
        return Mathf.Max(0, Mathf.CeilToInt(_rooms.Count / (float)RoomsPerPage) - 1);
    }
}
// CustomRoomBrowser은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
