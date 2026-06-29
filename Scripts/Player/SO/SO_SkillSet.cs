using UnityEngine;

[CreateAssetMenu(fileName = "SkillSet", menuName = "Player/SkillSet")]
public class SO_SkillSet : ScriptableObject
{
    [field: SerializeField]
    public SO_Skill[] Skills { get; private set; }

    private void OnValidate()
    {
        if (Skills == null || Skills.Length != 2 || Skills[0] == null || Skills[1] == null)
        {
            EditorLog.LogError("SkillSet의 Skills 배열은 2개여야 합니다!!!", this);
        }
    }
}
