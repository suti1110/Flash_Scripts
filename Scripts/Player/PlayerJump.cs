using Unity.Netcode;
using UnityEngine;

public class PlayerJump : NetworkBehaviour, IJumpable
{
    [Header("State")]
    [SerializeField]
    private SO_Jumping _jumping;

    private PlayerJumpingStateMachine _jumpingStateMachine;
    private Rigidbody _rb;
    private bool _hasGroundState;
    private bool _wasGrounded;

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
        _jumpingStateMachine = GetComponent<Player>().JumpingStateMachine;
    }

    public void Jump()
    {
        if (Time.timeScale <= 0f)
            return;

        if (!_jumpingStateMachine.TryChangeState<PlayerJumpingAscendingState>())
            return;

        float jumpForceMultiplier = RelayManager.Instance != null
            ? RelayManager.Instance.CurrentRoomSettings.JumpForceMultiplier
            : 1f;
        _rb.AddForce(
            Vector3.up * (_jumping.JumpForce * jumpForceMultiplier),
            ForceMode.Impulse
        );
        _wasGrounded = false;
        PlayMovementAudio(true, transform.position);
    }

    public void SetJumpProcessingEnabled(bool isEnabled)
    {
        enabled = isEnabled;
    }

    internal void ApplyGravity(bool useGravity)
    {
        _rb.useGravity = useGravity;
    }

    private void FixedUpdate()
    {
        CheckGround();
    }

    private void CheckGround()
    {
        if (
            _rb.linearVelocity.y > 0.1f
            && _jumpingStateMachine.IsInState<PlayerJumpingAscendingState>()
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

        if (!_hasGroundState)
        {
            _hasGroundState = true;
            _wasGrounded = isGround;
        }
        else if (isGround && !_wasGrounded)
        {
            PlayMovementAudio(false, transform.position);
        }

        _wasGrounded = isGround;

        if (isGround)
        {
            _jumpingStateMachine.TryChangeState<PlayerJumpingIdleState>();
        }
        else
        {
            _jumpingStateMachine.TryChangeState<PlayerJumpingFallingState>();
        }
    }

    private void PlayMovementAudio(bool isJump, Vector3 position)
    {
        if (IsSpawned)
            PlayMovementAudioRpc(isJump, position);
        else
            PlayMovementAudioLocal(isJump, position);
    }

    [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Owner)]
    private void PlayMovementAudioRpc(bool isJump, Vector3 position)
    {
        PlayMovementAudioLocal(isJump, position);
    }

    private static void PlayMovementAudioLocal(bool isJump, Vector3 position)
    {
        SO_SFXContainer container = AudioManager.Instance != null
            ? AudioManager.Instance.Container
            : null;
        AudioClip clip = isJump ? container?.Jump : container?.Land;
        AudioManager.SfxPlayAtPoint(clip, position);
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
