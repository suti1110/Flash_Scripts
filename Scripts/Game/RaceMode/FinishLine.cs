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

        // 투척물도 NetworkObject와 Rigidbody를 가지므로 NetworkObject 여부만으로 선수를 판정하면 안 된다.
        // 자식 신체 Collider는 attachedRigidbody를 통해 Player 루트로 정규화하고, Player가 없는 맵 오브젝트는 제외한다.
        if (
            rb == null
            || !rb.TryGetComponent<Player>(out _)
            || !rb.TryGetComponent(out NetworkObject playerNetObj)
        )
        {
            return;
        }

        // 플레이어가 맞다면 1등이 들어온 것이므로, 자물쇠를 걸어버립니다. (2, 3등 차단)
        _isGameEnded = true;

        // 1등으로 들어온 플레이어의 고유 번호(ClientId)를 가져옵니다.
        ulong winnerId = playerNetObj.OwnerClientId;

        EditorLog.Log($"[심판] {winnerId}번 선수가 1등으로 들어왔습니다! 게임 종료!");

        if (GameModeManager.Instance != null)
            GameModeManager.Instance.ReportPlayerFinished(winnerId);
    }
}
