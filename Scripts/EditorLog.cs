using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class EditorLog
{
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message) => Debug.Log(message);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(object message, Object context) => Debug.Log(message, context);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(object message) => Debug.LogWarning(message);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(object message, Object context) =>
        Debug.LogWarning(message, context);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(object message) => Debug.LogError(message);

    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogError(object message, Object context) => Debug.LogError(message, context);
}
// EditorLog은 프로젝트 전반에서 사용하는 공통 게임플레이 책임을 담당한다.
// 관련 처리 과정을 한곳에 모아 호출 측이 구체적인 구현 세부 사항에 의존하지 않도록 한다.
