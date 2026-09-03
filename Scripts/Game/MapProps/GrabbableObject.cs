using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject), typeof(Rigidbody), typeof(Collider))]
public sealed class GrabbableObject : NetworkBehaviour
{
    private enum InteractionAudio : byte
    {
        Grab,
        Throw,
        Drop,
        Impact,
    }

    // 잡기 상태와 Rigidbody 물리는 서버가 소유하며 NetworkTransform/NetworkRigidbody가 결과를 복제한다.
    [SerializeField, Min(1)]
    private int _damage = 20;

    [SerializeField, Min(0f)]
    private float _minimumDamageSpeed = 4f;

    [SerializeField, Min(0f)]
    private float _knockback = 20f;

    [SerializeField, Min(0f)]
    private float _throwerImmunityDuration = 0.3f;

    [Header("Sound")]
    [SerializeField]
    private AudioClip _grabAudio;

    [SerializeField]
    private AudioClip _throwAudio;

    [SerializeField]
    private AudioClip _dropAudio;

    [SerializeField]
    private AudioClip _impactAudio;

    // Physics.IgnoreCollision은 피어별 로컬 상태이므로, 무시 대상과 복원 대기 여부만 서버가 복제한다.
    private readonly NetworkVariable<NetworkObjectReference> _ignoredPlayerReference = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<bool> _separationPending = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Rigidbody _rigidbody;
    private Collider[] _objectColliders;
    private Collider[] _ignoredPlayerColliders;
    private PlayerMapInteraction _holder;
    private PlayerMapInteraction _thrower;
    private PlayerMapInteraction _collisionIgnoredPlayer;
    private float _throwerImmuneUntil;
    private bool _isArmed;
    private bool _restoreCollisionsWhenSeparated;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _objectColliders = GetComponentsInChildren<Collider>(true);
    }

    private void FixedUpdate()
    {
        // 늦게 생성된 플레이어 NetworkObject도 참조가 해석되는 즉시 로컬 충돌 무시를 적용한다.
        if (
            _collisionIgnoredPlayer == null
            && IsSpawned
            && TryApplyReplicatedIgnoredPlayer()
        )
        {
            _restoreCollisionsWhenSeparated = _separationPending.Value;
        }

        if (!_restoreCollisionsWhenSeparated || _collisionIgnoredPlayer == null)
            return;

        // 각 피어의 보간 위치가 다를 수 있으므로 실제로 분리된 첫 로컬 물리 프레임에 충돌을 되돌린다.
        if (!IsOverlappingIgnoredPlayer())
        {
            RestorePlayerCollisionsLocally();

            // 서버가 분리를 확인하면 복제 상태도 비워 늦게 참가한 피어가 지난 무시 상태를 적용하지 않게 한다.
            if (IsSpawned && IsServer)
            {
                _ignoredPlayerReference.Value = default;
                _separationPending.Value = false;
            }
        }
    }

    public bool CanBeGrabbedBy(PlayerMapInteraction requester)
    {
        return requester != null && (_holder == null || _holder == requester);
    }

    internal void Grab(PlayerMapInteraction requester)
    {
        // 날아오는 물체도 같은 진입점을 사용하므로 잡기에 성공하면 즉시 투척 피해 상태가 해제된다.
        if (!MapPropAuthority.CanSimulate || !CanBeGrabbedBy(requester))
            return;

        _holder = requester;
        _thrower = null;
        _isArmed = false;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.isKinematic = true;

        // 부모가 되는 플레이어의 모든 Collider와 충돌을 끊어, 애니메이션이나 이동 중 물체가 몸을 밀지 않게 한다.
        SetIgnoredPlayer(requester);

        // 일반 자식 Transform도 잡기 기준으로 쓸 수 있도록 서버에서 실제 부모를 교체한다.
        // NetworkTransform은 월드 좌표를 복제하므로 원격 클라이언트에도 같은 위치와 회전이 표현된다.
        transform.SetParent(requester.HoldParent, true);
        transform.SetPositionAndRotation(requester.HoldPosition, requester.HoldRotation);
        requester.SetHeldObject(this);
        PlayInteractionAudio(InteractionAudio.Grab, transform.position);
    }

    internal bool Throw(Vector3 direction, float speed)
    {
        // 손 위치 추적을 끝낸 뒤 서버 Rigidbody에 초기 속도를 부여하여 이후 궤적을 물리에 맡긴다.
        if (!MapPropAuthority.CanSimulate || _holder == null)
            return false;

        _thrower = _holder;
        _holder.ClearHeldObject(this);
        _holder = null;
        _isArmed = true;
        _throwerImmuneUntil = Time.time + _throwerImmunityDuration;
        BeginCollisionRestoration();
        transform.SetParent(null, true);
        _rigidbody.isKinematic = false;
        _rigidbody.linearVelocity = direction.normalized * speed;
        PlayInteractionAudio(InteractionAudio.Throw, transform.position);
        return true;
    }

    internal void Drop()
    {
        if (!MapPropAuthority.CanSimulate || _holder == null)
            return;

        _holder.ClearHeldObject(this);
        _holder = null;
        _thrower = null;
        _isArmed = false;
        BeginCollisionRestoration();
        transform.SetParent(null, true);
        _rigidbody.isKinematic = false;
        PlayInteractionAudio(InteractionAudio.Drop, transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 투척 직후 자기 몸과 겹치는 현상은 짧은 면역 시간으로 무시하고, 유효 속도 이상일 때만 피해를 준다.
        if (!MapPropAuthority.CanSimulate || !_isArmed)
            return;

        PlayerDamage player = collision.collider.GetComponentInParent<PlayerDamage>();
        if (player == null)
            return;

        if (_thrower != null && player.transform == _thrower.transform && Time.time < _throwerImmuneUntil)
            return;

        float speed = _rigidbody.linearVelocity.magnitude;
        if (speed < _minimumDamageSpeed)
            return;

        Vector3 direction = _rigidbody.linearVelocity.normalized;
        player.TakeDamage(_damage, direction * _knockback);
        _isArmed = false;
        Vector3 impactPosition = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;
        PlayInteractionAudio(InteractionAudio.Impact, impactPosition);
    }

    private void PlayInteractionAudio(InteractionAudio audio, Vector3 position)
    {
        if (IsSpawned)
            PlayInteractionAudioRpc((byte)audio, position);
        else
            PlayInteractionAudioLocal(audio, position);
    }

    [Rpc(SendTo.Everyone)]
    private void PlayInteractionAudioRpc(byte audioId, Vector3 position)
    {
        PlayInteractionAudioLocal((InteractionAudio)audioId, position);
    }

    private void PlayInteractionAudioLocal(InteractionAudio audio, Vector3 position)
    {
        AudioClip clip = audio switch
        {
            InteractionAudio.Grab => _grabAudio,
            InteractionAudio.Throw => _throwAudio,
            InteractionAudio.Drop => _dropAudio,
            InteractionAudio.Impact => _impactAudio,
            _ => null,
        };
        AudioManager.SfxPlayAtPoint(clip, position);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _ignoredPlayerReference.OnValueChanged += HandleIgnoredPlayerChanged;
        _separationPending.OnValueChanged += HandleSeparationPendingChanged;
        ApplyReplicatedCollisionState();
    }

    public override void OnNetworkDespawn()
    {
        _ignoredPlayerReference.OnValueChanged -= HandleIgnoredPlayerChanged;
        _separationPending.OnValueChanged -= HandleSeparationPendingChanged;
        _holder?.ClearHeldObject(this);
        _holder = null;
        RestorePlayerCollisionsLocally();
        transform.SetParent(null, true);
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        RestorePlayerCollisionsLocally();
        base.OnDestroy();
    }

    private void SetIgnoredPlayer(PlayerMapInteraction player)
    {
        IgnorePlayerCollisionsLocally(player);

        if (!IsSpawned || !IsServer)
            return;

        _separationPending.Value = false;
        _ignoredPlayerReference.Value = new NetworkObjectReference(player.NetworkObject);
    }

    private void IgnorePlayerCollisionsLocally(PlayerMapInteraction player)
    {
        // 재사용되거나 비정상적인 순서로 다시 잡혀도 이전 플레이어와의 IgnoreCollision 쌍을 먼저 정리한다.
        RestorePlayerCollisionsLocally();
        _collisionIgnoredPlayer = player;
        _ignoredPlayerColliders = player.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < _objectColliders.Length; i++)
        {
            Collider objectCollider = _objectColliders[i];
            if (objectCollider == null)
                continue;

            for (int j = 0; j < _ignoredPlayerColliders.Length; j++)
            {
                Collider playerCollider = _ignoredPlayerColliders[j];
                if (playerCollider == null || playerCollider == objectCollider)
                    continue;

                Physics.IgnoreCollision(objectCollider, playerCollider, true);
            }
        }
    }

    private void BeginCollisionRestoration()
    {
        // Collider가 아직 플레이어 안에 있는 동안에는 IgnoreCollision 상태를 유지한다.
        _restoreCollisionsWhenSeparated = true;

        if (IsSpawned && IsServer)
            _separationPending.Value = true;
    }

    private void HandleIgnoredPlayerChanged(
        NetworkObjectReference previousReference,
        NetworkObjectReference currentReference
    )
    {
        // 서버가 먼저 분리되었더라도 이 피어의 보간 위치가 아직 겹치면 로컬 IgnoreCollision은 유지한다.
        if (!currentReference.TryGet(out _) && IsOverlappingIgnoredPlayer())
        {
            _restoreCollisionsWhenSeparated = true;
            return;
        }

        RestorePlayerCollisionsLocally();
        TryApplyReplicatedIgnoredPlayer();
        _restoreCollisionsWhenSeparated = _separationPending.Value;
    }

    private void HandleSeparationPendingChanged(bool previousValue, bool currentValue)
    {
        if (currentValue)
        {
            _restoreCollisionsWhenSeparated = true;
            return;
        }

        if (_collisionIgnoredPlayer != null && IsOverlappingIgnoredPlayer())
        {
            _restoreCollisionsWhenSeparated = true;
            return;
        }

        RestorePlayerCollisionsLocally();
    }

    private void ApplyReplicatedCollisionState()
    {
        TryApplyReplicatedIgnoredPlayer();
        _restoreCollisionsWhenSeparated = _separationPending.Value;
    }

    private bool TryApplyReplicatedIgnoredPlayer()
    {
        if (
            _collisionIgnoredPlayer != null
            || !_ignoredPlayerReference.Value.TryGet(out NetworkObject playerNetworkObject)
            || !playerNetworkObject.TryGetComponent(out PlayerMapInteraction player)
        )
        {
            return false;
        }

        IgnorePlayerCollisionsLocally(player);
        return true;
    }

    private bool IsOverlappingIgnoredPlayer()
    {
        if (_collisionIgnoredPlayer == null || _ignoredPlayerColliders == null)
            return false;

        for (int i = 0; i < _objectColliders.Length; i++)
        {
            Collider objectCollider = _objectColliders[i];
            if (!IsColliderActive(objectCollider))
                continue;

            for (int j = 0; j < _ignoredPlayerColliders.Length; j++)
            {
                Collider playerCollider = _ignoredPlayerColliders[j];
                if (!IsColliderActive(playerCollider) || playerCollider == objectCollider)
                    continue;

                // Physics.ComputePenetration은 충돌 응답이 무시된 Collider 쌍도 실제 형상 기준으로 겹침을 판정한다.
                if (Physics.ComputePenetration(
                    objectCollider,
                    objectCollider.transform.position,
                    objectCollider.transform.rotation,
                    playerCollider,
                    playerCollider.transform.position,
                    playerCollider.transform.rotation,
                    out _,
                    out _
                ))
                    return true;
            }
        }

        return false;
    }

    private static bool IsColliderActive(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }

    private void RestorePlayerCollisionsLocally()
    {
        _restoreCollisionsWhenSeparated = false;

        if (_ignoredPlayerColliders == null)
        {
            _collisionIgnoredPlayer = null;
            return;
        }

        // IgnoreCollision은 전역 물리 상태이므로 모든 종료 경로에서 원래 충돌 관계를 반드시 복원한다.
        for (int i = 0; i < _objectColliders.Length; i++)
        {
            Collider objectCollider = _objectColliders[i];
            if (objectCollider == null)
                continue;

            for (int j = 0; j < _ignoredPlayerColliders.Length; j++)
            {
                Collider playerCollider = _ignoredPlayerColliders[j];
                if (playerCollider == null || playerCollider == objectCollider)
                    continue;

                Physics.IgnoreCollision(objectCollider, playerCollider, false);
            }
        }

        _collisionIgnoredPlayer = null;
        _ignoredPlayerColliders = null;
    }
}
// GrabbableObject은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
