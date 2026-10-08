using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HomingBeamEffect : CancellableSkillEffect
{
    private const int MaxReflectionsPerFrame = 8;
    private static readonly Dictionary<GameObject, HomingBeamEffect> ActiveSearchingBeams = new();

    private Color _coreColor = new(0.8f, 1f, 1f, 1f);

    private Color _glowColor = new(0.1f, 0.65f, 1f, 0.65f);

    [SerializeField, InspectorName("중심 광선 너비"), Min(0.001f)]
    private float _coreWidth = 0.6f;

    [SerializeField, InspectorName("외곽 광선 너비"), Min(0.001f)]
    private float _glowWidth = 1.9f;

    [SerializeField, InspectorName("광선 몸체 길이"), Min(0.01f)]
    private float _bodyLength = 4.75f;

    [SerializeField, InspectorName("거울 레이어")]
    private LayerMask _mirrorLayers = 1 << 8;

    [SerializeField, InspectorName("반사 표면 여유 거리"), Min(0.001f)]
    private float _reflectionSurfaceOffset = 0.02f;

    [SerializeField, InspectorName("명중 후 표시 시간"), Min(0f)]
    private float _impactVisibilityTime = 0.08f;

    [SerializeField, InspectorName("자연 소멸 시간"), Min(0.01f)]
    private float _dissolveDuration = 0.4f;

    [SerializeField, InspectorName("자연 소멸 분산 거리"), Min(0f)]
    private float _dissolveScatterDistance = 1.5f;

    [SerializeField, InspectorName("명중 폭발 시간"), Min(0.01f)]
    private float _impactExplosionDuration = 0.45f;

    [SerializeField, InspectorName("명중 효과음")]
    private AudioClip _impactAudio;

    private GameObject _caster;
    private GameObject _target;
    private Rigidbody _targetBody;
    private float _lifetime;
    private float _speed;
    private float _turnRate;
    private float _homingAcceleration;
    private float _hitDistance;
    private int _damage;
    private float _knockbackForce;
    // 판정 반경과 광선의 길이·굵기에 같은 검 배율을 적용해 시각 크기를 맞춘다.
    private float _swordSize = 1f;
    private float CoreWidth => _coreWidth * _swordSize;
    private float GlowWidth => _glowWidth * _swordSize;
    private float BodyLength => _bodyLength * _swordSize;
    private bool _canApplyDamage;
    private bool _isInitialized;
    private bool _hasHit;
    private bool _isStraightPhase;
    private bool _isSearchingPhase;
    private bool _isEnding;
    private float _elapsedTime;
    private float _endingElapsedTime;
    private float _endingDuration;
    private Vector3 _velocity;
    private Action<Vector3, Quaternion> _straightPhaseCompleted;
    private Func<Vector3, Quaternion, bool> _targetSearch;
    [SerializeField] private Material _coreMaterialTemplate;
    [SerializeField] private Material _glowMaterialTemplate;
    [SerializeField] private ParticleSystem _impactParticles;
    private Material _coreMaterial;
    private Material _glowMaterial;
    [SerializeField] private TrailRenderer[] _trails;
    private readonly List<Vector3> _pathPoints = new();
    [SerializeField] private Transform[] _coreSegments;
    [SerializeField] private Transform[] _glowSegments;
    private Transform[] _dissolveSegments;
    private Vector3[] _dissolveStartPositions;
    private Vector3[] _dissolveStartScales;
    private Vector3[] _dissolveDirections;

    public void Initialize(
        GameObject caster,
        GameObject target,
        float lifetime,
        float speed,
        float turnRate,
        float homingAcceleration,
        float hitDistance,
        int damage,
        float knockbackForce,
        bool canApplyDamage,
        float swordSize
    )
    {
        _caster = caster;
        BindCaster(caster);
        _target = target;
        _targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
        _lifetime = Mathf.Max(0.01f, lifetime);
        _speed = Mathf.Max(0.01f, speed);
        _turnRate = Mathf.Max(0f, turnRate);
        _homingAcceleration = Mathf.Max(0.01f, homingAcceleration);
        _hitDistance = Mathf.Max(0.01f, hitDistance);
        _damage = Mathf.Max(0, damage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
        _swordSize = Mathf.Max(0.01f, swordSize);
        _canApplyDamage = canApplyDamage;
        _isStraightPhase = false;
        _isSearchingPhase = false;
        _isEnding = false;
        _hasHit = false;
        _elapsedTime = 0f;
        _velocity = transform.forward * _speed;
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
        Action<Vector3, Quaternion> phaseCompleted,
        float swordSize
    )
    {
        _caster = caster;
        BindCaster(caster);
        _target = null;
        _targetBody = null;
        _lifetime = Mathf.Max(0.01f, duration);
        _speed = Mathf.Max(0.01f, speed);
        _swordSize = Mathf.Max(0.01f, swordSize);
        _canApplyDamage = false;
        _isStraightPhase = true;
        _isSearchingPhase = false;
        _isEnding = false;
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

    public void InitializeSearching(
        GameObject caster,
        float lifetime,
        float speed,
        Func<Vector3, Quaternion, bool> targetSearch,
        float swordSize
    )
    {
        _caster = caster;
        BindCaster(caster);
        _target = null;
        _targetBody = null;
        _lifetime = Mathf.Max(0.01f, lifetime);
        _speed = Mathf.Max(0.01f, speed);
        _swordSize = Mathf.Max(0.01f, swordSize);
        _canApplyDamage = false;
        _isStraightPhase = true;
        _isSearchingPhase = true;
        _isEnding = false;
        _hasHit = false;
        _elapsedTime = 0f;
        _targetSearch = targetSearch;
        _isInitialized = _caster != null;

        if (!_isInitialized)
        {
            Destroy(gameObject);
            return;
        }

        RegisterSearchingBeam();
        CreateVisuals();
        InitializeVolumePath();
    }

    public static void StopActiveSearchingBeam(GameObject caster)
    {
        if (
            caster == null
            || !ActiveSearchingBeams.TryGetValue(caster, out HomingBeamEffect effect)
            || effect == null
        )
        {
            return;
        }

        effect.StopForHomingTransition();
    }

    protected override void UpdateActiveEffect()
    {
        if (_isEnding)
        {
            UpdateEnding();
            return;
        }

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
            {
                if (_isSearchingPhase)
                    BeginDissolve();
                else
                    CompleteStraightPhase();
                return;
            }

            if (
                _isSearchingPhase
                && _targetSearch != null
                && _targetSearch.Invoke(transform.position, transform.rotation)
            )
            {
                StopForHomingTransition();
                return;
            }
            return;
        }

        _elapsedTime += Time.deltaTime;
        if (_elapsedTime >= _lifetime)
        {
            BeginDissolve();
            return;
        }

        if (_target == null)
        {
            BeginDissolve();
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

        Vector3 currentDirection =
            _velocity.sqrMagnitude > Mathf.Epsilon ? _velocity.normalized : transform.forward;
        Vector3 limitedTargetDirection = Vector3.RotateTowards(
            currentDirection,
            toTarget / distanceToTarget,
            _turnRate * Mathf.Deg2Rad * Time.deltaTime,
            0f
        ).normalized;

        // 목표를 즉시 향하도록 회전시키지 않고 속도 벡터에 제한된 가속도를 적용한다.
        // 이 속도 벡터가 다음 프레임에도 유지되므로 급격한 방향 전환에서도 물리적인 관성이 남는다.
        Vector3 desiredVelocity = limitedTargetDirection * _speed;
        Vector3 steeredVelocity = Vector3.MoveTowards(
            _velocity,
            desiredVelocity,
            _homingAcceleration * Time.deltaTime
        );
        _velocity = steeredVelocity.sqrMagnitude > Mathf.Epsilon
            ? steeredVelocity.normalized * _speed
            : currentDirection * _speed;

        Vector3 direction = _velocity.normalized;
        moveDistance = _velocity.magnitude * Time.deltaTime;

        float forwardDistance = Vector3.Dot(toTarget, direction);
        float lateralSqrDistance = Mathf.Max(
            0f,
            toTarget.sqrMagnitude - forwardDistance * forwardDistance
        );
        bool canReachTarget = forwardDistance >= 0f
            && forwardDistance <= moveDistance + _hitDistance
            && lateralSqrDistance <= _hitDistance * _hitDistance;
        float travelDistance = canReachTarget
            ? Mathf.Min(moveDistance, forwardDistance)
            : moveDistance;
        bool wasReflected = MoveWithMirrorReflections(direction, travelDistance);
        _velocity = transform.forward * _velocity.magnitude;

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
        float castRadius = Mathf.Max(0.01f, CoreWidth * 0.5f);
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

    private void RegisterSearchingBeam()
    {
        if (_caster == null)
            return;

        if (
            ActiveSearchingBeams.TryGetValue(_caster, out HomingBeamEffect previous)
            && previous != null
            && previous != this
        )
        {
            Destroy(previous.gameObject);
        }

        ActiveSearchingBeams[_caster] = this;
    }

    private void UnregisterSearchingBeam()
    {
        if (
            _caster != null
            && ActiveSearchingBeams.TryGetValue(_caster, out HomingBeamEffect current)
            && current == this
        )
        {
            ActiveSearchingBeams.Remove(_caster);
        }
    }

    private void StopForHomingTransition()
    {
        _targetSearch = null;
        UnregisterSearchingBeam();
        Destroy(gameObject);
    }

    private void BeginDissolve()
    {
        if (_isEnding)
            return;

        _isEnding = true;
        _isInitialized = false;
        _targetSearch = null;
        _endingElapsedTime = 0f;
        _endingDuration = Mathf.Max(0.01f, _dissolveDuration);
        UnregisterSearchingBeam();
        StopTrailEmission();
        PrepareDissolveSegments();
    }

    private void UpdateEnding()
    {
        _endingElapsedTime += Time.deltaTime;
        float progress = Mathf.Clamp01(_endingElapsedTime / _endingDuration);

        if (_dissolveSegments != null)
        {
            float scatteredProgress = 1f - (1f - progress) * (1f - progress);
            for (int i = 0; i < _dissolveSegments.Length; i++)
            {
                Transform segment = _dissolveSegments[i];
                if (segment == null)
                    continue;

                segment.position =
                    _dissolveStartPositions[i]
                    + _dissolveDirections[i] * (_dissolveScatterDistance * scatteredProgress);
                segment.localScale = Vector3.Lerp(
                    _dissolveStartScales[i],
                    Vector3.zero,
                    progress
                );
            }

            SetVisualAlpha(1f - progress);
        }

        if (_endingElapsedTime >= _endingDuration)
            Destroy(gameObject);
    }

    private void PrepareDissolveSegments()
    {
        List<Transform> segments = new();
        AddActiveSegments(_coreSegments, segments);
        AddActiveSegments(_glowSegments, segments);

        _dissolveSegments = segments.ToArray();
        _dissolveStartPositions = new Vector3[_dissolveSegments.Length];
        _dissolveStartScales = new Vector3[_dissolveSegments.Length];
        _dissolveDirections = new Vector3[_dissolveSegments.Length];

        for (int i = 0; i < _dissolveSegments.Length; i++)
        {
            Transform segment = _dissolveSegments[i];
            _dissolveStartPositions[i] = segment.position;
            _dissolveStartScales[i] = segment.localScale;
            _dissolveDirections[i] = UnityEngine.Random.onUnitSphere;
        }
    }

    private static void AddActiveSegments(Transform[] source, List<Transform> destination)
    {
        if (source == null)
            return;

        foreach (Transform segment in source)
        {
            if (segment != null && segment.gameObject.activeSelf)
                destination.Add(segment);
        }
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

        StopTrailEmission();
        SetVolumeSegmentsActive(false);
        CreateImpactExplosion();
        _isEnding = true;
        _endingElapsedTime = 0f;
        _endingDuration = Mathf.Max(
            _impactVisibilityTime,
            Mathf.Max(_impactParticles != null ? _impactParticles.main.duration + _impactParticles.main.startLifetime.constantMax : _impactExplosionDuration, Time.deltaTime)
        );
    }

    private void StopTrailEmission()
    {
        if (_trails == null)
            return;

        foreach (TrailRenderer trail in _trails)
        {
            if (trail != null)
                trail.emitting = false;
        }
    }

    private void SetVolumeSegmentsActive(bool isActive)
    {
        SetSegmentsActive(_coreSegments, isActive);
        SetSegmentsActive(_glowSegments, isActive);
    }

    private static void SetSegmentsActive(Transform[] segments, bool isActive)
    {
        if (segments == null)
            return;

        foreach (Transform segment in segments)
        {
            if (segment != null)
                segment.gameObject.SetActive(isActive);
        }
    }

    private void CreateImpactExplosion()
    {
        if (_impactParticles == null) return;
        _impactParticles.Play();
    }

    private void SetVisualAlpha(float alphaMultiplier)
    {
        SetMaterialColor(_coreMaterial, _coreColor, alphaMultiplier);
        SetMaterialColor(_glowMaterial, _glowColor, alphaMultiplier);
    }

    private static void SetMaterialColor(Material material, Color color, float alphaMultiplier)
    {
        if (material == null)
            return;

        color.a *= Mathf.Clamp01(alphaMultiplier);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private void CreateVisuals()
    {
        if (_coreMaterial != null) return;
        if (_coreMaterialTemplate == null || _glowMaterialTemplate == null) return;
        _coreMaterial = new Material(_coreMaterialTemplate);
        _glowMaterial = new Material(_glowMaterialTemplate);
        _coreColor = ReadColor(_coreMaterial);
        _glowColor = ReadColor(_glowMaterial);
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == _coreMaterialTemplate) materials[i] = _coreMaterial;
                else if (materials[i] == _glowMaterialTemplate) materials[i] = _glowMaterial;
            }
            renderer.sharedMaterials = materials;
        }
        foreach (var trail in _trails) if (trail != null) trail.widthMultiplier *= _swordSize;
        if (_impactParticles != null)
        {
            var shape = _impactParticles.shape;
            shape.radius *= _swordSize;
        }
    }

    private static Color ReadColor(Material material) => material.HasProperty("_BaseColor")
        ? material.GetColor("_BaseColor") : material.GetColor("_Color");

    private void InitializeVolumePath()
    {
        _pathPoints.Clear();
        int segmentCount = Mathf.Max(1, Mathf.Min(_coreSegments.Length, _glowSegments.Length));

        for (int i = segmentCount; i >= 0; i--)
        {
            float distance = BodyLength * i / segmentCount;
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
        while (_pathPoints.Count > 2 && GetPathLengthFrom(2) >= BodyLength)
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
        float segmentLength = BodyLength / segmentCount;

        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 front = SamplePath(i * segmentLength);
            Vector3 back = SamplePath((i + 1) * segmentLength);
            UpdateVolumeSegment(_glowSegments[i], front, back, GlowWidth);
            UpdateVolumeSegment(_coreSegments[i], front, back, CoreWidth);
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
        UnregisterSearchingBeam();
        if (!Application.isPlaying) return;

        if (_coreMaterial != null)
            Destroy(_coreMaterial);
        if (_glowMaterial != null)
            Destroy(_glowMaterial);
    }
}
// HomingBeamEffect은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
