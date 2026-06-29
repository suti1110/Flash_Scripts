using UnityEngine;
using UnityEngine.UI;

public enum SkillSet
{
    First,
    Second,
}

public interface ISkillGetter
{
    SO_Skill GetSkill(SkillSet skillSet);
}

public class SkillPreview : MonoBehaviour, ISkillGetter
{
    [SerializeField]
    private SO_Skill[] _allSkills;

    [SerializeField]
    private SO_SkillSet _skillSet;

    [SerializeField]
    private Image[] _skillIcons = new Image[2];

    private readonly int[] _currentIndex = new int[2] { 0, 0 };

    private void Awake()
    {
        UpdateSkillIcons();
    }

    public void IncreaseIndex(int index)
    {
        _currentIndex[index] = (_currentIndex[index] + 1) % _allSkills.Length;

        _skillSet.Skills[index] = _allSkills[_currentIndex[index]];

        UpdateSkillIcons();
    }

    public void DecreaseIndex(int index)
    {
        _currentIndex[index] = (_currentIndex[index] - 1 + _allSkills.Length) % _allSkills.Length;

        _skillSet.Skills[index] = _allSkills[_currentIndex[index]];

        UpdateSkillIcons();
    }

    private void UpdateSkillIcons()
    {
        for (int i = 0; i < _skillIcons.Length; i++)
        {
            _skillIcons[i].sprite = _skillSet.Skills[i].SkillIcon;
        }
    }

    public SO_Skill GetSkill(SkillSet skillSet) => _allSkills[_currentIndex[(int)skillSet]];

    private void OnValidate()
    {
        if (!_skillSet)
            EditorLog.LogError("SkillSet이 할당되지 않았습니다!!!", this);

        if (
            _skillIcons == null
            || _skillIcons.Length != 2
            || _skillIcons[0] == null
            || _skillIcons[1] == null
        )
        {
            EditorLog.LogError("SkillIcons 배열은 2개의 Image를 가져야 합니다!!!", this);
        }
    }
}
