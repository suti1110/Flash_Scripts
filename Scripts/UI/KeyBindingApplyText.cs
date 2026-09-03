using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class KeyBindingApplyText : MonoBehaviour
{
    [SerializeField]
    private InputActionReference _targetAction;

    [SerializeField]
    private int _rebindIndex;

    [SerializeField]
    private TMP_Text _tmpText;

    private void OnEnable()
    {
        _tmpText.text = _targetAction.action.GetBindingDisplayString(_rebindIndex);
    }

    private void OnValidate()
    {
        if (_targetAction == null)
        {
            Debug.LogError("TargetAction이 할당되지 않았습니다!!!", this);
        }

        if (_tmpText == null)
        {
            Debug.LogError("TmpText가 할당되지 않았습니다!!!", this);
            return;
        }

        _tmpText.text = _targetAction.action.GetBindingDisplayString(_rebindIndex);
    }
}
// KeyBindingApplyText은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
