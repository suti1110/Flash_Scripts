// RequireInterfaceDrawer는 인터페이스 구현체만 할당할 수 있도록 Object 필드의 드래그와 선택 결과를 검증한다.
// 잘못된 Attribute 인수나 호환되지 않는 객체는 인스펙터에서 즉시 오류로 표시한다.

using System;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(RequireInterfaceAttribute))]
public class RequireInterfaceDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        RequireInterfaceAttribute requireInterface = attribute as RequireInterfaceAttribute;
        Type interfaceType = requireInterface.InterfaceType;

        if (!interfaceType.IsInterface)
        {
            EditorGUI.HelpBox(
                position,
                $"[RequireInterface] {interfaceType.Name} is not an interface.",
                UnityEditor.MessageType.Error
            );
            return;
        }

        EditorGUI.BeginChangeCheck();

        UnityEngine.Object obj = EditorGUI.ObjectField(
            position,
            label,
            property.objectReferenceValue,
            typeof(UnityEngine.Object),
            true
        );

        if (EditorGUI.EndChangeCheck())
        {
            if (obj == null)
            {
                property.objectReferenceValue = null;
            }
            else
            {
                // Validation logic
                UnityEngine.Object validatedObj = null;

                if (obj is GameObject go)
                {
                    validatedObj = go.GetComponent(interfaceType);
                }
                else if (obj is Component c)
                {
                    validatedObj = c.GetComponent(interfaceType);
                }
                else if (interfaceType.IsInstanceOfType(obj))
                {
                    validatedObj = obj;
                }

                if (validatedObj != null)
                {
                    property.objectReferenceValue = validatedObj;
                }
                else
                {
                    Debug.LogError($"{obj.name} does not implement interface {interfaceType.Name}");
                    property.objectReferenceValue = null;
                }
            }
        }
    }
}
