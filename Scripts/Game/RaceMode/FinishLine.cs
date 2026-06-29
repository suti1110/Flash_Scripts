using Unity.Netcode;
using UnityEngine;

public class FinishLine : NetworkBehaviour
{
    // 누가 먼저 들어왔는지 딱 한 번만 판정하기 위한 자물쇠
    private bool _isGameEnded = false;

    private void OnTriggerEnter(Collider other)
    {
        // 절대 규칙: 오직 방장(서버) 컴퓨터에서 일어난 물리 충돌만 진짜로 인정합니다!
        // 클라이언트 컴퓨터에서 자기가 먼저 닿았다고 충돌 이벤트가 발생해도 쿨하게 무시합니다.
        if (!IsServer || _isGameEnded)
            return;

        Rigidbody rb = other.attachedRigidbody;

        // 부딪힌 물체가 네트워크 플레이어인지 확인합니다. (NetworkObject가 있는지)
        if (rb && rb.TryGetComponent<NetworkObject>(out var playerNetObj))
        {
            // 플레이어가 맞다면 1등이 들어온 것이므로, 자물쇠를 걸어버립니다. (2, 3등 차단)
            _isGameEnded = true;

            // 1등으로 들어온 플레이어의 고유 번호(ClientId)를 가져옵니다.
            ulong winnerId = playerNetObj.OwnerClientId;

            EditorLog.Log($"[심판] {winnerId}번 선수가 1등으로 들어왔습니다! 게임 종료!");

            // 게임 매니저에게 "얘가 우승했다!" 라고 방송을 지시합니다.
            RaceModeManager.MyInstance.DeclareWinnerRpc(winnerId);
        }
    }
}
