using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PracticeExitConfirmation : MonoBehaviour
{
    [Header("Input")]
    [SerializeField]
    private InputActionReference _escapeAction;

    [Header("Popup")]
    [SerializeField]
    private PracticeExitDialog _exitCanvasPrefab;

    private InputAction _resolvedEscapeAction;
    private bool _enabledEscapeAction;
    private PracticeExitDialog _activeDialog;

    private void OnEnable()
    {
        if (_exitCanvasPrefab == null)
        {
            EditorLog.LogWarning(
                $"{nameof(PracticeExitConfirmation)} requires an ExitCanvas prefab."
            );
            return;
        }

        SubscribeEscapeAction();
    }

    private void OnDisable()
    {
        UnsubscribeEscapeAction();
    }

    private void SubscribeEscapeAction()
    {
        _resolvedEscapeAction = _escapeAction != null ? _escapeAction.action : null;
        if (_resolvedEscapeAction == null)
        {
            EditorLog.LogWarning("Practice exit input action is not assigned.");
            return;
        }

        _resolvedEscapeAction.performed += OnEscapePerformed;

        if (!_resolvedEscapeAction.enabled)
        {
            _resolvedEscapeAction.Enable();
            _enabledEscapeAction = true;
        }
    }

    private void UnsubscribeEscapeAction()
    {
        if (_resolvedEscapeAction == null)
            return;

        _resolvedEscapeAction.performed -= OnEscapePerformed;

        if (_enabledEscapeAction)
            _resolvedEscapeAction.Disable();

        _resolvedEscapeAction = null;
        _enabledEscapeAction = false;
    }

    private void OnEscapePerformed(InputAction.CallbackContext context)
    {
        RelayManager relayManager = RelayManager.Instance;
        if (relayManager == null || !relayManager.IsPracticeMode || _activeDialog != null)
            return;

        _activeDialog = Instantiate(_exitCanvasPrefab);
        _activeDialog.gameObject.SetActive(true);
    }
}
// PracticeExitConfirmation은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
