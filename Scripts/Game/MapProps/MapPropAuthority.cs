using Unity.Netcode;

public static class MapPropAuthority
{
    // 네트워크 세션이 없을 때는 로컬 테스트를 허용하고, 세션 중에는 서버만 중요 물리를 실행한다.
    public static bool CanSimulate
    {
        get
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager == null || !manager.IsListening || manager.IsServer;
        }
    }
}
// MapPropAuthority은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
