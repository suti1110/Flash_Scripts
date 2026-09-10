using UnityEngine;

[CreateAssetMenu(fileName = "TakingDamage_Stat", menuName = "Player/Stat/TakingDamage")]
public class SO_TakingDamage : ScriptableObject
{
    [field: SerializeField, Min(0f)]
    public float ActionDuration { get; private set; } = 0.225f;

    [field: SerializeField]
    public int MaxHp { get; private set; } = 3;

    [field: Header("피격 화면 연출")]
    [field: SerializeField, Min(0f)]
    public float CameraRippleStrength { get; private set; } = 1.5f;

    [field: SerializeField, Min(0.01f)]
    public float CameraRippleDuration { get; private set; } = 0.4f;

    [field: Header("피격 오디오 연출")]
    [field: SerializeField, Range(0f, 1f)]
    public float HitSfxVolume { get; private set; } = 1f;

    [field: SerializeField, Range(0f, 1f)]
    public float BgmDuckVolumeRatio { get; private set; } = 0.08f;

    [field: SerializeField, Min(0.01f)]
    public float BgmRecoveryDuration { get; private set; } = 0.8f;
}
// SO_TakingDamage은 인스펙터에서 조정하는 플레이어 설정 데이터를 런타임 로직과 분리해 제공한다.
// 에셋 기반 참조를 사용하여 기능 추가 시 호출 코드를 수정하지 않고 설정을 교체할 수 있게 한다.
