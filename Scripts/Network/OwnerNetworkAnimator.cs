using Unity.Netcode.Components;

public class OwnerNetworkAnimator : NetworkAnimator
{
    protected override bool OnIsServerAuthoritative() => false;
}
// OwnerNetworkAnimator은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
