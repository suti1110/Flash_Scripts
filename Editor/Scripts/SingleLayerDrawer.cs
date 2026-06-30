using UnityEditor;
using UnityEngine;

// SingleLayerAttribute가 붙은 변수의 인스펙터 UI를 이 클래스가 그려줍니다.
[CustomPropertyDrawer(typeof(SingleLayerAttribute))]
public class SingleLayerDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        // 변수 타입이 int일 때만 단일 레이어 드롭다운을 띄움
        if (property.propertyType == SerializedPropertyType.Integer)
        {
            // 유니티 기본 '단일 레이어 선택' UI를 사용
            property.intValue = EditorGUI.LayerField(position, label, property.intValue);
        }
        else
        {
            // int가 아니면 그냥 기본 UI를 그림
            EditorGUI.PropertyField(position, property, label);
        }
    }
}
