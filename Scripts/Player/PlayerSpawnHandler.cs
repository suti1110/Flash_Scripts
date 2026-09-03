using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

// 캐릭터 프리팹의 최상단(NetworkObject가 있는 곳)에 붙어있어야 합니다.
public class PlayerSpawnHandler : NetworkBehaviour
{
    private Vector3 _spawnPosition;
    private Quaternion _spawnRotation;
    private bool _hasSpawnPoint;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer && !_hasSpawnPoint)
            SetSpawnPoint(transform.position, transform.rotation);
    }

    internal void SetSpawnPoint(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        if (!IsServer)
            return;

        _spawnPosition = spawnPosition;
        _spawnRotation = spawnRotation;
        _hasSpawnPoint = true;
    }

    internal bool TryRespawnAtSpawnPoint()
    {
        if (!IsServer || !_hasSpawnPoint)
            return false;

        ForceTeleportRpc(_spawnPosition, _spawnRotation);
        return true;
    }

    // [Rpc(SendTo.Owner)]는 "서버 -> 이 캐릭터의 주인 1명에게만" 보내는 최신 V2 강제 명령
    [Rpc(SendTo.Owner)]
    public void ForceTeleportRpc(Vector3 spawnPosition, Quaternion spawnRotation)
    {
        // 물리적 위치와 회전값을 서버가 지시한(GameManager가 정해준) 값으로 강제 덮어쓰기
        bool isTeleported = false;

        if (TryGetComponent(out NetworkTransform netTransform))
        {
            netTransform.Teleport(spawnPosition, spawnRotation, transform.localScale);
            isTeleported = true;
        }

        if (TryGetComponent(out NetworkRigidbody netRigidbody))
        {
            netRigidbody.SetPosition(spawnPosition);
            netRigidbody.SetRotation(spawnRotation);
            isTeleported = true;
        }

        if (!isTeleported)
        {
            // 만약 Network 계열을 안 쓰는 객체라면 기존 방식 사용
            transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        }

        if (TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero; // 떨어지거나 날아가던 속도를 0으로!
            rb.angularVelocity = Vector3.zero; // 빙글빙글 돌던 회전력도 0으로
        }

        EditorLog.Log($"[강제 이동 완료] 서버의 지시에 따라 {spawnPosition} 좌표로 안착했습니다!");
    }
}
