using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HomingBeamEffect : MonoBehaviour
{
    private const string AdditiveShaderName = "Flash/Divine Punishment Additive";
    private const float TrailWidthScale = 0.35f;
    private const int MaxReflectionsPerFrame = 8;
    private static readonly Dictionary<GameObject, HomingBeamEffect> ActiveSearchingBeams = new();

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

    [SerializeField, InspectorName("자연 소멸 시간"), Min(0.01f)]
    private float _dissolveDuration = 0.4f;

    [SerializeField, InspectorName("자연 소멸 분산 거리"), Min(0f)]
    private float _dissolveScatterDistance = 1.5f;

    [SerializeField, InspectorName("명중 폭발 시간"), Min(0.01f)]
    private float _impactExplosionDuration = 0.45f;

    [SerializeField, InspectorName("명중 폭발 입자 수"), Range(8, 128)]
    private int _impactParticleCount = 42;

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
    private Material _coreMaterial;
    private Material _glowMaterial;
    private TrailRenderer[] _trails;
    private readonly List<Vector3> _pathPoints = new();
    private Transform[] _coreSegments;
    private Transform[] _glowSegments;
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
        bool canApplyDamage
    )
    {
        _caster = caster;
        _target = target;
        _targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
        _lifetime = Mathf.Max(0.01f, lifetime);
        _speed = Mathf.Max(0.01f, speed);
        _turnRate = Mathf.Max(0f, turnRate);
        _homingAcceleration = Mathf.Max(0.01f, homingAcceleration);
        _hitDistance = Mathf.Max(0.01f, hitDistance);
        _damage = Mathf.Max(0, damage);
        _knockbackForce = Mathf.Max(0f, knockbackForce);
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
        Func<Vector3, Quaternion, bool> targetSearch
    )
    {
        _caster = caster;
        _target = null;
        _targetBody = null;
        _lifetime = Mathf.Max(0.01f, lifetime);
        _speed = Mathf.Max(0.01f, speed);
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

    private void Update()
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
            Mathf.Max(_impactExplosionDuration, Time.deltaTime)
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
        GameObject explosionObject = new("BlueImpactExplosion");
        explosionObject.layer = gameObject.layer;
        explosionObject.transform.SetParent(transform, false);

        ParticleSystem particles = explosionObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.duration = Mathf.Max(0.01f, _impactExplosionDuration);
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            _impactExplosionDuration * 0.45f,
            _impactExplosionDuration
        );
        main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 16f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.42f);
        main.startColor = new ParticleSystem.MinMaxGradient(_glowColor, _coreColor);
        main.maxParticles = Mathf.Max(8, _impactParticleCount);

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(
            new[]
            {
                new ParticleSystem.Burst(
                    0f,
                    (short)Mathf.Clamp(_impactParticleCount, 8, short.MaxValue)
                ),
            }
        );

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.05f, _coreWidth * 0.5f);

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.25f, 0.7f, 1f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.75f, 0.35f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        colorOverLifetime.color = fadeGradient;

        ParticleSystemRenderer particleRenderer =
            explosionObject.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.sharedMaterial = _glowMaterial;
        particleRenderer.sortingOrder = 2;
        particles.Play();
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
        UnregisterSearchingBeam();

        if (_coreMaterial != null)
            Destroy(_coreMaterial);
        if (_glowMaterial != null)
            Destroy(_glowMaterial);
    }
}
// HomingBeamEffect은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
