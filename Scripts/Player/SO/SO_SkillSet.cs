using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SkillSet", menuName = "Player/SkillSet")]
public class SO_SkillSet : ScriptableObject
{
    [field: SerializeField]
    public SO_Skill[] AvailableSkills { get; private set; }

    [field: SerializeField]
    public SO_Skill[] Skills { get; private set; }

    public int AvailableSkillCount => SkillCatalog?.Length ?? 0;
    public int DefaultSkillCount => Skills?.Length ?? 0;

    private SO_Skill[] SkillCatalog =>
        AvailableSkills != null && AvailableSkills.Length > 0 ? AvailableSkills : Skills;

    public SO_Skill GetAvailableSkill(int catalogIndex)
    {
        SO_Skill[] catalog = SkillCatalog;
        return catalog != null && catalogIndex >= 0 && catalogIndex < catalog.Length
            ? catalog[catalogIndex]
            : null;
    }

    public int GetCatalogIndex(SO_Skill skill)
    {
        SO_Skill[] catalog = SkillCatalog;
        return skill == null || catalog == null ? -1 : Array.IndexOf(catalog, skill);
    }

    public int GetSkillNetworkId(SO_Skill skill)
    {
        return skill != null && GetCatalogIndex(skill) >= 0 ? skill.NetworkId : -1;
    }

    public SO_Skill GetSkillByNetworkId(int networkId)
    {
        SO_Skill[] catalog = SkillCatalog;
        if (catalog == null || networkId <= 0)
            return null;

        return Array.Find(catalog, skill => skill != null && skill.NetworkId == networkId);
    }

    public SO_Skill GetDefaultSkill(int slotIndex)
    {
        return Skills != null && slotIndex >= 0 && slotIndex < Skills.Length
            ? Skills[slotIndex]
            : null;
    }

    private void OnValidate()
    {
        if (AvailableSkills == null || AvailableSkills.Length == 0)
        {
            EditorLog.LogError("AvailableSkills must contain every selectable skill.", this);
        }

        if (Skills == null || Skills.Length == 0 || Array.Exists(Skills, skill => skill == null))
        {
            EditorLog.LogError("Skills must contain a valid default skill for every slot.", this);
        }

        SO_Skill[] catalog = SkillCatalog;
        if (catalog == null)
            return;

        HashSet<SO_Skill> defaultSkills = new();
        if (Skills != null)
        {
            foreach (SO_Skill skill in Skills)
            {
                if (skill == null)
                    continue;

                if (!defaultSkills.Add(skill))
                {
                    EditorLog.LogError(
                        $"Default skills cannot contain duplicates: {skill.name}",
                        this
                    );
                }

                if (Array.IndexOf(catalog, skill) < 0)
                {
                    EditorLog.LogError(
                        $"Default skill must also exist in AvailableSkills: {skill.name}",
                        this
                    );
                }
            }
        }

        HashSet<int> networkIds = new();
        foreach (SO_Skill skill in catalog)
        {
            if (skill == null)
            {
                EditorLog.LogError("AvailableSkills cannot contain null entries.", this);
                continue;
            }

            if (skill.NetworkId <= 0 || !networkIds.Add(skill.NetworkId))
            {
                EditorLog.LogError(
                    $"Skill NetworkId must be positive and unique: {skill.name} ({skill.NetworkId})",
                    this
                );
            }
        }
    }
}
// SO_SkillSet은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
