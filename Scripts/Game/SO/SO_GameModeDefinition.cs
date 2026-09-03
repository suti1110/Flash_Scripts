using UnityEngine;

[CreateAssetMenu(
    fileName = "GameModeDefinition",
    menuName = "ScriptableObjects/게임/게임 모드 정의"
)]
public sealed class SO_GameModeDefinition : ScriptableObject
{
    [SerializeField, InspectorName("게임 종류")]
    private GameKind _gameKind;

    [SerializeField, InspectorName("표시 이름")]
    private string _displayName;

    [SerializeField, InspectorName("모드 아이콘")]
    private Sprite _icon;

    [SerializeField, InspectorName("정렬 순서")]
    private int _sortOrder;

    [SerializeField, Min(1), InspectorName("온라인 선택 가중치")]
    private int _onlineSelectionWeight = 1;

    public string Id => name;
    public GameKind GameKind => _gameKind;
    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
    public Sprite Icon => _icon;
    public int SortOrder => _sortOrder;
    public int OnlineSelectionWeight => Mathf.Max(1, _onlineSelectionWeight);

    private void OnValidate()
    {
        _onlineSelectionWeight = Mathf.Max(1, _onlineSelectionWeight);
    }
}
// SO_GameModeDefinition은 인스펙터에서 조정하는 게임 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
