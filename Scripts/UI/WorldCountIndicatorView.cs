using TMPro;
using UnityEngine;

// 월드 공간에서 아이콘과 정수 카운트를 함께 표시하는 범용 뷰다.
// 어떤 게임 규칙의 수치인지 판단하지 않고 전달받은 값만 표현한다.
public sealed class WorldCountIndicatorView : MonoBehaviour
{
    [SerializeField] private GameObject _contentRoot;
    [SerializeField] private TMP_Text _countText;

    private void Awake()
    {
        Hide();
    }

    public void Show(int count)
    {
        if (_countText == null || _contentRoot == null)
            return;

        _countText.SetText("{0}", Mathf.Max(0, count));
        _contentRoot.SetActive(true);
    }

    public void Hide()
    {
        if (_contentRoot != null)
            _contentRoot.SetActive(false);
    }

    private void OnValidate()
    {
        if (_contentRoot == null)
            EditorLog.LogError("월드 카운트 표시의 Content Root가 지정되지 않았습니다.", this);

        if (_countText == null)
            EditorLog.LogError("월드 카운트 표시의 TextMeshPro 참조가 지정되지 않았습니다.", this);
    }
}
