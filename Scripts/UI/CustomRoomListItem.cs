using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CustomRoomListItem : MonoBehaviour
{
    [SerializeField]
    private Button _button;

    [SerializeField]
    private TMP_Text _roomNameText;

    [SerializeField]
    private TMP_Text _playerCountText;

    [SerializeField]
    private GameObject _passwordIcon;

    [SerializeField]
    private TMP_Text _settingsText;

    private CustomRoomSummary _room;
    private Action<CustomRoomSummary> _selected;

    private void Awake()
    {
        if (_button != null)
            _button.onClick.AddListener(Select);
    }

    public void Bind(CustomRoomSummary room, Action<CustomRoomSummary> selected)
    {
        _room = room;
        _selected = selected;

        if (_roomNameText != null)
            _roomNameText.text = room.RoomName;
        if (_playerCountText != null)
            _playerCountText.SetText("{0}/{1}", room.PlayerCount, room.MaxPlayers);
        if (_passwordIcon != null)
            _passwordIcon.SetActive(room.HasPassword);
        if (_settingsText != null)
        {
            _settingsText.SetText(
                "Gravity {0:0.00}x  Jump {1:0.00}x\nATK {2}  HP {3}",
                room.Settings.GravityMultiplier,
                room.Settings.JumpForceMultiplier,
                room.Settings.AttackDamage,
                room.Settings.MaximumHealth
            );
        }

        gameObject.SetActive(true);
    }

    public void Clear()
    {
        _room = null;
        _selected = null;
        gameObject.SetActive(false);
    }

    private void Select()
    {
        if (_room != null)
        {
            AudioManager.SfxPlay(AudioManager.Instance?.Container?.MouseClick);
            _selected?.Invoke(_room);
        }
    }

    private void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(Select);
    }
}
// CustomRoomListItem은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
