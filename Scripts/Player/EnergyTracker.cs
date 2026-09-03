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
    private const float NetworkSyncInterval = 0.1f;

    private readonly NetworkVariable<float> _energy = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public float Energy => !IsSpawned || IsOwner ? _localEnergy : _energy.Value;

    public event Action<float> OnEnergyChanged;

    private float _localEnergy;
    private float _nextNetworkSyncTime;
    private bool _isEnergyDirty;
    private Vector3 _lastPosition;
    private bool _isTrackingPaused = false; // 이동 거리 측정 일시 정지 여부

    public override void OnNetworkSpawn()
    {
        _energy.OnValueChanged += HandleEnergyValueChanged;
        _localEnergy = _energy.Value;

        if (IsOwner)
        {
            _lastPosition = transform.position;
            _nextNetworkSyncTime = Time.unscaledTime + NetworkSyncInterval;
        }

        OnEnergyChanged?.Invoke(Energy);
    }

    public override void OnNetworkDespawn()
    {
        _energy.OnValueChanged -= HandleEnergyValueChanged;
    }

    private void HandleEnergyValueChanged(float previousValue, float newValue)
    {
        if (!IsOwner)
            OnEnergyChanged?.Invoke(newValue);
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        if (!_isTrackingPaused)
        {
            float moveDistance = Vector3.Distance(transform.position, _lastPosition);

            if (moveDistance > 0.001f)
            {
                _localEnergy += moveDistance;
                _lastPosition = transform.position;
                _isEnergyDirty = true;
            }
        }

        if (_isEnergyDirty && Time.unscaledTime >= _nextNetworkSyncTime)
            PublishEnergy();
    }

    public bool TryConsumeEnergy(float amount)
    {
        if (Energy >= amount)
        {
            _localEnergy = Energy - amount;
            _isEnergyDirty = true;
            PublishEnergy();
            return true;
        }

        return false;
    }

    public void ResetDistanceOnHit()
    {
        if (IsSpawned && !IsOwner)
            return;

        _localEnergy = 0f;
        _isEnergyDirty = true;
        PublishEnergy();
        EditorLog.Log("앗! 피격당했습니다. 모아둔 이동 거리가 전부 증발합니다!");
    }

    public void SetTrackingPause(bool isPaused)
    {
        _isTrackingPaused = isPaused;
        if (!isPaused)
            _lastPosition = transform.position;
    }

    private void PublishEnergy()
    {
        _isEnergyDirty = false;
        _nextNetworkSyncTime = Time.unscaledTime + NetworkSyncInterval;

        if (IsSpawned && IsOwner)
            _energy.Value = _localEnergy;

        OnEnergyChanged?.Invoke(_localEnergy);
    }
}
