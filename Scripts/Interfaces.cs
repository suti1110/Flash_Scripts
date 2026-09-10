using UnityEngine;

public interface IReflectable
{
    public Vector3 LastVelocity { get; }
    public void TweenCameraRotation(Vector2 value);
}

public interface IAttackable
{
    /// <summary>
    /// 공격 액션을 시작합니다.
    /// </summary>
    void Attack();
}

public interface IMovable
{
    void Move(Vector3 direction);
}

public interface IJumpable
{
    void Jump();
    void SetJumpProcessingEnabled(bool isEnabled);
}

public interface IDamageable
{
    void TakeDamage(int damage, Vector3 knockback);
}

public interface IDeathable
{
    void Death();
}
