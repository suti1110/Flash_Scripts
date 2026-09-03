using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

public sealed class CustomRoomMenu : MonoBehaviour
{
    [Header("Create Room")]
    [SerializeField]
    private GameObject _createRoomPanel;

    [SerializeField]
    private TMP_InputField _roomNameInput;

    [SerializeField]
    private TMP_InputField _createPasswordInput;

    [SerializeField]
    private Slider _maxPlayersSlider;

    [SerializeField]
    private TMP_Text _maxPlayersValueText;

    [SerializeField]
    private Toggle _publicRoomToggle;

    [Header("Join Room")]
    [SerializeField]
    private TMP_InputField _roomIdInput;

    [SerializeField]
    private GameObject _findRoomPanel;

    [Header("Password Entry")]
    [SerializeField]
    private GameObject _passwordPanel;

    [SerializeField]
    private TMP_InputField _joinPasswordInput;

    [Header("Room Settings")]
    [SerializeField]
    private Slider _gravitySlider;

    [SerializeField]
    private Slider _moveSpeedSlider;

    [SerializeField]
    private Slider _jumpForceSlider;

    [SerializeField]
    private Slider _attackDamageSlider;

    [SerializeField]
    private Slider _healthSlider;

    [SerializeField]
    private Slider _knockbackSlider;

    [SerializeField]
    private Slider _throwPowerSlider;

    [Header("Setting Values")]
    [SerializeField]
    private TMP_Text _gravityValueText;

    [SerializeField]
    private TMP_Text _moveSpeedValueText;

    [SerializeField]
    private TMP_Text _jumpForceValueText;

    [SerializeField]
    private TMP_Text _attackDamageValueText;

    [SerializeField]
    private TMP_Text _healthValueText;

    [SerializeField]
    private TMP_Text _knockbackValueText;

    [SerializeField]
    private TMP_Text _throwPowerValueText;

    [Header("Room Information")]
    [SerializeField]
    private TMP_Text _roomIdText;

    [Header("Waiting Room")]
    [SerializeField, FormerlySerializedAs("_hostOnlyButtons")]
    private Selectable[] _hostVisibleSelectables;

    [SerializeField]
    private Selectable[] _hostInteractableSelectables;

    [SerializeField]
    private UnityEvent _roomEntered;

    [SerializeField]
    private UnityEvent _roomLeft;

    private bool _operationInProgress;
    private string _pendingRoomId;
    private bool _returnToFindPanel;
    private RelayManager _observedRelayManager;

    private void Awake()
    {
        ConfigureSlider(_maxPlayersSlider, 2f, 4f, true);
        ConfigureSlider(
            _gravitySlider,
            CustomRoomSettings.MinimumGravityMultiplier,
            CustomRoomSettings.MaximumGravityMultiplier
        );
        ConfigureSlider(
            _moveSpeedSlider,
            CustomRoomSettings.MinimumMoveSpeedMultiplier,
            CustomRoomSettings.MaximumMoveSpeedMultiplier
        );
        ConfigureSlider(
            _jumpForceSlider,
            CustomRoomSettings.MinimumJumpForceMultiplier,
            CustomRoomSettings.MaximumJumpForceMultiplier
        );
        ConfigureSlider(
            _attackDamageSlider,
            CustomRoomSettings.MinimumAttackDamage,
            CustomRoomSettings.MaximumAttackDamage,
            true
        );
        ConfigureSlider(
            _healthSlider,
            CustomRoomSettings.MinimumHealthValue,
            CustomRoomSettings.MaximumHealthValue,
            true
        );
        ConfigureSlider(
            _knockbackSlider,
            CustomRoomSettings.MinimumKnockbackMultiplier,
            CustomRoomSettings.MaximumKnockbackMultiplier
        );
        ConfigureSlider(
            _throwPowerSlider,
            CustomRoomSettings.MinimumThrowPower,
            CustomRoomSettings.MaximumThrowPower,
            true
        );

        if (_createPasswordInput != null)
            _createPasswordInput.contentType = TMP_InputField.ContentType.Password;
        if (_joinPasswordInput != null)
            _joinPasswordInput.contentType = TMP_InputField.ContentType.Password;

        SetSliderValue(_maxPlayersSlider, 4f);
        SetSliders(CustomRoomSettings.Default);
        RefreshSettingLabels();
    }

    private void OnEnable()
    {
        AddSliderListener(_maxPlayersSlider);
        AddSliderListener(_gravitySlider);
        AddSliderListener(_moveSpeedSlider);
        AddSliderListener(_jumpForceSlider);
        AddSliderListener(_attackDamageSlider);
        AddSliderListener(_healthSlider);
        AddSliderListener(_knockbackSlider);
        AddSliderListener(_throwPowerSlider);

        RelayManager relayManager = RelayManager.Instance;
        if (relayManager != null && relayManager.IsCustomRoom)
        {
            SetRoomId(relayManager.CurrentRoomId);
            EnterWaitingRoom(relayManager);
        }
    }

    private void OnDisable()
    {
        RemoveSliderListener(_maxPlayersSlider);
        RemoveSliderListener(_gravitySlider);
        RemoveSliderListener(_moveSpeedSlider);
        RemoveSliderListener(_jumpForceSlider);
        RemoveSliderListener(_attackDamageSlider);
        RemoveSliderListener(_healthSlider);
        RemoveSliderListener(_knockbackSlider);
        RemoveSliderListener(_throwPowerSlider);
        UnbindRoomSettings();
    }

    public async void CreateRoom()
    {
        if (_operationInProgress)
            return;

        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        if (_maxPlayersSlider == null)
        {
            SetStatus("Max players slider is not assigned.", MessageType.Warning);
            return;
        }

        int maxPlayers = Mathf.RoundToInt(_maxPlayersSlider.value);
        string roomName = _roomNameInput != null ? _roomNameInput.text : string.Empty;
        string password = _createPasswordInput != null ? _createPasswordInput.text : string.Empty;

        await RunOperationAsync(
            () =>
                relayManager.CreateCustomRoomAsync(
                    roomName,
                    password,
                    maxPlayers,
                    CustomRoomSettings.Default,
                    _publicRoomToggle == null || _publicRoomToggle.isOn
                ),
            () =>
            {
                SetRoomId(relayManager.CurrentRoomId);
                SetSliders(relayManager.CurrentRoomSettings);
                RefreshSettingLabels();
                CancelCreateRoomEntry();
                SetStatus("Room created. Share the room ID with other players.");
                EnterWaitingRoom(relayManager);
            },
            "Creating room"
        );
    }

    public void CancelCreateRoomEntry()
    {
        if (_roomNameInput != null)
            _roomNameInput.SetTextWithoutNotify(string.Empty);
        if (_createPasswordInput != null)
            _createPasswordInput.SetTextWithoutNotify(string.Empty);
        if (_maxPlayersSlider != null)
            _maxPlayersSlider.SetValueWithoutNotify(4f);
        if (_publicRoomToggle != null)
            _publicRoomToggle.SetIsOnWithoutNotify(true);
        if (_createRoomPanel != null)
            _createRoomPanel.SetActive(false);

        RefreshSettingLabels();
    }

    public async void JoinRoom()
    {
        if (_operationInProgress)
            return;

        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        string roomId = _roomIdInput != null ? _roomIdInput.text : string.Empty;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("Room ID is required.", MessageType.Warning);
            return;
        }

        string normalizedRoomId = roomId.Trim();
        await RunOperationAsync(
            () => relayManager.JoinCustomRoomByIdAsync(normalizedRoomId, string.Empty),
            () => CompleteRoomJoin(relayManager),
            "Joining room",
            exception =>
            {
                if (
                    exception is NetworkConnectionRejectedException rejection
                    && string.Equals(
                        rejection.RejectionReason,
                        RelayManager.IncorrectRoomPasswordReason,
                        StringComparison.Ordinal
                    )
                )
                {
                    OpenPasswordPanel(normalizedRoomId, true);
                    return true;
                }

                return false;
            }
        );
    }

    public void RequestJoin(CustomRoomSummary room)
    {
        if (_operationInProgress || room == null)
            return;

        if (room.HasPassword)
        {
            OpenPasswordPanel(room.RoomId, false);
            return;
        }

        JoinRoomById(room.RoomId, string.Empty);
    }

    public void JoinPasswordProtectedRoom()
    {
        if (_operationInProgress)
            return;

        if (string.IsNullOrWhiteSpace(_pendingRoomId))
        {
            SetStatus("No password-protected room is selected.", MessageType.Warning);
            return;
        }

        string joinPassword = _joinPasswordInput != null ? _joinPasswordInput.text : string.Empty;
        JoinRoomById(_pendingRoomId, joinPassword);
    }

    public void CancelPasswordEntry()
    {
        ClosePasswordPanel(true);
    }

    public void CancelFindRoomEntry()
    {
        if (_roomIdInput != null)
            _roomIdInput.SetTextWithoutNotify(string.Empty);
        if (_findRoomPanel != null)
            _findRoomPanel.SetActive(false);
    }

    private void ClosePasswordPanel(bool restoreFindPanel)
    {
        _pendingRoomId = string.Empty;
        if (_joinPasswordInput != null)
            _joinPasswordInput.SetTextWithoutNotify(string.Empty);
        if (_passwordPanel != null)
            _passwordPanel.SetActive(false);
        if (restoreFindPanel && _returnToFindPanel && _findRoomPanel != null)
            _findRoomPanel.SetActive(true);

        _returnToFindPanel = false;
    }

    public async void LeaveRoom()
    {
        if (_operationInProgress)
            return;

        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        await RunOperationAsync(
            relayManager.LeaveLobby,
            null,
            "Leaving room"
        );
    }

    public async void ApplySettings()
    {
        if (_operationInProgress)
            return;

        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        await RunOperationAsync(
            () => relayManager.UpdateCustomRoomSettingsAsync(ReadSettings()),
            () => SetStatus("Room settings updated."),
            "Updating room settings"
        );
    }

    public async void StartRoom()
    {
        if (_operationInProgress)
            return;

        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        await RunOperationAsync(
            relayManager.StartCustomRoomGameAsync,
            null,
            null
        );
    }

    public void CopyRoomId()
    {
        string roomId = RelayManager.Instance != null ? RelayManager.Instance.CurrentRoomId : null;
        if (string.IsNullOrWhiteSpace(roomId))
        {
            SetStatus("There is no room ID to copy.", MessageType.Warning);
            return;
        }

        GUIUtility.systemCopyBuffer = roomId;
        SetStatus("Room ID copied.");
    }

    public void RefreshSettingLabels()
    {
        SetPlayerCountText(_maxPlayersValueText, _maxPlayersSlider);
        SetMultiplierText(_gravityValueText, _gravitySlider);
        SetMultiplierText(_moveSpeedValueText, _moveSpeedSlider);
        SetMultiplierText(_jumpForceValueText, _jumpForceSlider);
        SetIntegerText(_attackDamageValueText, _attackDamageSlider);
        SetIntegerText(_healthValueText, _healthSlider);
        SetMultiplierText(_knockbackValueText, _knockbackSlider);
        SetIntegerText(_throwPowerValueText, _throwPowerSlider);
    }

    public void ResetSettingsToDefault()
    {
        SetSliders(CustomRoomSettings.Default);
        RefreshSettingLabels();
        SetStatus("Settings reset to defaults. Apply to save.");
    }

    private async Task RunOperationAsync(
        Func<Task> operation,
        Action onSuccess,
        string loadingMessage,
        Func<Exception, bool> handleException = null
    )
    {
        _operationInProgress = true;
        using IDisposable loading = string.IsNullOrWhiteSpace(loadingMessage)
            ? null
            : NetworkLoadingPanel.Begin(loadingMessage);

        try
        {
            await operation();
            onSuccess?.Invoke();
        }
        catch (Exception exception)
        {
            if (handleException?.Invoke(exception) != true)
            {
                SetStatus(exception.Message, MessageType.Warning);
                EditorLog.LogError($"Custom room operation failed: {exception}");
            }
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private CustomRoomSettings ReadSettings()
    {
        return new CustomRoomSettings(
            GetSliderValue(_gravitySlider),
            GetSliderValue(_moveSpeedSlider),
            GetSliderValue(_jumpForceSlider),
            Mathf.RoundToInt(GetSliderValue(_attackDamageSlider)),
            Mathf.RoundToInt(GetSliderValue(_healthSlider)),
            GetSliderValue(_knockbackSlider),
            Mathf.RoundToInt(
                GetSliderValue(_throwPowerSlider, CustomRoomSettings.DefaultThrowPower)
            )
        );
    }

    private void SetSliders(CustomRoomSettings settings)
    {
        SetSliderValue(_gravitySlider, settings.GravityMultiplier);
        SetSliderValue(_moveSpeedSlider, settings.MoveSpeedMultiplier);
        SetSliderValue(_jumpForceSlider, settings.JumpForceMultiplier);
        SetSliderValue(_attackDamageSlider, settings.AttackDamage);
        SetSliderValue(_healthSlider, settings.MaximumHealth);
        SetSliderValue(_knockbackSlider, settings.KnockbackMultiplier);
        SetSliderValue(_throwPowerSlider, settings.ThrowPower);
    }

    private RelayManager RequireRelayManager()
    {
        if (RelayManager.Instance != null)
            return RelayManager.Instance;

        SetStatus("RelayManager is not available.", MessageType.Warning);
        return null;
    }

    private async void JoinRoomById(string roomId, string password)
    {
        RelayManager relayManager = RequireRelayManager();
        if (relayManager == null)
            return;

        await RunOperationAsync(
            () => relayManager.JoinCustomRoomByIdAsync(roomId, password),
            () => CompleteRoomJoin(relayManager),
            "Joining room"
        );
    }

    private void CompleteRoomJoin(RelayManager relayManager)
    {
        ClosePasswordPanel(false);
        CancelFindRoomEntry();
        SetRoomId(relayManager.CurrentRoomId);
        SetSliders(relayManager.CurrentRoomSettings);
        RefreshSettingLabels();
        SetStatus("Joined the custom room.");
        EnterWaitingRoom(relayManager);
    }

    private void OpenPasswordPanel(string roomId, bool returnToFindPanel)
    {
        _pendingRoomId = roomId;
        _returnToFindPanel = returnToFindPanel;
        if (_joinPasswordInput != null)
            _joinPasswordInput.SetTextWithoutNotify(string.Empty);
        if (_findRoomPanel != null)
            _findRoomPanel.SetActive(false);

        if (_passwordPanel != null)
            _passwordPanel.SetActive(true);
        else
            SetStatus("Password panel is not assigned.", MessageType.Warning);
    }

    private void EnterWaitingRoom(RelayManager relayManager)
    {
        SetHostOnlyControls(relayManager.IsHost);
        BindRoomSettings(relayManager);
        HandleRoomSettingsChanged(relayManager.CurrentRoomSettings);
        _roomEntered?.Invoke();
    }

    private void SetHostOnlyControls(bool isHost)
    {
        SetSelectablesVisible(_hostVisibleSelectables, isHost);
        SetSelectablesInteractable(_hostInteractableSelectables, isHost);
    }

    private static void SetSelectablesVisible(Selectable[] selectables, bool isVisible)
    {
        if (selectables == null)
            return;

        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i] != null)
                selectables[i].gameObject.SetActive(isVisible);
        }
    }

    private static void SetSelectablesInteractable(
        Selectable[] selectables,
        bool isInteractable
    )
    {
        if (selectables == null)
            return;

        for (int i = 0; i < selectables.Length; i++)
        {
            if (selectables[i] != null)
                selectables[i].interactable = isInteractable;
        }
    }

    private void BindRoomSettings(RelayManager relayManager)
    {
        if (_observedRelayManager == relayManager)
            return;

        UnbindRoomSettings();
        _observedRelayManager = relayManager;
        _observedRelayManager.RoomSettingsChanged += HandleRoomSettingsChanged;
        _observedRelayManager.CustomRoomLeft += HandleCustomRoomLeft;
    }

    private void UnbindRoomSettings()
    {
        if (_observedRelayManager != null)
        {
            _observedRelayManager.RoomSettingsChanged -= HandleRoomSettingsChanged;
            _observedRelayManager.CustomRoomLeft -= HandleCustomRoomLeft;
        }

        _observedRelayManager = null;
    }

    private void HandleRoomSettingsChanged(CustomRoomSettings settings)
    {
        SetSliders(settings);
        RefreshSettingLabels();
    }

    private void HandleCustomRoomLeft()
    {
        SetRoomId(string.Empty);
        SetHostOnlyControls(false);
        UnbindRoomSettings();
        _roomLeft?.Invoke();
    }

    private static void SetStatus(string message, MessageType messageType = MessageType.Message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            MessageOnUI.ShowMessage(message, messageType);
    }

    private void SetRoomId(string roomId)
    {
        if (_roomIdText != null)
            _roomIdText.text = roomId;
    }

    private void HandleSliderChanged(float _)
    {
        RefreshSettingLabels();
    }

    private void AddSliderListener(Slider slider)
    {
        if (slider != null)
            slider.onValueChanged.AddListener(HandleSliderChanged);
    }

    private void RemoveSliderListener(Slider slider)
    {
        if (slider != null)
            slider.onValueChanged.RemoveListener(HandleSliderChanged);
    }

    private static void ConfigureSlider(
        Slider slider,
        float minimum,
        float maximum,
        bool wholeNumbers = false
    )
    {
        if (slider == null)
            return;

        slider.minValue = minimum;
        slider.maxValue = maximum;
        slider.wholeNumbers = wholeNumbers;
    }

    private static float GetSliderValue(Slider slider, float fallbackValue = 1f)
    {
        return slider != null ? slider.value : fallbackValue;
    }

    private static void SetSliderValue(Slider slider, float value)
    {
        if (slider != null)
            slider.SetValueWithoutNotify(value);
    }

    private static void SetMultiplierText(TMP_Text text, Slider slider)
    {
        if (text != null && slider != null)
            text.SetText("{0:0.00}x", slider.value);
    }

    private static void SetIntegerText(TMP_Text text, Slider slider)
    {
        if (text != null && slider != null)
            text.SetText("{0:0}", slider.value);
    }

    private static void SetPlayerCountText(TMP_Text text, Slider slider)
    {
        if (text != null && slider != null)
            text.SetText("{0:0} Players", slider.value);
    }

    private void OnValidate()
    {
        if (_throwPowerSlider == null)
            EditorLog.LogError("Custom Room의 던지기 파워 Slider가 연결되지 않았습니다.", this);

        if (_throwPowerValueText == null)
            EditorLog.LogError("Custom Room의 던지기 파워 값 Text가 연결되지 않았습니다.", this);
    }
}
// CustomRoomMenu은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
