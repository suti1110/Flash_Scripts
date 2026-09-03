using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class NetworkLoadingPanel : MonoBehaviour
{
    private sealed class RequestScope : IDisposable
    {
        private NetworkLoadingPanel _owner;
        private readonly int _requestId;

        public RequestScope(NetworkLoadingPanel owner, int requestId)
        {
            _owner = owner;
            _requestId = requestId;
        }

        public void Dispose()
        {
            if (_owner == null)
                return;

            _owner.EndRequest(_requestId);
            _owner = null;
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose() { }
    }

    public static NetworkLoadingPanel Instance { get; private set; }

    [SerializeField]
    private GameObject _panel;

    [SerializeField]
    private TMP_Text _messageText;

    [SerializeField, Min(0.1f)]
    private float _dotInterval = 0.35f;

    [SerializeField, Min(0f)]
    private float _minimumVisibleDuration = 0.25f;

    private readonly Dictionary<int, string> _requests = new();
    private int _nextRequestId;
    private int _displayedRequestId;
    private int _dotCount = 1;
    private float _nextDotTime;
    private float _visibleSince;
    private bool _hidePending;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (_panel != null)
            _panel.SetActive(false);
    }

    private void Update()
    {
        if (_requests.Count == 0)
        {
            if (
                _hidePending
                && Time.unscaledTime >= _visibleSince + _minimumVisibleDuration
            )
            {
                HidePanel();
            }

            return;
        }

        if (Time.unscaledTime < _nextDotTime)
            return;

        _dotCount = _dotCount % 3 + 1;
        _nextDotTime = Time.unscaledTime + _dotInterval;
        RefreshMessage();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static IDisposable Begin(string message)
    {
        return Instance != null
            ? Instance.BeginRequest(message)
            : EmptyScope.Instance;
    }

    private IDisposable BeginRequest(string message)
    {
        int requestId = ++_nextRequestId;
        _requests.Add(requestId, NormalizeMessage(message));
        _displayedRequestId = requestId;
        _dotCount = 1;
        _nextDotTime = Time.unscaledTime + _dotInterval;
        _hidePending = false;

        if (_panel != null && !_panel.activeSelf)
        {
            _panel.SetActive(true);
            _visibleSince = Time.unscaledTime;
        }

        RefreshMessage();
        return new RequestScope(this, requestId);
    }

    private void EndRequest(int requestId)
    {
        if (!_requests.Remove(requestId))
            return;

        if (_requests.Count == 0)
        {
            _hidePending = true;
            return;
        }

        if (_displayedRequestId != requestId)
            return;

        _displayedRequestId = GetNewestRequestId();
        _dotCount = 1;
        _nextDotTime = Time.unscaledTime + _dotInterval;
        RefreshMessage();
    }

    private int GetNewestRequestId()
    {
        int newestRequestId = 0;
        foreach (int requestId in _requests.Keys)
            newestRequestId = Mathf.Max(newestRequestId, requestId);

        return newestRequestId;
    }

    private void RefreshMessage()
    {
        if (
            _messageText == null
            || !_requests.TryGetValue(_displayedRequestId, out string message)
        )
            return;

        _messageText.text = message + new string('.', _dotCount);
    }

    private void HidePanel()
    {
        _hidePending = false;
        _displayedRequestId = 0;
        if (_panel != null)
            _panel.SetActive(false);
    }

    private static string NormalizeMessage(string message)
    {
        string normalized = string.IsNullOrWhiteSpace(message)
            ? "Please wait"
            : message.Trim();
        return normalized.TrimEnd('.', ' ');
    }
}
// NetworkLoadingPanel은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
