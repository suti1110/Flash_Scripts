using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TooltipOnUI : MonoBehaviour
{
    private static TooltipOnUI _instance;
    private static TooltipOnUI Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<TooltipOnUI>();

                if (_instance == null)
                {
                    EditorLog.LogError("TooltipOnUI가 Scene에 없습니다!!!");
                }
            }
            return _instance;
        }
    }

    [SerializeField]
    private Canvas _canvas;

    [SerializeField]
    private RectTransform _tooltipPanel;

    [SerializeField]
    private TMP_Text _tooltipTitle;

    [SerializeField]
    private TMP_Text _tooltipContent;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        _tooltipPanel.gameObject.SetActive(false);
    }

    public static void OnTooltip(string title, string content)
    {
        Instance._tooltipTitle.SetText(title);
        Instance._tooltipContent.SetText(content);
        Instance._tooltipPanel.gameObject.SetActive(true);
    }

    public static void TooltipTracking(Vector2 pos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            Instance._tooltipPanel.parent as RectTransform,
            pos,
            Instance._canvas.worldCamera,
            out Vector2 localPoint
        );

        int sign = -localPoint.y.CompareTo(0); // 툴팁이 화면 밖으로 나가서 보이지 않게 되는 현상 방지

        Instance._tooltipPanel.anchoredPosition =
            localPoint + new Vector2(0, Instance._tooltipPanel.sizeDelta.y / 2 * sign);
    }

    public static void OffTooltip()
    {
        Instance._tooltipPanel.gameObject.SetActive(false);
    }
}
// TooltipOnUI은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
