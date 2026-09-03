using UnityEngine;
using UnityEngine.UI;

public interface ISkillGetter
{
    SO_Skill GetSkill(int slotIndex);
}

public class SkillPreview : MonoBehaviour, ISkillGetter
{
    [SerializeField]
    private SO_SkillSet _skillSet;

    [SerializeField]
    private Image[] _skillIcons = new Image[2];

    private int[] _currentIndex;

    private void Awake()
    {
        PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
        loadout.InitializeSkills(_skillSet);
        _currentIndex = new int[loadout.SkillSlotCount];

        for (int i = 0; i < _currentIndex.Length; i++)
        {
            int catalogIndex = _skillSet.GetCatalogIndex(loadout.GetSkill(i, _skillSet));
            _currentIndex[i] = catalogIndex >= 0 ? catalogIndex : 0;
        }

        UpdateSkillIcons();
    }

    public void IncreaseIndex(int index)
    {
        ChangeSkill(index, 1);
    }

    public void DecreaseIndex(int index)
    {
        ChangeSkill(index, -1);
    }

    private void ChangeSkill(int slotIndex, int offset)
    {
        if (!IsValidSlot(slotIndex) || _skillSet.AvailableSkillCount == 0)
            return;

        PlayerLoadoutState loadout = PlayerLoadoutState.Instance;
        int candidateIndex = _currentIndex[slotIndex];

        for (int i = 0; i < _skillSet.AvailableSkillCount; i++)
        {
            candidateIndex = WrapIndex(candidateIndex + offset, _skillSet.AvailableSkillCount);
            SO_Skill candidate = _skillSet.GetAvailableSkill(candidateIndex);

            if (!loadout.TrySetSkill(slotIndex, candidate))
                continue;

            _currentIndex[slotIndex] = candidateIndex;
            UpdateSkillIcons();
            return;
        }
    }

    private static int WrapIndex(int index, int count)
    {
        int wrappedIndex = index % count;
        return wrappedIndex < 0 ? wrappedIndex + count : wrappedIndex;
    }

    private void UpdateSkillIcons()
    {
        for (int i = 0; i < _skillIcons.Length; i++)
        {
            SO_Skill skill = PlayerLoadoutState.Instance.GetSkill(i, _skillSet);
            _skillIcons[i].sprite = skill != null ? skill.SkillIcon : null;
        }
    }

    public SO_Skill GetSkill(int slotIndex) =>
        PlayerLoadoutState.Instance.GetSkill(slotIndex, _skillSet);

    private bool IsValidSlot(int index)
    {
        return _currentIndex != null && index >= 0 && index < _currentIndex.Length;
    }

    private void OnValidate()
    {
        if (!_skillSet)
            EditorLog.LogError("SkillSet이 할당되지 않았습니다!!!", this);

        if (
            _skillIcons == null
            || (_skillSet != null && _skillIcons.Length != _skillSet.DefaultSkillCount)
            || System.Array.Exists(_skillIcons, icon => icon == null)
        )
        {
            EditorLog.LogError("SkillIcons must match the default skill slot count.", this);
        }
    }
}
// SkillPreview은 게임 또는 네트워크 상태를 사용자에게 표시하고 UI 입력을 적절한 시스템으로 전달한다.
// UI가 핵심 게임 규칙을 직접 변경하지 않도록 표시 책임과 데이터 소유권을 분리한다.
