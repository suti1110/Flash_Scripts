using UnityEngine;

public interface IUnityObject
{
    public bool enabled { get; set; }
    public GameObject gameObject { get; }
    public Transform transform { get; }
    public T GetComponent<T>();
    public T GetComponentInChildren<T>();
}

public interface IReflectable
{
    public Vector3 LastVelocity { get; }
    public void SetCameraRotation(Vector2 value);
}

public interface IAttackable
{
    /// <summary>
    /// 공격 애니메이션이 시작되는 타이밍에 호출되는 함수
    /// </summary>
    void Attack();

    /// <summary>
    /// 공격 판정이 일어나는 타이밍에 호출되는 함수
    /// </summary>
    void AttackHit();
}

public interface IMovable
{
    void Move(Vector3 direction);
}

public interface IJumpable : IUnityObject
{
    void Jump();
}

public interface IDamagable
{
    void TakeDamageRpc(int damage, Vector3 knockback);
    void DeathCheck();
}

public interface IDeathable
{
    void Death();
}
