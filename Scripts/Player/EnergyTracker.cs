using System;
using Unity.Netcode;
using UnityEngine;

public interface IEnergyTracker
{
    float Energy { get; }
    void SetTrackingPause(bool isPaused);
}

// 이 컴포넌트는 PlayerNetworkDriver에서 소유자가 아니라면 자동으로 비활성화되는 컴포넌트입니다.
public class EnergyTracker : NetworkBehaviour, IEnergyTracker
{
    private readonly NetworkVariable<float> _energy = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public float Energy
    {
        get => _energy.Value;
        private set => _energy.Value = value;
    }

    public event Action<float> OnEnergyChanged;

    private Vector3 _lastPosition;
    private bool _isTrackingPaused = false; // 이동 거리 측정 일시 정지 여부

    private readonly PlayerStateManager _state = PlayerStateManager.Instance;

    private void Awake()
    {
        _state[gameObject].OnStateChanged += OnStateChanged;
    }

    public override void OnNetworkSpawn()
    {
        _energy.OnValueChanged += HandleEnergyValueChanged;

        if (IsOwner)
        {
            _lastPosition = transform.position;
        }
    }

    public override void OnNetworkDespawn()
    {
        _energy.OnValueChanged -= HandleEnergyValueChanged;
    }

    private void HandleEnergyValueChanged(float previousValue, float newValue)
    {
        OnEnergyChanged?.Invoke(newValue);
    }

    private void Update()
    {
        if (!IsOwner || _isTrackingPaused)
            return;

        float moveDistance = Vector3.Distance(transform.position, _lastPosition);

        if (moveDistance > 0.001f)
        {
            Energy += moveDistance;
            _lastPosition = transform.position;
        }
    }

    public bool TryConsumeEnergy(float amount)
    {
        if (Energy >= amount)
        {
            Energy -= amount;
            return true;
        }

        return false;
    }

    private void OnStateChanged(PlayerState state)
    {
        if (state == PlayerState.TakingDamage)
            ResetDistanceOnHit();
    }

    public void ResetDistanceOnHit()
    {
        Energy = 0f;
        EditorLog.Log("앗! 피격당했습니다. 모아둔 이동 거리가 전부 증발합니다!");
    }

    public void SetTrackingPause(bool isPaused)
    {
        _isTrackingPaused = isPaused;
        if (!isPaused)
            _lastPosition = transform.position;
    }
}
