using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class PracticeExitDialog : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField]
    private Button _yesButton;

    [SerializeField]
    private Button _noButton;

    [SerializeField]
    private Button _cancelButton;

    [Header("Navigation")]
    [SerializeField]
    private OnlyOneUnityString _mainMenuSceneName;

    private bool _isConfigured;
    private bool _isExiting;
    private bool _interactionStateCaptured;
    private float _previousTimeScale;
    private bool _previousAudioListenerPause;
    private CursorLockMode _previousCursorLockMode;
    private bool _previousCursorVisible;

    private void Awake()
    {
        _isConfigured = _yesButton != null && _noButton != null && _cancelButton != null;
        if (!_isConfigured)
        {
            EditorLog.LogError(
                $"{nameof(PracticeExitDialog)} requires Yes, No, and Cancel buttons."
            );
            return;
        }

        _yesButton.onClick.AddListener(ConfirmExit);
        _noButton.onClick.AddListener(CloseDialog);
        _cancelButton.onClick.AddListener(CloseDialog);
    }

    private void OnEnable()
    {
        if (!_isConfigured)
            return;

        CaptureInteractionState();
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(_noButton.gameObject);
        }
        else
        {
            EditorLog.LogWarning(
                $"{nameof(PracticeExitDialog)} needs an EventSystem for button interaction."
            );
        }
    }

    private void OnDisable()
    {
        ClearSelection();
        RestoreInteractionState();
    }

    private void OnDestroy()
    {
        if (_yesButton != null)
            _yesButton.onClick.RemoveListener(ConfirmExit);

        if (_noButton != null)
            _noButton.onClick.RemoveListener(CloseDialog);

        if (_cancelButton != null)
            _cancelButton.onClick.RemoveListener(CloseDialog);
    }

    private void CloseDialog()
    {
        if (_isExiting)
            return;

        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    private async void ConfirmExit()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        _yesButton.interactable = false;
        _noButton.interactable = false;
        _cancelButton.interactable = false;

        PrepareForMainMenu();
        gameObject.SetActive(false);
        if (GameManager.Instance != null)
            GameManager.Instance.SetGameKind(GameKind.None);

        using IDisposable loading = NetworkLoadingPanel.Begin("Leaving practice mode");

        try
        {
            RelayManager relayManager = RelayManager.Instance;
            if (relayManager != null)
                await relayManager.CloseSessionAsync(false);
            else if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
        }
        catch (Exception exception)
        {
            EditorLog.LogError($"Failed to close the practice session cleanly: {exception}");

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }

        SceneManager.LoadScene(_mainMenuSceneName);
    }

    private void CaptureInteractionState()
    {
        if (_interactionStateCaptured)
            return;

        _interactionStateCaptured = true;
        _previousTimeScale = Time.timeScale;
        _previousAudioListenerPause = AudioListener.pause;
        _previousCursorLockMode = Cursor.lockState;
        _previousCursorVisible = Cursor.visible;
    }

    private void RestoreInteractionState()
    {
        if (!_interactionStateCaptured)
            return;

        Time.timeScale = _previousTimeScale;
        AudioListener.pause = _previousAudioListenerPause;
        Cursor.lockState = _previousCursorLockMode;
        Cursor.visible = _previousCursorVisible;
        _interactionStateCaptured = false;
    }

    private void PrepareForMainMenu()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _interactionStateCaptured = false;
    }

    private static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }
}
// PracticeExitDialog은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
