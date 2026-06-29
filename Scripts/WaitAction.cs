using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaitActionManager : MonoBehaviour
{
    // 이 클래스는 WaitAction에서 코루틴을 실행하기 위한 MonoBehaviour 인스턴스를 제공하는 역할만 합니다.
}

public static class WaitAction
{
    // 코루틴을 실행하기 위한 MonoBehaviour 인스턴스
    private static WaitActionManager _instance;
    private static WaitActionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject obj = new GameObject("WaitActionManager");
                _instance = obj.AddComponent<WaitActionManager>();
                UnityEngine.Object.DontDestroyOnLoad(obj);
            }

            return _instance;
        }
    }

    // 코루틴 메모리 향상을 위한 캐시
    private static Dictionary<float, YieldInstruction> cache =
        new Dictionary<float, YieldInstruction>();

    /// <summary>
    /// 외부에 노출되는 정적 메서드
    /// </summary>
    /// <param name="seconds">대기할 시간</param>
    /// <param name="callback">실행할 콜백</param>
    /// <returns></returns>
    public static Coroutine Wait(float seconds, Action callback)
    {
        return Instance.StartCoroutine(Internal_Wait(seconds, callback));
    }

    // 내부 코루틴 메서드
    private static IEnumerator Internal_Wait(float seconds, Action callback)
    {
        // 캐싱
        if (!cache.ContainsKey(seconds))
        {
            cache[seconds] = new WaitForSeconds(seconds);
        }
        yield return cache[seconds];
        callback?.Invoke(); // 콜백 실행
    }

    /// <summary>
    /// 외부에 노출되는 정적 메서드(타임 아웃은 음수로 설정하면 무한 대기)
    /// </summary>
    /// <param name="condition">callback이 실행될 조건</param>
    /// <param name="callback">condition이 만족되었을 때 실행할 콜백</param>
    /// <param name="timeOut">condition이 만족되지 않을 가능성을 고려한 타임아웃</param>
    /// <returns></returns>
    public static Coroutine WaitUntil(Func<bool> condition, Action callback, float timeOut = -1)
    {
        return Instance.StartCoroutine(Internal_WaitUntil(condition, callback, timeOut));
    }

    // 내부 코루틴 메서드
    private static IEnumerator Internal_WaitUntil(
        Func<bool> condition,
        Action callback,
        float timeOut
    )
    {
        float timer = 0; // 타임아웃을 체크하기 위한 타이머
        bool isSuccess = false; // 조건이 충족되었는지 여부

        bool func()
        {
            timer += Time.deltaTime;
            if (condition())
            {
                // 조건이 충족되었을 때
                isSuccess = true;
                return true;
            }
            else
                return timeOut >= 0 && timer >= timeOut; // 타임아웃이 설정되어 있고, 타이머가 타임아웃을 초과했을 때
        }

        yield return new WaitUntil(func);

        if (isSuccess)
            callback?.Invoke(); // 조건이 충족되었을 때만 콜백 실행
    }
}
