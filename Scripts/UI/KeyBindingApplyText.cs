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
