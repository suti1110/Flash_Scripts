using System.Collections.Generic;
using UnityEngine;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField]
    private PlayerDamage _playerDamage;

    [SerializeField]
    private GameObject _canvas;

    [SerializeField]
    private GameObject _heartPrefab;

    [SerializeField]
    private RectTransform _heartRoot;

    [SerializeField]
    private Vector2 _layoutCenter = new(0f, 350f);

    [SerializeField, Min(1)]
    private int _heartsPerRow = 6;

    [SerializeField]
    private Vector2 _heartSpacing = new(75f, 65f);

    [SerializeField]
    private SO_GameModeFilter _visibilityFilter;

    private readonly List<GameObject> _hearts = new();

    private void Awake()
    {
        WaitAction.WaitUntil(
            () => GameManager.Instance != null && GameManager.Instance.GameKind != GameKind.None,
            () =>
            {
                GameManager gameManager = GameManager.Instance;
                if (gameManager == null)
                    return;

                if (
                    _visibilityFilter != null
                    && !_visibilityFilter.IsAllowed(gameManager.GameKind)
                )
                    enabled = false;
            },
            10f
        );
    }

    private void OnEnable()
    {
        EnsureHeartCapacity(_playerDamage.MaximumHealth);
        _playerDamage.OnHealthChanged += OnHpChanged;
        UpdateHearts(_playerDamage.CurrentHealth);
    }

    private void OnDisable()
    {
        if (_canvas != null)
            _canvas.SetActive(false);

        if (_playerDamage != null)
            _playerDamage.OnHealthChanged -= OnHpChanged;
    }

    private void OnHpChanged(int previousValue, int newValue)
    {
        if (_playerDamage.IsAlive)
        {
            if (_canvas != null)
                _canvas.SetActive(true);

            UpdateHearts(newValue);
        }
        else if (_canvas != null)
        {
            _canvas.SetActive(false);
        }
    }

    private void UpdateHearts(int currentHp)
    {
        for (int i = 0; i < _hearts.Count; i++)
            _hearts[i].SetActive(i < currentHp);
    }

    private void EnsureHeartCapacity(int requiredCount)
    {
        if (_heartPrefab == null || _heartRoot == null)
            return;

        while (_hearts.Count < requiredCount)
        {
            GameObject heart = Instantiate(_heartPrefab, _heartRoot);
            heart.name = $"Heart {_hearts.Count + 1}";
            _hearts.Add(heart);
        }

        LayoutHearts(requiredCount);
    }

    private void LayoutHearts(int displayedCapacity)
    {
        int heartsPerRow = Mathf.Max(1, _heartsPerRow);
        int heartCount = Mathf.Min(displayedCapacity, _hearts.Count);
        int rowCount = Mathf.CeilToInt(heartCount / (float)heartsPerRow);

        for (int i = 0; i < heartCount; i++)
        {
            if (!_hearts[i].TryGetComponent(out RectTransform heart))
                continue;

            int row = i / heartsPerRow;
            int column = i % heartsPerRow;
            int countInRow = Mathf.Min(heartsPerRow, heartCount - row * heartsPerRow);
            float centeredColumn = column - (countInRow - 1) * 0.5f;
            float centeredRow = row - (rowCount - 1) * 0.5f;

            heart.anchoredPosition =
                _layoutCenter
                + new Vector2(centeredColumn * _heartSpacing.x, -centeredRow * _heartSpacing.y);
        }
    }

    private void OnValidate()
    {
        if (_playerDamage == null)
            EditorLog.LogError("PlayerDamage가 할당되지 않았습니다!", this);
        if (_canvas == null)
            EditorLog.LogError("Canvas가 할당되지 않았습니다!", this);
        if (_heartPrefab == null)
            EditorLog.LogError("Heart Prefab이 할당되지 않았습니다!", this);
        if (_heartRoot == null)
            EditorLog.LogError("Heart Root가 할당되지 않았습니다!", this);
        if (_visibilityFilter == null)
            EditorLog.LogError("SO_GameModeFilter가 할당되지 않았습니다!", this);
    }
}
// PlayerHealthUI은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
