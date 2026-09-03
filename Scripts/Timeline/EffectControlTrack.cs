using UnityEngine;
using UnityEngine.Timeline;

/// <summary>
/// 프리팹 스폰과 클립별 위치/회전/스케일 오프셋 조절을 지원하는 커스텀 타임라인 트랙입니다.
/// 타임라인 창에서 우클릭하여 'Effect Control Track'을 추가하고
/// 클립마다 자유롭게 이펙트 위치를 미세조정할 수 있습니다.
/// </summary>
[TrackColor(0.2f, 0.65f, 1.0f)]
[TrackClipType(typeof(EffectControlClip))]
[TrackBindingType(typeof(GameObject))]
public sealed class EffectControlTrack : TrackAsset
{
}
