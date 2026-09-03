using UnityEngine;

[CreateAssetMenu(fileName = "Player_Material", menuName = "Player/Material")]
public class SO_PlayerMaterial : ScriptableObject
{
    public Material[] AllSkin;
    public int DefaultSkinIndex => 0;
}
// SO_PlayerMaterial은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
