using UnityEngine;

/// <summary>
/// 문자열 Inspector 참조가 하나만 존재하도록 제한하는 ScriptableObject 클래스입니다.
/// </summary>
[CreateAssetMenu(
    fileName = "OnlyOneUnityString",
    menuName = "Scriptable Objects/OnlyOneUnityString"
)]
public class OnlyOneUnityString : ScriptableObject
{
    public string Value => name;

    public static implicit operator string(OnlyOneUnityString onlyOneUnityString) =>
        onlyOneUnityString != null ? onlyOneUnityString.name : string.Empty;
}
