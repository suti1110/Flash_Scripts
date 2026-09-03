using System;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public struct CustomRoomSettings : INetworkSerializable, IEquatable<CustomRoomSettings>
{
    public const float MinimumGravityMultiplier = 0.1f;
    public const float MaximumGravityMultiplier = 3f;
    public const float MinimumMoveSpeedMultiplier = 0.25f;
    public const float MaximumMoveSpeedMultiplier = 3f;
    public const float MinimumJumpForceMultiplier = 0.25f;
    public const float MaximumJumpForceMultiplier = 3f;
    public const int MinimumAttackDamage = 1;
    public const int MaximumAttackDamage = 20;
    public const int MinimumHealthValue = 1;
    public const int MaximumHealthValue = 30;
    public const float MinimumKnockbackMultiplier = 0f;
    public const float MaximumKnockbackMultiplier = 5f;
    public const int MinimumThrowPower = 10;
    public const int MaximumThrowPower = 300;
    public const int DefaultThrowPower = 80;

    [SerializeField]
    private float _gravityMultiplier;

    [SerializeField]
    private float _moveSpeedMultiplier;

    [SerializeField]
    private float _jumpForceMultiplier;

    [SerializeField]
    private int _attackDamage;

    [SerializeField]
    private int _maximumHealth;

    [SerializeField]
    private float _knockbackMultiplier;

    [SerializeField]
    private int _throwPower;

    public static CustomRoomSettings Default => new(1f, 1f, 1f, 1, 3, 1f, DefaultThrowPower);

    public float GravityMultiplier => _gravityMultiplier;
    public float MoveSpeedMultiplier => _moveSpeedMultiplier;
    public float JumpForceMultiplier => _jumpForceMultiplier;
    public int AttackDamage => _attackDamage;
    public int MaximumHealth => _maximumHealth;
    public float KnockbackMultiplier => _knockbackMultiplier;
    public int ThrowPower => _throwPower;

    public CustomRoomSettings(
        float gravityMultiplier,
        float moveSpeedMultiplier,
        float jumpForceMultiplier,
        int attackDamage,
        int maximumHealth,
        float knockbackMultiplier,
        int throwPower
    )
    {
        _gravityMultiplier = gravityMultiplier;
        _moveSpeedMultiplier = moveSpeedMultiplier;
        _jumpForceMultiplier = jumpForceMultiplier;
        _attackDamage = attackDamage;
        _maximumHealth = maximumHealth;
        _knockbackMultiplier = knockbackMultiplier;
        _throwPower = throwPower;
        ClampValues();
    }

    public CustomRoomSettings Validated()
    {
        CustomRoomSettings result = this;
        result.ClampValues();
        return result;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer)
        where T : IReaderWriter
    {
        serializer.SerializeValue(ref _gravityMultiplier);
        serializer.SerializeValue(ref _moveSpeedMultiplier);
        serializer.SerializeValue(ref _jumpForceMultiplier);
        serializer.SerializeValue(ref _attackDamage);
        serializer.SerializeValue(ref _maximumHealth);
        serializer.SerializeValue(ref _knockbackMultiplier);
        serializer.SerializeValue(ref _throwPower);

        if (serializer.IsReader)
            ClampValues();
    }

    public bool Equals(CustomRoomSettings other)
    {
        return Mathf.Approximately(_gravityMultiplier, other._gravityMultiplier)
            && Mathf.Approximately(_moveSpeedMultiplier, other._moveSpeedMultiplier)
            && Mathf.Approximately(_jumpForceMultiplier, other._jumpForceMultiplier)
            && _attackDamage == other._attackDamage
            && _maximumHealth == other._maximumHealth
            && Mathf.Approximately(_knockbackMultiplier, other._knockbackMultiplier)
            && _throwPower == other._throwPower;
    }

    public override bool Equals(object obj)
    {
        return obj is CustomRoomSettings other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            _gravityMultiplier,
            _moveSpeedMultiplier,
            _jumpForceMultiplier,
            _attackDamage,
            _maximumHealth,
            _knockbackMultiplier,
            _throwPower
        );
    }

    private void ClampValues()
    {
        _gravityMultiplier = Mathf.Clamp(
            _gravityMultiplier,
            MinimumGravityMultiplier,
            MaximumGravityMultiplier
        );
        _moveSpeedMultiplier = Mathf.Clamp(
            _moveSpeedMultiplier,
            MinimumMoveSpeedMultiplier,
            MaximumMoveSpeedMultiplier
        );
        _jumpForceMultiplier = Mathf.Clamp(
            _jumpForceMultiplier,
            MinimumJumpForceMultiplier,
            MaximumJumpForceMultiplier
        );
        _attackDamage = Mathf.Clamp(_attackDamage, MinimumAttackDamage, MaximumAttackDamage);
        _maximumHealth = Mathf.Clamp(_maximumHealth, MinimumHealthValue, MaximumHealthValue);
        _knockbackMultiplier = Mathf.Clamp(
            _knockbackMultiplier,
            MinimumKnockbackMultiplier,
            MaximumKnockbackMultiplier
        );
        _throwPower = Mathf.Clamp(_throwPower, MinimumThrowPower, MaximumThrowPower);
    }
}

public sealed class CustomRoomSummary
{
    public string RoomId { get; }
    public string RoomName { get; }
    public int PlayerCount { get; }
    public int MaxPlayers { get; }
    public bool HasPassword { get; }
    public CustomRoomSettings Settings { get; }

    public CustomRoomSummary(
        string roomId,
        string roomName,
        int playerCount,
        int maxPlayers,
        bool hasPassword,
        CustomRoomSettings settings
    )
    {
        RoomId = roomId;
        RoomName = roomName;
        PlayerCount = playerCount;
        MaxPlayers = maxPlayers;
        HasPassword = hasPassword;
        Settings = settings;
    }
}
// CustomRoomSettings은 멀티플레이 세션에서 필요한 상태 전달과 네트워크 수명주기를 관리한다.
// 서버 권한 상태와 클라이언트 표시 상태를 구분하여 중복 실행과 비인가 변경을 방지한다.
