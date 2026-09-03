using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PlayerLoadoutState : MonoBehaviour
{
    private const string RuntimeObjectName = "Runtime";

    private static PlayerLoadoutState _instance;

    private SO_Skill[] _selectedSkills = Array.Empty<SO_Skill>();

    private int _skinIndex;
    private int _swordIndex;
    private bool _skinInitialized;
    private bool _swordInitialized;
    private bool _skillsInitialized;

    public static PlayerLoadoutState Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindOrCreateInstance();

            return _instance;
        }
    }

    public int SkinIndex => _skinIndex;
    public int SwordIndex => _swordIndex;
    public int SkillSlotCount => _selectedSkills.Length;

    public event Action<int> OnSkinChanged;
    public event Action<int> OnSwordChanged;
    public event Action<int, SO_Skill> OnSkillChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
    }

    private static PlayerLoadoutState FindOrCreateInstance()
    {
        PlayerLoadoutState state = FindAnyObjectByType<PlayerLoadoutState>(
            FindObjectsInactive.Include
        );

        if (state != null)
            return state;

        GameObject runtimeObject = GameObject.Find(RuntimeObjectName);
        if (runtimeObject == null)
            runtimeObject = new GameObject(RuntimeObjectName);

        return runtimeObject.AddComponent<PlayerLoadoutState>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
        gameObject.name = RuntimeObjectName;

        if (transform.parent != null)
            transform.SetParent(null);

        DontDestroyOnLoad(gameObject);
    }

    public void InitializeSkin(int defaultIndex, int optionCount)
    {
        if (_skinInitialized)
            return;

        _skinInitialized = true;
        _skinIndex = WrapIndex(defaultIndex, optionCount);
    }

    public void InitializeSword(int defaultIndex, int optionCount)
    {
        if (_swordInitialized)
            return;

        _swordInitialized = true;
        _swordIndex = WrapIndex(defaultIndex, optionCount);
    }

    public void InitializeSkills(SO_SkillSet defaultSkillSet)
    {
        if (_skillsInitialized || defaultSkillSet == null)
            return;

        _selectedSkills = new SO_Skill[defaultSkillSet.DefaultSkillCount];
        HashSet<SO_Skill> selectedSkills = new();

        for (int i = 0; i < _selectedSkills.Length; i++)
        {
            SO_Skill skill = defaultSkillSet.GetDefaultSkill(i);
            if (skill == null || selectedSkills.Contains(skill))
                skill = FindFirstUnequippedSkill(defaultSkillSet, selectedSkills);

            _selectedSkills[i] = skill;
            if (skill != null)
                selectedSkills.Add(skill);
        }

        _skillsInitialized = true;
    }

    public int ChangeSkinIndex(int offset, int optionCount)
    {
        SetSkinIndex(_skinIndex + offset, optionCount);
        return _skinIndex;
    }

    public int ChangeSwordIndex(int offset, int optionCount)
    {
        SetSwordIndex(_swordIndex + offset, optionCount);
        return _swordIndex;
    }

    public void SetSkinIndex(int index, int optionCount)
    {
        _skinInitialized = true;
        int newIndex = WrapIndex(index, optionCount);

        if (_skinIndex == newIndex)
            return;

        _skinIndex = newIndex;
        OnSkinChanged?.Invoke(_skinIndex);
    }

    public void SetSwordIndex(int index, int optionCount)
    {
        _swordInitialized = true;
        int newIndex = WrapIndex(index, optionCount);

        if (_swordIndex == newIndex)
            return;

        _swordIndex = newIndex;
        OnSwordChanged?.Invoke(_swordIndex);
    }

    public SO_Skill GetSkill(int slotIndex, SO_SkillSet defaultSkillSet)
    {
        InitializeSkills(defaultSkillSet);
        return IsValidSkillSlot(slotIndex) ? _selectedSkills[slotIndex] : null;
    }

    public void SetSkill(int slotIndex, SO_Skill skill)
    {
        TrySetSkill(slotIndex, skill);
    }

    public bool TrySetSkill(int slotIndex, SO_Skill skill)
    {
        if (
            !IsValidSkillSlot(slotIndex)
            || skill == null
            || IsEquippedInAnotherSlot(slotIndex, skill)
        )
        {
            return false;
        }

        _skillsInitialized = true;

        if (_selectedSkills[slotIndex] == skill)
            return true;

        _selectedSkills[slotIndex] = skill;
        OnSkillChanged?.Invoke(slotIndex, skill);
        return true;
    }

    private bool IsEquippedInAnotherSlot(int slotIndex, SO_Skill skill)
    {
        for (int i = 0; i < _selectedSkills.Length; i++)
        {
            if (i != slotIndex && _selectedSkills[i] == skill)
                return true;
        }

        return false;
    }

    private static SO_Skill FindFirstUnequippedSkill(
        SO_SkillSet skillSet,
        HashSet<SO_Skill> selectedSkills
    )
    {
        for (int i = 0; i < skillSet.AvailableSkillCount; i++)
        {
            SO_Skill skill = skillSet.GetAvailableSkill(i);
            if (skill != null && !selectedSkills.Contains(skill))
                return skill;
        }

        return null;
    }

    private bool IsValidSkillSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < _selectedSkills.Length;
    }

    private static int WrapIndex(int index, int optionCount)
    {
        if (optionCount <= 0)
            return 0;

        int wrappedIndex = index % optionCount;
        return wrappedIndex < 0 ? wrappedIndex + optionCount : wrappedIndex;
    }
}
// PlayerLoadoutState은 플레이어의 입력, 상태 또는 네트워크 표현 중 하나의 독립된 책임을 담당한다.
// 소유자 입력과 서버 판정의 경계를 유지하여 다른 플레이어 인스턴스에서 로직이 중복 실행되지 않도록 한다.
