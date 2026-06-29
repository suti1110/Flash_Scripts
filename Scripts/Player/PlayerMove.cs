using UnityEngine;

public class PlayerMove : MonoBehaviour, IMovable
{
    [SerializeField]
    private SO_Moving _moving;

    [SerializeField]
    private Collider _collider;
    private Rigidbody _rb;
    private PhysicsMaterial _physicMaterial;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        _physicMaterial = _collider.material;
    }

    public void Move(Vector3 direction)
    {
        //EditorLog.Log(transform.forward);
        direction = transform.TransformDirection(direction);
        _rb.AddForce(direction * _moving.MoveSpeed, ForceMode.Force);

        Vector3 moveDirection = new Vector3(
            _rb.linearVelocity.x,
            0,
            _rb.linearVelocity.z
        ).normalized;

        if (moveDirection == Vector3.zero)
        {
            _physicMaterial.dynamicFriction = 0;
            return;
        }

        if (direction == Vector3.zero)
        {
            _physicMaterial.dynamicFriction = 3;
        }
        else
        {
            float correctionRate = -(Vector3.Dot(direction, moveDirection) - 1) / 2; // 마찰력 보정률(커브 안정성)
            _physicMaterial.dynamicFriction = Mathf.Lerp(0, 3, correctionRate);
        }

        if (_rb.linearVelocity.magnitude > _moving.LightSpeed)
        {
            _rb.linearVelocity = _rb.linearVelocity.normalized * _moving.LightSpeed; // 플레이어가 시스템적인 문제가 생길 정도로 빨라지지 않게 하기 위한 리미트
        }
    }

    private void OnDestroy()
    {
        Destroy(_physicMaterial);
    }

    private void OnValidate()
    {
        if (!_moving)
        {
            EditorLog.LogError("SO_Moving이 할당되지 않았습니다!", this);
        }

        if (!_collider)
        {
            EditorLog.LogError("Collider가 할당되지 않았습니다!", this);
        }
    }
}
