using System.IO;
using UnityEngine;

[CreateAssetMenu(fileName = "MapDefinition", menuName = "ScriptableObjects/게임/맵 정의")]
public sealed class SO_MapDefinition : ScriptableObject
{
    [SerializeField, InspectorName("게임 모드")]
    private SO_GameModeDefinition _mode;

    [SerializeField, InspectorName("표시 이름")]
    private string _displayName;

    [SerializeField, InspectorName("대표 이미지")]
    private Sprite _thumbnail;

    [SerializeField, InspectorName("정렬 순서")]
    private int _sortOrder;

    [SerializeField, InspectorName("연습 모드에서 사용")]
    private bool _availableInPractice = true;

    [SerializeField, InspectorName("온라인 모드에서 사용")]
    private bool _availableInOnline = true;

    [SerializeField, Min(1), InspectorName("최소 플레이어 수")]
    private int _minimumPlayers = 1;

    [SerializeField, Min(1), InspectorName("최대 플레이어 수")]
    private int _maximumPlayers = 4;

    [SerializeField, Min(1), InspectorName("온라인 선택 가중치")]
    private int _onlineSelectionWeight = 1;

    [SerializeField, HideInInspector]
    private string _sceneName;

    [SerializeField, HideInInspector]
    private string _scenePath;

#if UNITY_EDITOR
    [SerializeField, InspectorName("게임 씬")]
    private UnityEditor.SceneAsset _scene;
#endif

    public SO_GameModeDefinition Mode => _mode;
    public string DisplayName => string.IsNullOrWhiteSpace(_displayName) ? name : _displayName;
    public Sprite Thumbnail => _thumbnail;
    public int SortOrder => _sortOrder;
    public bool AvailableInPractice => _availableInPractice;
    public bool AvailableInOnline => _availableInOnline;
    public int OnlineSelectionWeight => Mathf.Max(1, _onlineSelectionWeight);
    public string SceneName => _sceneName;
    public string ScenePath => _scenePath;
    public bool HasScene => !string.IsNullOrWhiteSpace(_sceneName);

    public bool SupportsPlayerCount(int playerCount)
    {
        return playerCount >= _minimumPlayers && playerCount <= _maximumPlayers;
    }

    private void OnValidate()
    {
        _minimumPlayers = Mathf.Max(1, _minimumPlayers);
        _maximumPlayers = Mathf.Max(_minimumPlayers, _maximumPlayers);
        _onlineSelectionWeight = Mathf.Max(1, _onlineSelectionWeight);

#if UNITY_EDITOR
        RefreshSceneMetadata();
#endif
    }

#if UNITY_EDITOR
    public bool RefreshSceneMetadata()
    {
        string scenePath =
            _scene == null ? string.Empty : UnityEditor.AssetDatabase.GetAssetPath(_scene);
        string sceneName = string.IsNullOrWhiteSpace(scenePath)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(scenePath);

        if (_scenePath == scenePath && _sceneName == sceneName)
            return false;

        _scenePath = scenePath;
        _sceneName = sceneName;
        return true;
    }
#endif
}
// SO_MapDefinition은 인스펙터에서 조정하는 게임 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
