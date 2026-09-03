using System;
using UnityEngine;
using Random = System.Random;

[DisallowMultipleComponent]
public sealed class DivinePunishmentStrikeEffect : MonoBehaviour
{
    private const string AdditiveShaderName = "Flash/Divine Punishment Additive";
    private const int BranchCount = 2;
    private const int RingSegments = 64;

    [Header("크기")]
    [SerializeField, InspectorName("기준 공격 반경"), Min(0.1f)]
    [Tooltip("이 프리팹이 원래 크기로 재생되는 천벌의 공격 반경입니다.")]
    private float _referenceStrikeRadius = 5f;

    [Header("낙뢰")]
    [SerializeField, InspectorName("번개 높이"), Min(1f)]
    private float _boltHeight = 18f;

    [SerializeField, InspectorName("번개 구간 수"), Range(6, 32)]
    private int _boltSegments = 16;

    [SerializeField, InspectorName("번개 흔들림"), Min(0f)]
    private float _boltJitter = 0.75f;

    [SerializeField, InspectorName("중심 굵기"), Min(0.01f)]
    private float _coreWidth = 0.16f;

    [SerializeField, InspectorName("광채 굵기"), Min(0.01f)]
    private float _glowWidth = 0.8f;

    [SerializeField, InspectorName("번개 표시 시간"), Min(0.01f)]
    private float _boltVisibleDuration = 0.24f;

    [SerializeField, InspectorName("경로 재생성 간격"), Min(0.01f)]
    private float _boltRefreshInterval = 0.045f;

    [SerializeField, InspectorName("중심 색상")]
    private Color _coreColor = new(0.85f, 0.96f, 1f, 1f);

    [SerializeField, InspectorName("광채 색상")]
    private Color _glowColor = new(0.18f, 0.52f, 1f, 0.55f);

    [Header("충돌")]
    [SerializeField, InspectorName("지면 링 반경"), Min(0.1f)]
    private float _groundRingRadius = 4.5f;

    [SerializeField, InspectorName("지면 링 지속 시간"), Min(0.01f)]
    private float _groundRingDuration = 0.55f;

    [SerializeField, InspectorName("광원 세기"), Min(0f)]
    private float _lightPeakIntensity = 12f;

    [SerializeField, InspectorName("광원 범위"), Min(0f)]
    private float _lightRange = 13f;

    [Header("수명과 오디오")]
    [SerializeField, InspectorName("전체 수명"), Min(0.1f)]
    private float _effectDuration = 1.25f;

    [SerializeField, InspectorName("낙뢰 효과음")]
    private AudioClip _strikeAudio;

    private readonly LineRenderer[] _branchCores = new LineRenderer[BranchCount];
    private readonly LineRenderer[] _branchGlows = new LineRenderer[BranchCount];

    private Random _random;
    private Vector3[] _boltPositions;
    private LineRenderer _boltCore;
    private LineRenderer _boltGlow;
    private LineRenderer _groundRing;
    private Light _flashLight;
    private Material _coreMaterial;
    private Material _glowMaterial;
    private Material _particleMaterial;
    private Material _dustMaterial;
    private Texture2D _softParticleTexture;
    private float _elapsedTime;
    private float _nextBoltRefreshTime;
    private float _visualScale = 1f;

    public void Initialize(float strikeRadius)
    {
        _visualScale = Mathf.Max(0.1f, strikeRadius / Mathf.Max(0.1f, _referenceStrikeRadius));
    }

    private void Start()
    {
        _random = new Random(unchecked(GetEntityId().GetHashCode() * 397 ^ Environment.TickCount));
        _boltPositions = new Vector3[Mathf.Max(2, _boltSegments)];

        CreateRuntimeMaterials();
        CreateBoltRenderers();
        CreateGroundRing();
        CreateImpactParticles();
        CreateFlashLight();
        PlayStrikeAudio();

        RefreshBoltPath();
        UpdateGroundRing(0f);
        Destroy(gameObject, _effectDuration);
    }

    private void Update()
    {
        _elapsedTime += Time.deltaTime;

        if (_elapsedTime < _boltVisibleDuration)
        {
            if (_elapsedTime >= _nextBoltRefreshTime)
            {
                RefreshBoltPath();
                _nextBoltRefreshTime += _boltRefreshInterval;
            }

            float normalizedTime = Mathf.Clamp01(_elapsedTime / _boltVisibleDuration);
            float flicker = 0.55f + Mathf.Abs(Mathf.Sin(_elapsedTime * 95f)) * 0.45f;
            SetBoltVisibility((1f - normalizedTime) * flicker);
        }
        else
        {
            SetBoltRenderersEnabled(false);
        }

        UpdateGroundRing(Mathf.Clamp01(_elapsedTime / _groundRingDuration));
        UpdateFlashLight();
    }

    private void CreateRuntimeMaterials()
    {
        Shader shader = Shader.Find(AdditiveShaderName);
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        _softParticleTexture = CreateSoftParticleTexture();
        _coreMaterial = CreateMaterial(shader, _coreColor, 6f, null);
        _glowMaterial = CreateMaterial(shader, _glowColor, 2.5f, null);
        _particleMaterial = CreateMaterial(shader, Color.white, 3.5f, _softParticleTexture);
        _dustMaterial = CreateMaterial(shader, Color.white, 0.55f, _softParticleTexture);
    }

    private static Material CreateMaterial(
        Shader shader,
        Color color,
        float intensity,
        Texture texture
    )
    {
        if (shader == null)
            return null;

        Material material = new(shader)
        {
            name = "DivinePunishment_RuntimeMaterial",
            hideFlags = HideFlags.HideAndDontSave,
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_Intensity"))
            material.SetFloat("_Intensity", intensity);
        if (texture != null)
        {
            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", texture);
        }

        return material;
    }

    private void CreateBoltRenderers()
    {
        _boltGlow = CreateLineRenderer("BoltGlow", _glowMaterial, Scale(_glowWidth), 1);
        _boltCore = CreateLineRenderer("BoltCore", _coreMaterial, Scale(_coreWidth), 2);

        for (int i = 0; i < BranchCount; i++)
        {
            _branchGlows[i] = CreateLineRenderer(
                $"BranchGlow_{i + 1}",
                _glowMaterial,
                Scale(_glowWidth * 0.5f),
                1
            );
            _branchCores[i] = CreateLineRenderer(
                $"BranchCore_{i + 1}",
                _coreMaterial,
                Scale(_coreWidth * 0.65f),
                2
            );
        }
    }

    private LineRenderer CreateLineRenderer(
        string objectName,
        Material material,
        float width,
        int sortingOrder
    )
    {
        GameObject lineObject = new(objectName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 3;
        line.numCapVertices = 2;
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f),
            new Keyframe(0.08f, 1f),
            new Keyframe(1f, 0.15f)
        );
        line.sharedMaterial = material;
        line.sortingOrder = sortingOrder;
        return line;
    }

    private void RefreshBoltPath()
    {
        int segmentCount = _boltPositions.Length;
        for (int i = 0; i < segmentCount; i++)
        {
            float normalizedHeight = i / (float)(segmentCount - 1);
            float endpointWeight = Mathf.Sin(normalizedHeight * Mathf.PI);
            Vector2 jitter = RandomInsideUnitCircle() * Scale(_boltJitter * endpointWeight);
            _boltPositions[i] = new Vector3(
                jitter.x,
                normalizedHeight * Scale(_boltHeight),
                jitter.y
            );
        }

        SetLinePositions(_boltCore, _boltPositions);
        SetLinePositions(_boltGlow, _boltPositions);

        for (int i = 0; i < BranchCount; i++)
            RefreshBranch(i);
    }

    private void RefreshBranch(int branchIndex)
    {
        int startIndex = Mathf.RoundToInt(
            Mathf.Lerp(3f, _boltPositions.Length - 4f, (branchIndex + 1f) / (BranchCount + 1f))
        );
        startIndex = Mathf.Clamp(startIndex, 1, _boltPositions.Length - 2);

        const int branchSegments = 6;
        Vector3[] branchPositions = new Vector3[branchSegments];
        Vector2 horizontalDirection = RandomInsideUnitCircle().normalized;
        float branchLength = Scale(RandomRange(1.8f, 3.4f));
        Vector3 branchStart = _boltPositions[startIndex];

        for (int i = 0; i < branchSegments; i++)
        {
            float normalizedLength = i / (float)(branchSegments - 1);
            Vector2 jitter =
                RandomInsideUnitCircle() * Scale(_boltJitter * 0.25f * normalizedLength);
            branchPositions[i] =
                branchStart
                + new Vector3(horizontalDirection.x, -0.35f, horizontalDirection.y)
                    * (branchLength * normalizedLength)
                + new Vector3(jitter.x, 0f, jitter.y);
        }

        SetLinePositions(_branchCores[branchIndex], branchPositions);
        SetLinePositions(_branchGlows[branchIndex], branchPositions);
    }

    private static void SetLinePositions(LineRenderer line, Vector3[] positions)
    {
        line.positionCount = positions.Length;
        line.SetPositions(positions);
    }

    private void SetBoltVisibility(float alpha)
    {
        SetBoltRenderersEnabled(true);
        SetLineColor(_boltCore, _coreColor, alpha);
        SetLineColor(_boltGlow, _glowColor, alpha);

        for (int i = 0; i < BranchCount; i++)
        {
            SetLineColor(_branchCores[i], _coreColor, alpha * 0.8f);
            SetLineColor(_branchGlows[i], _glowColor, alpha * 0.65f);
        }
    }

    private void SetBoltRenderersEnabled(bool isEnabled)
    {
        _boltCore.enabled = isEnabled;
        _boltGlow.enabled = isEnabled;

        for (int i = 0; i < BranchCount; i++)
        {
            _branchCores[i].enabled = isEnabled;
            _branchGlows[i].enabled = isEnabled;
        }
    }

    private static void SetLineColor(LineRenderer line, Color color, float alpha)
    {
        Color startColor = color;
        startColor.a *= alpha;
        Color endColor = color;
        endColor.a *= alpha * 0.55f;
        line.startColor = startColor;
        line.endColor = endColor;
    }

    private void CreateGroundRing()
    {
        _groundRing = CreateLineRenderer("GroundRing", _glowMaterial, Scale(0.13f), 1);
        _groundRing.loop = true;
        _groundRing.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
        _groundRing.positionCount = RingSegments;
    }

    private void UpdateGroundRing(float normalizedTime)
    {
        if (_groundRing == null)
            return;

        if (normalizedTime >= 1f)
        {
            _groundRing.enabled = false;
            return;
        }

        _groundRing.enabled = true;
        float easedTime = 1f - Mathf.Pow(1f - normalizedTime, 3f);
        float radius = Mathf.Lerp(Scale(0.25f), Scale(_groundRingRadius), easedTime);

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i / (float)RingSegments * Mathf.PI * 2f;
            _groundRing.SetPosition(
                i,
                new Vector3(Mathf.Cos(angle) * radius, 0.035f, Mathf.Sin(angle) * radius)
            );
        }

        SetLineColor(_groundRing, _glowColor, (1f - normalizedTime) * 0.8f);
    }

    private void CreateImpactParticles()
    {
        ParticleSystem sparks = CreateBurstParticles(
            "Sparks",
            44,
            new Color(0.65f, 0.9f, 1f, 1f),
            new Vector2(0.25f, 0.7f),
            Scale(new Vector2(5f, 11f)),
            Scale(new Vector2(0.035f, 0.11f)),
            1.1f,
            68f,
            0.2f,
            _particleMaterial
        );
        ParticleSystemRenderer sparkRenderer = sparks.GetComponent<ParticleSystemRenderer>();
        sparkRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        sparkRenderer.velocityScale = 0.08f;
        sparkRenderer.lengthScale = 2.5f;

        CreateBurstParticles(
            "ImpactFlash",
            14,
            new Color(0.8f, 0.96f, 1f, 0.9f),
            new Vector2(0.08f, 0.2f),
            Scale(new Vector2(0.5f, 3f)),
            Scale(new Vector2(0.45f, 1.25f)),
            0f,
            80f,
            0.1f,
            _particleMaterial
        );

        CreateBurstParticles(
            "GroundMist",
            22,
            new Color(0.35f, 0.43f, 0.5f, 0.28f),
            new Vector2(0.55f, 1f),
            Scale(new Vector2(0.8f, 2.6f)),
            Scale(new Vector2(0.45f, 1.35f)),
            0.12f,
            87f,
            Scale(0.6f),
            _dustMaterial
        );
    }

    private ParticleSystem CreateBurstParticles(
        string objectName,
        short particleCount,
        Color color,
        Vector2 lifetimeRange,
        Vector2 speedRange,
        Vector2 sizeRange,
        float gravityModifier,
        float coneAngle,
        float coneRadius,
        Material material
    )
    {
        GameObject particleObject = new(objectName);
        particleObject.SetActive(false);
        particleObject.transform.SetParent(transform, false);
        particleObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

        ParticleSystem particleSystem = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particleSystem.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Min(0.2f, _effectDuration);
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = color;
        main.gravityModifier = gravityModifier;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = particleCount;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, particleCount) });

        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = coneAngle;
        shape.radius = coneRadius;
        shape.length = 0.1f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.08f),
                new GradientAlphaKey(0f, 1f),
            }
        );
        colorOverLifetime.color = fadeGradient;

        ParticleSystemRenderer particleRenderer =
            particleSystem.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = material;
        particleRenderer.sortingOrder = 3;

        particleObject.SetActive(true);
        particleSystem.Play();
        return particleSystem;
    }

    private void CreateFlashLight()
    {
        GameObject lightObject = new("FlashLight");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = Vector3.up * Scale(1.5f);

        _flashLight = lightObject.AddComponent<Light>();
        _flashLight.type = LightType.Point;
        _flashLight.color = _glowColor;
        _flashLight.range = Scale(_lightRange);
        _flashLight.intensity = _lightPeakIntensity;
        _flashLight.shadows = LightShadows.None;
    }

    private void UpdateFlashLight()
    {
        if (_flashLight == null)
            return;

        float normalizedTime = Mathf.Clamp01(_elapsedTime / Mathf.Max(0.01f, _boltVisibleDuration));
        _flashLight.intensity = _lightPeakIntensity * Mathf.Pow(1f - normalizedTime, 2f);
        _flashLight.enabled = normalizedTime < 1f;
    }

    private void PlayStrikeAudio()
    {
        if (_strikeAudio == null)
            return;

        AudioSource audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.clip = _strikeAudio;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
        audioSource.minDistance = Scale(4f);
        audioSource.maxDistance = Scale(55f);
        audioSource.Play();
    }

    private Vector2 RandomInsideUnitCircle()
    {
        float angle = RandomRange(0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt(RandomRange(0f, 1f));
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    private float RandomRange(float min, float max)
    {
        return Mathf.Lerp(min, max, (float)_random.NextDouble());
    }

    private float Scale(float value) => value * _visualScale;

    private Vector2 Scale(Vector2 value) => value * _visualScale;

    private static Texture2D CreateSoftParticleTexture()
    {
        const int textureSize = 32;
        Texture2D texture = new(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            name = "DivinePunishment_SoftParticle",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave,
        };

        Color[] pixels = new Color[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                Vector2 position = new(
                    (x + 0.5f) / textureSize * 2f - 1f,
                    (y + 0.5f) / textureSize * 2f - 1f
                );
                float alpha = Mathf.Clamp01(1f - position.magnitude);
                alpha = alpha * alpha * (3f - 2f * alpha);
                pixels[y * textureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(_coreMaterial);
        DestroyRuntimeObject(_glowMaterial);
        DestroyRuntimeObject(_particleMaterial);
        DestroyRuntimeObject(_dustMaterial);
        DestroyRuntimeObject(_softParticleTexture);
    }

    private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
    {
        if (runtimeObject != null)
            Destroy(runtimeObject);
    }

    private void OnValidate()
    {
        _effectDuration = Mathf.Max(_effectDuration, _boltVisibleDuration, _groundRingDuration);
        _boltRefreshInterval = Mathf.Max(0.01f, _boltRefreshInterval);
        _boltSegments = Mathf.Max(6, _boltSegments);
    }
}
// DivinePunishmentStrikeEffect은 런타임 시각 효과의 생성, 갱신 및 정리 수명주기를 담당한다.
// 게임 판정과 표현을 분리하여 효과가 종료되거나 비활성화될 때 리소스가 안전하게 정리되도록 한다.
