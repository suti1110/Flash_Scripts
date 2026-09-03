using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class KeyBindingManager : MonoBehaviour
{
    [SerializeField]
    private InputActionReference _targetAction;

    [SerializeField]
    private int _rebindIndex;

    [SerializeField]
    private TMP_Text _bindingText;

    [SerializeField]
    private Button _rebindButton;

    private InputActionRebindingExtensions.RebindingOperation _rebindingOperation;

    private void Awake()
    {
        UpdateBindingText();
        _rebindButton.onClick.AddListener(StartRebinding);
    }

    private void StartRebinding()
    {
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        _rebindButton.interactable = false;
        _bindingText.text = "Press any key...";

        _targetAction.action.Disable();

        _rebindingOperation = _targetAction
            .action.PerformInteractiveRebinding()
            // _rebindIndex로 Axis와 같은 키 처리
            .WithTargetBinding(_rebindIndex)
            // 특정 기기 입력 무시 (예: 마우스 이동으로 키가 바뀌는 것 방지)
            .WithControlsExcluding("<Mouse>/position")
            .WithControlsExcluding("<Mouse>/delta")
            // 키를 눌렀을 때(완료) 실행할 콜백
            .OnComplete(operation => FinishRebinding())
            // 취소했을 때 실행할 콜백
            .OnCancel(operation => FinishRebinding())
            .Start();
    }

    private void FinishRebinding()
    {
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.Notification);
        UpdateBindingText();
        _targetAction.action.Enable();
        _rebindButton.interactable = true;

        _rebindingOperation.Dispose();
    }

    private void UpdateBindingText()
    {
        _bindingText.text = _targetAction.action.GetBindingDisplayString(_rebindIndex);
    }

    private void OnDisable()
    {
        _rebindingOperation?.Cancel();
    }

    private void OnDestroy()
    {
        // 스크립트가 파괴될 때 구독 해제 (C# 델리게이트 정석)
        _rebindButton.onClick.RemoveListener(StartRebinding);
    }
}
