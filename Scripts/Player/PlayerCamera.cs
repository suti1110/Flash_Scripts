using UnityEngine;

public class PlayerCamera : MonoBehaviour, IReflectable
{
    [field: SerializeField]
    public Camera MainCamera { get; private set; }

    [field: SerializeField]
    public Transform CameraPivot { get; private set; }
    private CameraController _controller;

    private Vector2 _pivotRotation;

    [SerializeField]
    private float _mouseSensitivity = 1f;

    [SerializeField]
    private Vector3 _cameraOffset;

    [SerializeField]
    private float _cameraBlockRadius;

    [SerializeField]
    private LayerMask _cameraBlockLayers;

    private PlayerStateMachine _playerStateMachine;

    private Rigidbody _rb;
    private Vector3 _lastVelocity;
    public Vector3 LastVelocity => _lastVelocity;

    private void Awake()
    {
        _controller = new CameraController();
        _rb = GetComponent<Rigidbody>();
        _playerStateMachine = GetComponent<Player>().StateMachine;
    }

    private void OnEnable()
    {
        _controller.Enable();
    }

    private void OnDisable()
    {
        _controller.Disable();
    }

    private void OnDestroy()
    {
        _controller.Dispose();
    }

    private void FixedUpdate()
    {
        _lastVelocity = _rb.linearVelocity;

        if (!_playerStateMachine.CurrentState.UsesDetachedCameraRotation)
        {
            _rb.MoveRotation(Quaternion.Euler(0, _pivotRotation.x, 0));
        }
    }

    private void LateUpdate()
    {
        if (Time.timeScale <= 0f)
            return;

        SetCameraRotation(
            _pivotRotation
                + 0.1f * _mouseSensitivity * _controller.MousePosition.Mouse.ReadValue<Vector2>()
        );

        CameraPivot.rotation = Quaternion.Euler(-_pivotRotation.y, _pivotRotation.x, 0);

        Vector3 rayDirection = CameraPivot.TransformDirection(_cameraOffset.normalized);

        if (
            Physics.SphereCast(
                CameraPivot.position,
                _cameraBlockRadius,
                rayDirection,
                out RaycastHit hitInfo,
                _cameraOffset.magnitude,
                _cameraBlockLayers
            )
        )
            MainCamera.transform.position = CameraPivot.position + rayDirection * hitInfo.distance;
        else
            MainCamera.transform.localPosition = _cameraOffset;

        MainCamera.transform.rotation = Quaternion.LookRotation(CameraPivot.forward);
    }

    public void SetCameraRotation(Vector2 value)
    {
        _pivotRotation = value;
        _pivotRotation.y = Mathf.Clamp(_pivotRotation.y, -80f, 80f);
    }

    private void OnValidate()
    {
        if (!MainCamera)
        {
            EditorLog.LogError("MainCamera가 할당되지 않았습니다!!!", this);
        }

        if (!CameraPivot)
        {
            EditorLog.LogError("CameraPivot이 할당되지 않았습니다!!!", this);
        }

        if (_cameraOffset == Vector3.zero)
        {
            EditorLog.LogError("CameraOffset이 (0,0,0)입니다!!!", this);
        }

        if (_cameraBlockRadius <= 0)
        {
            EditorLog.LogError("CameraBlockRadius가 0 이하입니다!!!", this);
        }
    }

    // Raycast를 화면에 그리기
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Vector3 startPos = CameraPivot.position;
        Vector3 endPos = startPos + _cameraOffset;

        // 시작점과 끝점에 구체를 그리고 선으로 연결
        Gizmos.DrawWireSphere(startPos, _cameraBlockRadius);
        Gizmos.DrawWireSphere(endPos, _cameraBlockRadius);
        Gizmos.DrawLine(
            startPos + Vector3.left * _cameraBlockRadius,
            endPos + Vector3.left * _cameraBlockRadius
        );
        Gizmos.DrawLine(
            startPos + Vector3.right * _cameraBlockRadius,
            endPos + Vector3.right * _cameraBlockRadius
        );
    }
}
