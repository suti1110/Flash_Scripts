using UnityEngine;

// SniperDrone의 눈 추적과 로터 회전만 담당하는 로컬 표현 컴포넌트다.
// 네트워크 상태나 명중 판정을 소유하지 않고 각 피어가 복제된 조준 목표를 같은 방식으로 표현한다.
public sealed class SniperDroneVisual : MonoBehaviour
{
    [Header("본체 표면")]
    [SerializeField]
    [Tooltip("눈이 붙을 실제 본체 Mesh와 동일한 MeshCollider입니다.")]
    private MeshCollider _bodySurfaceCollider;

    [SerializeField, Min(0f)]
    [Tooltip("눈이 본체에 파묻히지 않도록 표면 법선 방향으로 더할 거리입니다.")]
    private float _eyeSurfaceOffset = 0.01f;

    [Header("조준 눈")]
    [SerializeField]
    private Transform _eyePivot;

    [SerializeField]
    [Tooltip("눈 모델의 정면 축이 Transform의 +Z와 다를 때 적용할 회전 보정값입니다.")]
    private Vector3 _eyeEulerOffset;

    [Header("상단 로터")]
    [SerializeField]
    private Transform _rotor;

    [SerializeField]
    [Tooltip("로터가 회전할 로컬 축입니다.")]
    private Vector3 _rotorLocalAxis = Vector3.up;

    [SerializeField, Min(0f)]
    private float _rotorDegreesPerSecond = 720f;

    private void Update()
    {
        if (_rotor == null || _rotorLocalAxis.sqrMagnitude <= Mathf.Epsilon)
            return;

        _rotor.Rotate(
            _rotorLocalAxis.normalized,
            _rotorDegreesPerSecond * Time.deltaTime,
            Space.Self
        );
    }

    internal void AimAt(Vector3 targetPosition)
    {
        if (_bodySurfaceCollider == null || _eyePivot == null)
            return;

        Bounds bodyBounds = _bodySurfaceCollider.bounds;
        Vector3 bodyCenter = bodyBounds.center;
        Vector3 worldDirection = targetPosition - bodyCenter;
        if (worldDirection.sqrMagnitude <= Mathf.Epsilon)
            return;

        Vector3 outwardDirection = worldDirection.normalized;
        float rayDistance = bodyBounds.extents.magnitude + 0.1f;
        Ray surfaceRay = new(
            bodyCenter + outwardDirection * rayDistance,
            -outwardDirection
        );

        // 물리 월드 전체가 아니라 지정된 본체 MeshCollider만 검사해 실제 삼각형 표면에 붙인다.
        if (!_bodySurfaceCollider.Raycast(surfaceRay, out RaycastHit surfaceHit, rayDistance * 2f))
            return;

        _eyePivot.position = surfaceHit.point + surfaceHit.normal * _eyeSurfaceOffset;

        Vector3 eyeDirection = targetPosition - _eyePivot.position;
        if (eyeDirection.sqrMagnitude <= Mathf.Epsilon)
            return;

        // Muzzle을 EyePivot 아래에 두면 움직이는 눈과 레이저 시작점이 같은 위치와 방향을 유지한다.
        _eyePivot.rotation =
            Quaternion.LookRotation(eyeDirection.normalized, Vector3.up)
            * Quaternion.Euler(_eyeEulerOffset);
    }

    private void OnValidate()
    {
        if (_bodySurfaceCollider == null)
            EditorLog.LogError("SniperDroneVisual에 Body Surface Collider가 설정되지 않았습니다.", this);
        else if (_bodySurfaceCollider.sharedMesh == null)
            EditorLog.LogError("Body Surface Collider에 본체 Mesh가 설정되지 않았습니다.", this);
        else if (_bodySurfaceCollider.convex)
            EditorLog.LogError("정확한 본체 표면 추적을 위해 Body Surface Collider의 Convex를 해제해야 합니다.", this);

        if (_bodySurfaceCollider != null && !_bodySurfaceCollider.enabled)
            EditorLog.LogError("눈의 표면 Raycast를 위해 Body Surface Collider가 활성화되어야 합니다.", this);

        if (_eyePivot == null)
            EditorLog.LogError("SniperDroneVisual에 Eye Pivot이 설정되지 않았습니다.", this);

        if (_rotor == null)
            EditorLog.LogError("SniperDroneVisual에 Rotor가 설정되지 않았습니다.", this);

        if (_rotorLocalAxis.sqrMagnitude <= Mathf.Epsilon)
            EditorLog.LogError("SniperDroneVisual의 Rotor Local Axis는 0이 아니어야 합니다.", this);
    }
}
