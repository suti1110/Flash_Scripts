using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(InspectorNameAttribute))]
public sealed class InspectorNamePropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        InspectorNameAttribute inspectorName = (InspectorNameAttribute)attribute;
        GUIContent displayLabel = new(inspectorName.displayName, label.tooltip);

        EditorGUI.BeginProperty(position, displayLabel, property);
        EditorGUI.PropertyField(position, property, displayLabel, true);
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}
// InspectorNamePropertyDrawer 편집기 도구는 인스펙터, 에셋 또는 빌드 설정을 안전하게 편집하는 작업을 담당한다.
// 런타임 코드와 분리하여 잘못된 설정을 가능한 한 에디터 단계에서 발견하도록 한다.
