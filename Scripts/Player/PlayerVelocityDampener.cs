using Unity.Netcode;
using UnityEngine;

// 서버에서 확정된 충격을 플레이어 소유자의 Rigidbody에 한 번만 반영한다.
// 지속 시간이나 이동 능력치에는 관여하지 않으므로, 피격 직후부터 기존 이동 입력이 정상적으로 다시 가속시킨다.
[RequireComponent(typeof(Rigidbody))]
public sealed class PlayerVelocityDampener : NetworkBehaviour
{
    private Rigidbody _rigidbody;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
    }

    public void ApplyVelocityDampingFromServer(float multiplier)
    {
        // 명중 판정과 감쇠 배율의 신뢰 경계는 서버에 둔다.
        // NetworkObject가 없는 로컬 테스트 환경에서는 같은 로직을 즉시 실행한다.
        multiplier = Mathf.Clamp(multiplier, 0.01f, 1f);

        if (IsSpawned)
        {
            if (!IsServer)
                return;

            ApplyVelocityDampingRpc(multiplier);
            return;
        }

        ApplyVelocityDampingLocal(multiplier);
    }

    [Rpc(SendTo.Owner)]
    private void ApplyVelocityDampingRpc(float multiplier)
    {
        ApplyVelocityDampingLocal(multiplier);
    }

    private void ApplyVelocityDampingLocal(float multiplier)
    {
        // 플레이어 이동 물리는 Owner가 계산하므로, 현재 속도 벡터 전체를 Owner에서 순간적으로 감쇠한다.
        _rigidbody.linearVelocity *= multiplier;
    }
}
