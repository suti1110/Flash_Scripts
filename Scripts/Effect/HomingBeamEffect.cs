using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HomingBeamEffect : MonoBehaviour
{
    private const string AdditiveShaderName = "Flash/Divine Punishment Additive";
    private const float TrailWidthScale = 0.35f;
    private const int MaxReflectionsPerFrame = 8;

    [SerializeField, InspectorName("광선 중심 색상")]
    private Color _coreColor = new(0.8f, 1f, 1f, 1f);

    [SerializeField, InspectorName("광선 외곽 색상")]
    private Color _glowColor = new(0.1f, 0.65f, 1f, 0.65f);

    [SerializeField, InspectorName("Trail 유지 시간"), Min(0.01f)]
    private float _trailTime = 0.2f;

    [SerializeField, InspectorName("중심 광선 너비"), Min(0.001f)]
    private float _coreWidth = 0.6f;

    [SerializeField, InspectorName("외곽 광선 너비"), Min(0.001f)]
    private float _glowWidth = 1.9f;

    [SerializeField, InspectorName("광선 몸체 길이"), Min(0.01f)]
    private float _bodyLength = 4.75f;

    [SerializeField, InspectorName("광선 곡선 분할 수"), Range(4, 32)]
    private int _volumeSegmentCount = 16;

    [SerializeField, InspectorName("거울 레이어")]
    private LayerMask _mirrorLayers = 1 << 8;

    [SerializeField, InspectorName("반사 표면 여유 거리"), Min(0.001f)]
    private float _reflectionSurfaceOffset = 0.02f;

    [SerializeField, InspectorName("명중 후 표시 시간"), Min(0f)]
    private float _impactVisibilityTime = 0.08f;

    [SerializeField, InspectorName("명중 효과음")]
    private AudioClip _impactAudio;

    private GameObject _caster;
    private GameObject _target;
    private Rigidbody _targetBody;
    private float _lifetime;
    private float _speed;
    private float _turnRate;
    private float _hitDistance;
    private int _damage;
    private float _knockbackForce;
    private bool _canApplyDamage;
    private bool _isInitialized;
    private bool _hasHit;
    private bool _isStraightPhase;
    private float _elapsedTime;
    private Action<Vector3, Quaternion> _straightPhaseCompleted;
    private Material _coreMaterial;
    private Material _glowMaterial;
    private TrailRenderer[] _trails;
    private readonly List<Vector3> _pathPoints = new();
    private Transform[] _coreSegments;
    private Transform[] _glowSegments;

    public void Initialize(
        GameObject caster,
        GameObject target,
        float lifetime,
        float speed,
        float turnRate,
        float hitDistance,
        int damage,
        float knockbackForce,
        bool canApplyDamage
    )
    {
        _caster = caster;
        _target = target;
        _targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
        _lifetime = Mathf.Max(0.01f, lifetime);
        _speed = Mathf.Max(0.01f, speed);
        _turnRate = Mathf.Max(0f, turnRate);
        _hitDistance = Mathf.Max(0.01f, hitDistance);
        _damage = Mathf.Max(0, damage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
        _canApplyDamage = canApplyDamage;
        _isStraightPhase = false;
        _hasHit = false;
        _elapsedTime = 0f;
        _isInitialized = _caster != null && _target != null;

        if (_isInitialized)
        {
            CreateVisuals();
            InitializeVolumePath();
        }
        else
            Destroy(gameObject);
    }

    public void InitializeStraight(
        GameObject caster,
        float duration,
        float speed,
        Action<Vector3, Quaternion> phaseCompleted
    )
    {
        _caster = caster;
        _target = null;
        _targetBody = null;
        _lifetime = Mathf.Max(0.01f, duration);
        _speed = Mathf.Max(0.01f, speed);
        _canApplyDamage = false;
        _isStraightPhase = true;
        _hasHit = false;
        _elapsedTime = 0f;
        _straightPhaseCompleted = phaseCompleted;
        _isInitialized = _caster != null;

        if (_isInitialized)
        {
            CreateVisuals();
            InitializeVolumePath();
        }
        else
            Destroy(gameObject);
    }

    private void Update()
    {
        if (_hasHit)
            return;

        if (!_isInitialized)
        {
            Destroy(gameObject);
            return;
        }

        if (_isStraightPhase)
        {
            float remainingTime = Mathf.Max(0f, _lifetime - _elapsedTime);
            float movementTime = Mathf.Min(Time.deltaTime, remainingTime);
            MoveWithMirrorReflections(transform.forward, _speed * movementTime);
            _elapsedTime += Time.deltaTime;

            if (_elapsedTime >= _lifetime)
                CompleteStraightPhase();
            return;
        }

        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= _lifetime)
        {
            Destroy(gameObject);
            return;
        }

        if (_target == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPosition =
            _targetBody != null ? _targetBody.worldCenterOfMass : _target.transform.position;
        Vector3 toTarget = targetPosition - transform.position;
        float distanceToTarget = toTarget.magnitude;
        float moveDistance = _speed * Time.deltaTime;

        if (distanceToTarget <= _hitDistance)
        {
            transform.position = targetPosition;
            RecordVolumePath();
            HitTarget(toTarget);
            return;
        }

        Vector3 desiredDirection = toTarget / distanceToTarget;
        Vector3 currentDirection = transform.forward;
        Vector3 direction = Vector3.RotateTowards(
            currentDirection,
            desiredDirection,
            _turnRate * Mathf.Deg2Rad * Time.deltaTime,
            0f
        ).normalized;

        bool canReachTarget = distanceToTarget <= _hitDistance + moveDistance;
        float travelDistance = canReachTarget
            ? Mathf.Min(moveDistance, distanceToTarget)
            : moveDistance;
        bool wasReflected = MoveWithMirrorReflections(direction, travelDistance);

        if (!wasReflected && canReachTarget)
        {
            transform.position = targetPosition;
            RecordVolumePath();
            HitTarget(toTarget);
        }
    }

    private bool MoveWithMirrorReflections(Vector3 direction, float distance)
    {
        if (distance <= Mathf.Epsilon || direction.sqrMagnitude <= Mathf.Epsilon)
            return false;

        Vector3 currentDirection = direction.normalized;
        Vector3 currentPosition = transform.position;
        float remainingDistance = distance;
        float castRadius = Mathf.Max(0.01f, _coreWidth * 0.5f);
        int reflectionCount = 0;

        while (
            remainingDistance > Mathf.Epsilon
            && reflectionCount < MaxReflectionsPerFrame
        )
        {
            bool hitSurface = Physics.SphereCast(
                currentPosition,
                castRadius,
                currentDirection,
                out RaycastHit hit,
                remainingDistance,
                _mirrorLayers,
                QueryTriggerInteraction.Ignore
            );
            Mirror mirror = hitSurface ? hit.collider.GetComponentInParent<Mirror>() : null;
            if (!hitSurface || mirror == null)
            {
                currentPosition += currentDirection * remainingDistance;
                remainingDistance = 0f;
                break;
            }

            Vector3 reflectionPoint = currentPosition + currentDirection * hit.distance;
            mirror.PlayReflectionAudio(reflectionPoint);
            AddVolumePathPoint(reflectionPoint, false);
            remainingDistance = Mathf.Max(0f, remainingDistance - hit.distance);
            currentDirection = Vector3.Reflect(currentDirection, hit.normal).normalized;
            float offsetDistance = Mathf.Min(
                _reflectionSurfaceOffset,
                remainingDistance
            );
            currentPosition = reflectionPoint + currentDirection * offsetDistance;
            remainingDistance -= offsetDistance;
            reflectionCount++;
        }

        transform.SetPositionAndRotation(
            currentPosition,
            GetDirectionRotation(currentDirection)
        );
        RecordVolumePath();
        return reflectionCount > 0;
    }

    private static Quaternion GetDirectionRotation(Vector3 direction)
    {
        Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.98f
            ? Vector3.right
            : Vector3.up;
        return Quaternion.LookRotation(direction, up);
    }

    private void CompleteStraightPhase()
    {
        _isInitialized = false;
        _straightPhaseCompleted?.Invoke(transform.position, transform.rotation);
        _straightPhaseCompleted = null;
        Destroy(gameObject);
    }

    private void HitTarget(Vector3 toTarget)
    {
        _hasHit = true;
        AudioManager.SfxPlayAtPoint(_impactAudio, transform.position);

        if (_canApplyDamage && _damage > 0 && _target.TryGetComponent(out IDamageable damageable))
        {
            Vector3 knockbackDirection =
                toTarget.sqrMagnitude > Mathf.Epsilon ? toTarget.normalized : transform.forward;
            damageable.TakeDamage(_damage, knockbackDirection * _knockbackForce);
        }

        if (_trails != null)
        {
            foreach (TrailRenderer trail in _trails)
            {
                if (trail != null)
                    trail.emitting = false;
            }
        }

        Destroy(gameObject, Mathf.Max(_impactVisibilityTime, Time.deltaTime));
    }

    private void CreateVisuals()
    {
        Shader shader = Shader.Find(AdditiveShaderName);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            return;

        _coreMaterial = CreateMaterial(shader, _coreColor);
        _glowMaterial = CreateMaterial(shader, _glowColor);
        CreateTrail("Glow", _glowWidth * TrailWidthScale, _glowColor, _glowMaterial, 0);
        CreateTrail("Core", _coreWidth * TrailWidthScale, _coreColor, _coreMaterial, 1);
        _glowSegments = CreateVolumeSegments("GlowBody", _glowMaterial, 0);
        _coreSegments = CreateVolumeSegments("CoreBody", _coreMaterial, 1);
        _trails = GetComponentsInChildren<TrailRenderer>(true);
    }

    private static Material CreateMaterial(Shader shader, Color color)
    {
        Material material = new(shader)
        {
            name = "HomingBeam_RuntimeMaterial",
            hideFlags = HideFlags.HideAndDontSave,
        };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Intensity"))
            material.SetFloat("_Intensity", 3f);
        return material;
    }

    private void CreateTrail(
        string objectName,
        float width,
        Color color,
        Material material,
        int sortingOrder
    )
    {
        GameObject trailObject = new(objectName);
        trailObject.transform.SetParent(transform, false);

        TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
        trail.time = _trailTime;
        trail.minVertexDistance = 0.03f;
        trail.widthMultiplier = width;
        trail.sharedMaterial = material;
        trail.sortingOrder = sortingOrder;
        trail.emitting = true;

        Gradient gradient = new();
        gradient.SetKeys(
            new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) }
        );
        trail.colorGradient = gradient;
    }

    private Transform[] CreateVolumeSegments(
        string objectName,
        Material material,
        int sortingOrder
    )
    {
        int segmentCount = Mathf.Clamp(_volumeSegmentCount, 4, 32);
        Transform[] segments = new Transform[segmentCount];

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject segmentObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            segmentObject.name = $"{objectName}_{i}";
            segmentObject.layer = gameObject.layer;
            segmentObject.transform.SetParent(transform, false);

            Collider collider = segmentObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }

            MeshRenderer meshRenderer = segmentObject.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            segments[i] = segmentObject.transform;
        }

        return segments;
    }

    private void InitializeVolumePath()
    {
        _pathPoints.Clear();
        int segmentCount = Mathf.Clamp(_volumeSegmentCount, 4, 32);

        for (int i = segmentCount; i >= 0; i--)
        {
            float distance = _bodyLength * i / segmentCount;
            _pathPoints.Add(transform.position - transform.forward * distance);
        }

        UpdateVolumeSegments();
    }

    private void RecordVolumePath()
    {
        if (_pathPoints.Count == 0)
        {
            InitializeVolumePath();
            return;
        }

        AddVolumePathPoint(transform.position, true);
    }

    private void AddVolumePathPoint(Vector3 currentPosition, bool updateSegments)
    {
        int lastIndex = _pathPoints.Count - 1;
        if ((_pathPoints[lastIndex] - currentPosition).sqrMagnitude <= Mathf.Epsilon)
            _pathPoints[lastIndex] = currentPosition;
        else
            _pathPoints.Add(currentPosition);

        TrimVolumePath();
        if (updateSegments)
            UpdateVolumeSegments();
    }

    private void TrimVolumePath()
    {
        while (_pathPoints.Count > 2 && GetPathLengthFrom(2) >= _bodyLength)
            _pathPoints.RemoveAt(0);
    }

    private float GetPathLengthFrom(int startIndex)
    {
        float length = 0f;
        for (int i = Mathf.Max(1, startIndex); i < _pathPoints.Count; i++)
            length += Vector3.Distance(_pathPoints[i - 1], _pathPoints[i]);
        return length;
    }

    private void UpdateVolumeSegments()
    {
        if (_coreSegments == null || _glowSegments == null)
            return;

        int segmentCount = Mathf.Min(_coreSegments.Length, _glowSegments.Length);
        float segmentLength = _bodyLength / segmentCount;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 front = SamplePath(i * segmentLength);
            Vector3 back = SamplePath((i + 1) * segmentLength);
            UpdateVolumeSegment(_glowSegments[i], front, back, _glowWidth);
            UpdateVolumeSegment(_coreSegments[i], front, back, _coreWidth);
        }
    }

    private Vector3 SamplePath(float distanceBehindHead)
    {
        float remainingDistance = Mathf.Max(0f, distanceBehindHead);

        for (int i = _pathPoints.Count - 1; i > 0; i--)
        {
            Vector3 front = _pathPoints[i];
            Vector3 back = _pathPoints[i - 1];
            float sectionLength = Vector3.Distance(front, back);
            if (sectionLength <= Mathf.Epsilon)
                continue;

            if (remainingDistance <= sectionLength)
                return Vector3.Lerp(front, back, remainingDistance / sectionLength);

            remainingDistance -= sectionLength;
        }

        return _pathPoints[0];
    }

    private static void UpdateVolumeSegment(
        Transform segment,
        Vector3 front,
        Vector3 back,
        float width
    )
    {
        Vector3 direction = front - back;
        float length = direction.magnitude;
        if (length <= Mathf.Epsilon)
        {
            segment.gameObject.SetActive(false);
            return;
        }

        if (!segment.gameObject.activeSelf)
            segment.gameObject.SetActive(true);

        Vector3 forward = direction / length;
        Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f
            ? Vector3.right
            : Vector3.up;
        segment.SetPositionAndRotation(
            (front + back) * 0.5f,
            Quaternion.LookRotation(forward, up)
        );
        segment.localScale = new Vector3(width, width, length + width * 0.35f);
    }

    private void OnDestroy()
    {
        if (_coreMaterial != null)
            Destroy(_coreMaterial);
        if (_glowMaterial != null)
            Destroy(_glowMaterial);
    }
}
// HomingBeamEffect은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
