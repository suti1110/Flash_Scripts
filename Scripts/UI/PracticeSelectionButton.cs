using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class PracticeSelectionButton : MonoBehaviour
{
    [SerializeField, InspectorName("버튼")]
    private Button _button;

    [SerializeField, InspectorName("이름 텍스트")]
    private TMP_Text _label;

    [SerializeField, InspectorName("대표 이미지")]
    private Image _thumbnail;

    private Action _onClick;

    private void Awake()
    {
        RegisterClickListener();
    }

    public void Bind(string label, Sprite thumbnail, Action onClick)
    {
        if (_label != null)
            _label.text = label;

        if (_thumbnail != null)
            _thumbnail.sprite = thumbnail;

        _onClick = onClick;
    }

    public void SetInteractable(bool interactable)
    {
        if (_button == null)
            _button = GetComponent<Button>();

        _button.interactable = interactable;
    }

    private void RegisterClickListener()
    {
        if (_button == null)
            _button = GetComponent<Button>();

        _button.onClick.RemoveListener(HandleClick);
        _button.onClick.AddListener(HandleClick);
    }

    private void HandleClick()
    {
        AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
        _onClick?.Invoke();
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleClick);
    }
}
// PracticeSelectionButton은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
