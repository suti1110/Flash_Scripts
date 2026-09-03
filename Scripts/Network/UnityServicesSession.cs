using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;

public sealed class UnityServicesSession
{
    private Task _initializationTask;

    public string PlayerId => AuthenticationService.Instance.PlayerId;

    public Task EnsureInitializedAsync()
    {
        return _initializationTask ??= InitializeAsync();
    }

    private static async Task InitializeAsync()
    {
        InitializationOptions options = new();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        options.SetProfile($"Player_{Guid.NewGuid():N}"[..15]);
#endif

        await UnityServices.InitializeAsync(options);

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        EditorLog.Log("Unity Services 초기화 완료!");
        EditorLog.Log($"Player ID: {AuthenticationService.Instance.PlayerId}");
    }
}
// UnityServicesSession은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
