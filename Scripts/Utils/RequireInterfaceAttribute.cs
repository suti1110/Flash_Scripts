using System;
using UnityEngine;

public class RequireInterfaceAttribute : PropertyAttribute
{
    public Type InterfaceType { get; private set; }

    public RequireInterfaceAttribute(Type interfaceType)
    {
        this.InterfaceType = interfaceType;
    }
}
// RequireInterfaceAttribute은 여러 시스템에서 재사용하는 검증 또는 편의 기능을 제공한다.
// 기능별 중복 구현을 피하고 호출부가 동일한 규칙을 일관되게 사용하도록 한다.
