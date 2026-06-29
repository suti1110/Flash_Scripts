using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SkillStatusUI : MonoBehaviour
{
    [SerializeField]
    [Tooltip("표시할 스킬 슬롯의 인덱스 (0: 첫 번째 스킬, 1: 두 번째 스킬)")]
    private int _skillSlotIndex;

    [SerializeField]
    private EnergyTracker _energyTracker;

    [SerializeField]
    private SO_SkillSet _skillSet;

    [SerializeField]
    private Image _skillIcon;

    [SerializeField]
    private TMP_Text _skillCostText;

    [Header("Colors")]
    [SerializeField]
    private Color _affordableColor = Color.white;

    [SerializeField]
    private Color _unaffordableColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

    private SO_Skill CurrentSkill =>
        _skillSet != null && _skillSlotIndex >= 0 && _skillSlotIndex < _skillSet.Skills.Length
            ? _skillSet.Skills[_skillSlotIndex]
            : null;

    private void Awake()
    {
        SO_Skill skill = CurrentSkill;

        if (skill == null)
        {
            if (_skillIcon != null)
                _skillIcon.color = Color.clear; // 스킬이 없으면 투명하게
            if (_skillCostText != null)
                _skillCostText.text = string.Empty;
            return;
        }

        if (_skillIcon != null)
        {
            _skillIcon.sprite = skill.SkillIcon;
        }

        if (_skillCostText != null)
        {
            _skillCostText.SetText("{0:0}", skill.EnergyCost);
        }

        _energyTracker.OnEnergyChanged += UpdateSkillStatus;
    }

    private void UpdateSkillStatus(float energy)
    {
        SO_Skill skill = CurrentSkill;

        if (_skillIcon != null)
        {
            // 기력 검사 후 아이콘 밝기 조절
            if (_energyTracker != null)
            {
                bool canAfford = energy >= skill.EnergyCost;
                _skillIcon.color = canAfford ? _affordableColor : _unaffordableColor;
            }
            else
            {
                _skillIcon.color = _affordableColor;
            }
        }
    }

    private void OnValidate()
    {
        if (_energyTracker == null)
            EditorLog.LogError("EnergyTracker가 할당되지 않았습니다!!!", this);

        if (_skillSet == null)
            EditorLog.LogError("SkillSet이 할당되지 않았습니다!!!", this);

        if (_skillIcon == null)
            EditorLog.LogError("SkillIcon (Image)이 할당되지 않았습니다!!!", this);

        if (_skillCostText == null)
            EditorLog.LogError("SkillCostText (TMP_Text)가 할당되지 않았습니다!!!", this);
    }
}
