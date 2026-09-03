using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public sealed class PendulumMace : MonoBehaviour
{
    // 사인 곡선으로 모든 피어에서 동일한 왕복 표현을 만들되, 피해 판정은 서버에서 한 번만 수행한다.
    [SerializeField]
    private Transform _swingRoot;

    [SerializeField, Range(1f, 89f)]
    private float _swingAngle = 65f;

    [SerializeField, Min(0.1f)]
    private float _swingPeriod = 3f;

    [SerializeField, Min(1)]
    private int _damage = 35;

    [SerializeField, Min(0f)]
    private float _knockback = 30f;

    [SerializeField, Min(0f)]
    private float _repeatHitCooldown = 0.75f;

    [Header("Sound")]
    [SerializeField]
    private AudioClip _swingLoopAudio;

    [SerializeField, Range(0f, 1f)]
    private float _swingVolume = 0.65f;

    private readonly Dictionary<PlayerDamage, float> _nextHitTimes = new();
    private PendulumMaceHitbox _hitbox;
    private AudioSource _swingAudioSource;

    private void Awake()
    {
        if (_swingRoot == null)
            _swingRoot = transform;

        _hitbox = GetComponentInChildren<PendulumMaceHitbox>();
        if (_hitbox != null)
            _hitbox.Initialize(this);

        if (_swingLoopAudio != null)
        {
            _swingAudioSource = gameObject.AddComponent<AudioSource>();
            AudioManager.ConfigureSpatialSfxSource(_swingAudioSource, 2f, 35f);
            _swingAudioSource.clip = _swingLoopAudio;
            _swingAudioSource.loop = true;
            _swingAudioSource.volume = _swingVolume;
            _swingAudioSource.Play();
        }
    }

    private void FixedUpdate()
    {
        // 네트워크 세션에서는 모든 피어가 공유하는 서버 시각으로 위상을 계산한다.
        // 로컬 테스트에서는 Unity 물리 시각을 사용하여 네트워크 의존성 없이 같은 동작을 유지한다.
        double simulationTime = GetSimulationTime();
        float phase = (float)(simulationTime * Mathf.PI * 2d / _swingPeriod);
        _swingRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(phase) * _swingAngle);
    }

    private static double GetSimulationTime()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return manager != null && manager.IsListening
            ? manager.ServerTime.Time
            : Time.fixedTimeAsDouble;
    }

    internal void Hit(Collider other, Vector3 headPosition)
    {
        if (!MapPropAuthority.CanSimulate)
            return;

        PlayerDamage player = other.GetComponentInParent<PlayerDamage>();
        if (player == null)
            return;

        // CollisionStay가 매 물리 프레임 호출되어도 플레이어별 재피격 간격을 보장한다.
        if (_nextHitTimes.TryGetValue(player, out float nextHitTime) && Time.time < nextHitTime)
            return;

        _nextHitTimes[player] = Time.time + _repeatHitCooldown;
        // 철퇴 머리에서 바깥쪽으로 밀어내고 위쪽 성분을 더해 장애물을 넘는 큰 넉백을 만든다.
        Vector3 direction = player.transform.position - headPosition;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up).normalized + Vector3.up * 0.25f;
        direction.Normalize();
        player.TakeDamage(_damage, direction * _knockback);
    }
}
// PendulumMace은 맵 소품 기믹의 물리 동작과 플레이어 상호작용 경계를 담당한다.
// 멀티플레이 중요 판정은 서버 권한에서 처리하고, 플레이어 공통 컴포넌트를 통해 결과를 적용한다.
