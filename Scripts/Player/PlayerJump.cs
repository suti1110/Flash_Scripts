using UnityEngine;

public class PlayerJump : MonoBehaviour, IJumpable
{
    [Header("State")]
    [SerializeField]
    private SO_Jumping _jumping;

    private readonly PlayerJumpingStateManager _jumpingState = PlayerJumpingStateManager.Instance;
    private Rigidbody _rb;

    [Header("Ground Check (SphereCast)")]
    [SerializeField]
    private float _castDistance = 0.1f;

    [SerializeField]
    private float _castRadius = 0.3f; // 캐릭터 콜라이더 반경에 맞게 조절

    [SerializeField]
    private Vector3 _castOffset;

    [SerializeField]
    private LayerMask _layerMask;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        _jumpingState[gameObject].OnJumpingStateChanged += OnJumpingStateChanged;
    }

    private void OnJumpingStateChanged(PlayerJumpingState state)
    {
        switch (state)
        {
            case PlayerJumpingState.Idle:
                _rb.useGravity = true;
                break;
            case PlayerJumpingState.Jumping:
                _rb.useGravity = true;
                break;
            case PlayerJumpingState.Falling:
                _rb.useGravity = true;
                break;
            case PlayerJumpingState.Dead:
                _rb.useGravity = false;
                break;
        }
    }

    private void OnDestroy()
    {
        _jumpingState[gameObject].OnJumpingStateChanged -= OnJumpingStateChanged;
    }

    public void Jump()
    {
        _rb.AddForce(Vector3.up * _jumping.JumpForce, ForceMode.Impulse);
        _jumpingState[gameObject].JumpingState = PlayerJumpingState.Jumping;
    }

    private void FixedUpdate()
    {
        CheckGround();
    }

    private void CheckGround()
    {
        if (
            _rb.linearVelocity.y > 0.1f
            && _jumpingState[gameObject].JumpingState == PlayerJumpingState.Jumping
        )
            return;

        bool isGround = Physics.SphereCast(
            transform.position + _castOffset,
            _castRadius,
            Vector3.down,
            out _,
            _castDistance,
            _layerMask
        );

        if (isGround)
        {
            _jumpingState[gameObject].JumpingState = PlayerJumpingState.Idle;
        }
        else
        {
            _jumpingState[gameObject].JumpingState = PlayerJumpingState.Falling;
        }
    }

    private void OnValidate()
    {
        if (!_jumping)
        {
            EditorLog.LogError("SO_Jumping이 할당되지 않았습니다!", this);
        }
    }

    // Raycast를 화면에 그리기
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 startPos = transform.position + _castOffset;
        Vector3 endPos = startPos + (Vector3.down * _castDistance);

        // 시작점과 끝점에 구체를 그리고 선으로 연결
        Gizmos.DrawWireSphere(startPos, _castRadius);
        Gizmos.DrawWireSphere(endPos, _castRadius);
        Gizmos.DrawLine(startPos + Vector3.left * _castRadius, endPos + Vector3.left * _castRadius);
        Gizmos.DrawLine(
            startPos + Vector3.right * _castRadius,
            endPos + Vector3.right * _castRadius
        );
    }
}
