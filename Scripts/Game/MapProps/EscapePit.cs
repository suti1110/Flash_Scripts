using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public sealed class EscapePit : MonoBehaviour
{
    // 구덩이는 피해를 주지 않으며, 탈출 횟수와 입·출구 위치만 제공한다.
    // 실제 입력 소비와 플레이어 물리 고정은 PlayerTrappedState가 담당한다.
    [SerializeField, Min(1)]
    private int _requiredJumpPresses = 6;

    [SerializeField]
    private Transform _trapPoint;

    [SerializeField]
    private Transform _escapePoint;

    [SerializeField, Min(0f)]
    private float _releaseUpwardSpeed = 6f;

    [Header("Sound")]
    [SerializeField]
    private AudioClip _trapAudio;

    [SerializeField]
    private AudioClip _struggleAudio;

    [SerializeField]
    private AudioClip _releaseAudio;

    public int RequiredJumpPresses => _requiredJumpPresses;
    public Vector3 TrapPosition => _trapPoint != null ? _trapPoint.position : transform.position;

    private void OnTriggerEnter(Collider other)
    {
        TryTrap(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryTrap(other);
    }

    private void TryTrap(Collider other)
    {
        Player player = other.GetComponentInParent<Player>();
        if (player == null || !player.isActiveAndEnabled || !player.StateMachine.IsInitialized)
            return;

        // Owner 권한 플레이어만 자신의 State와 물리 위치를 변경하고 NetworkTransform으로 결과를 전파한다.
        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned && !networkObject.IsOwner)
            return;

        if (player.StateMachine.TryChangeState<PlayerTrappedState>(state => state.Configure(this)))
            AudioManager.SfxPlayAtPoint(_trapAudio, player.transform.position);
    }

    internal void Release(Rigidbody rigidbody)
    {
        // Transform 직접 이동 후 위쪽 속도를 부여하여 다음 물리 프레임부터 자연스럽게 낙하 상태로 이어지게 한다.
        Vector3 destination = _escapePoint != null
            ? _escapePoint.position
            : transform.position + transform.up * 2f;
        rigidbody.position = destination;
        rigidbody.linearVelocity = transform.up * _releaseUpwardSpeed;
        AudioManager.SfxPlayAtPoint(_releaseAudio, destination);
    }

    internal void PlayStruggleAudio(Vector3 position)
    {
        AudioManager.SfxPlayAtPoint(_struggleAudio, position);
    }

    private void OnValidate()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }
}
// EscapePit은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
