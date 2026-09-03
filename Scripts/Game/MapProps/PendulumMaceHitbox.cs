using UnityEngine;

[RequireComponent(typeof(SphereCollider), typeof(Rigidbody))]
public sealed class PendulumMaceHitbox : MonoBehaviour
{
    private const int MaximumOverlappingPlayerColliders = 64;

    // 서버의 원격 플레이어와 철퇴 머리는 모두 Kinematic이므로 둘 사이에는 Collision 콜백이 없다.
    // 머리의 실제 구체 범위를 서버에서 직접 검사하고, 피해 정책은 루트 PendulumMace에 위임한다.
    private PendulumMace _owner;
    private SphereCollider _hitbox;
    private int _playerLayers;
    private readonly Collider[] _overlappingPlayers = new Collider[
        MaximumOverlappingPlayerColliders
    ];

    private void Awake()
    {
        _hitbox = GetComponent<SphereCollider>();
        _playerLayers = LayerMask.GetMask("Player", "OtherPlayer");
    }

    internal void Initialize(PendulumMace owner)
    {
        _owner = owner;
    }

    private void FixedUpdate()
    {
        if (_owner == null || _hitbox == null || !MapPropAuthority.CanSimulate)
            return;

        Vector3 scale = transform.lossyScale;
        float maximumScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.y),
            Mathf.Abs(scale.z)
        );
        float worldRadius = _hitbox.radius * maximumScale;
        Vector3 worldCenter = transform.TransformPoint(_hitbox.center);
        int overlapCount = Physics.OverlapSphereNonAlloc(
            worldCenter,
            worldRadius,
            _overlappingPlayers,
            _playerLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < overlapCount; i++)
            _owner.Hit(_overlappingPlayers[i], worldCenter);
    }
}
// PendulumMaceHitbox은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
