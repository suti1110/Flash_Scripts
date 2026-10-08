using System.Collections.Generic;
using UnityEngine;

// 침묵을 발생시키는 기믹의 공통 계약이다. 영역 스킬 외의 강제 침묵도 같은 계약으로 추가한다.
public interface IPlayerSilenceSource
{
    float GetSilencePower(Transform target);
    float GetSilenceImmunity(Transform target);
}

// PlayerSkill은 구체적인 침묵 스킬을 모르고 등록된 침묵 소스의 결과만 합산한다.
public static class PlayerSilenceSources
{
    private static readonly HashSet<IPlayerSilenceSource> _sources = new();

    // Domain Reload를 끈 에디터에서도 이전 플레이의 영역을 남기지 않는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSources() => _sources.Clear();

    public static void Register(IPlayerSilenceSource source)
    {
        if (source != null)
            _sources.Add(source);
    }

    public static void Unregister(IPlayerSilenceSource source)
    {
        if (source != null)
            _sources.Remove(source);
    }

    public static float GetPower(Transform target)
    {
        float power = 0f;
        foreach (IPlayerSilenceSource source in _sources)
            power = Mathf.Max(power, source.GetSilencePower(target));
        return power;
    }

    public static float GetImmunity(Transform target)
    {
        float immunity = 0f;
        foreach (IPlayerSilenceSource source in _sources)
            immunity += Mathf.Max(0f, source.GetSilenceImmunity(target));
        return immunity;
    }
}
