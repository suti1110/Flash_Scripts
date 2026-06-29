using UnityEngine;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField]
    private PlayerDamage _playerDamage;

    [SerializeField]
    private GameObject _canvas;

    [SerializeField]
    private GameObject[] _hearts; // 인스펙터에서 하트 이미지들을 순서대로 넣을 배열

    [SerializeField]
    private SO_GameModeFilter _visibilityFilter;

    private void Awake()
    {
        WaitAction.WaitUntil(
            () => GameManager.Instance.GameKind != GameKind.None,
            () =>
            {
                if (_visibilityFilter != null && !_visibilityFilter.IsAllowed(GameManager.Instance.GameKind))
                    enabled = false;
            },
            10f
        );
    }

    private void OnEnable()
    {
        // 핵심: 체력(Hp) 값이 변할 때마다 OnHpChanged 함수를 자동으로 실행하라는 네트워크 이벤트 구독!
        _playerDamage.Hp.OnValueChanged += OnHpChanged;

        // 처음 스폰되었을 때 초기 체력에 맞춰서 하트 세팅
        UpdateHearts(_playerDamage.Hp.Value);
    }

    private void OnDisable()
    {
        _canvas.SetActive(false);
        _playerDamage.Hp.OnValueChanged -= OnHpChanged;
    }

    private void OnHpChanged(int previousValue, int newValue)
    {
        if (_playerDamage.Hp.Value > 0)
            UpdateHearts(newValue);
        else
        {
            _canvas.SetActive(false);
        }
    }

    private void UpdateHearts(int currentHp)
    {
        // 배열에 등록된 하트 개수만큼 반복하면서 켜고 끄기
        for (int i = 0; i < _hearts.Length; i++)
        {
            // 인덱스(i)가 현재 체력보다 작으면 켜고, 크거나 같으면 끕니다.
            // 예: 체력이 2면 -> 0번 켜짐, 1번 켜짐, 2번 꺼짐
            _hearts[i].SetActive(i < currentHp);
        }
    }

    private void OnValidate()
    {
        if (_visibilityFilter == null)
            EditorLog.LogError("SO_GameModeFilter가 할당되지 않았습니다!", this);
    }
}
