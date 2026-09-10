using UnityEngine;
using UnityEngine.Serialization;

public sealed class AttackHitEffect : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField, Min(0f)]
    private float _movementDuration = 0.05f;

    [SerializeField, Min(0.01f)]
    private float _fadeDuration = 0.08f;

    [Header("Particle Layers")]
    [SerializeField, FormerlySerializedAs("_electricParticles")]
    private ParticleSystem[] _movingParticles;

    private readonly ParticleSystem.Particle[] _particleBuffer = new ParticleSystem.Particle[64];
    private float _elapsedTime;
    private float _totalDuration;
    private bool _isPlaying;
    private bool _motionStopped;

    public float MovementDuration => Mathf.Max(0f, _movementDuration);

    public void Play(float holdDuration = 0f)
    {
        float movementDuration = MovementDuration;
        float fadeDuration = Mathf.Max(0.01f, _fadeDuration);
        float clampedHoldDuration = Mathf.Max(0f, holdDuration);
        _totalDuration = movementDuration + clampedHoldDuration + fadeDuration;
        _elapsedTime = 0f;
        _motionStopped = false;
        _isPlaying = true;

        float fadeStart = Mathf.Clamp01(
            (movementDuration + clampedHoldDuration) / _totalDuration
        );
        ConfigureAndPlay(_movingParticles, fadeStart);
    }

    private void Start()
    {
        if (!_isPlaying)
            Play();
    }

    private void Update()
    {
        if (!_isPlaying)
            return;

        _elapsedTime += Time.deltaTime;

        if (!_motionStopped && _elapsedTime >= MovementDuration)
        {
            StopParticleMotion();
            _motionStopped = true;
        }

        if (_elapsedTime >= _totalDuration)
            Destroy(gameObject);
    }

    private void ConfigureAndPlay(ParticleSystem[] particles, float fadeStart)
    {
        if (particles == null)
            return;

        for (int i = 0; i < particles.Length; i++)
            ConfigureAndPlay(particles[i], fadeStart);
    }

    private void ConfigureAndPlay(ParticleSystem particles, float fadeStart)
    {
        if (particles == null)
            return;

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = _totalDuration;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient fadeGradient = new();
        fadeGradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, fadeStart),
                new GradientAlphaKey(0.35f, Mathf.Lerp(fadeStart, 1f, 0.7f)),
                new GradientAlphaKey(0f, 1f),
            }
        );
        colorOverLifetime.color = fadeGradient;

        ParticleSystem.TrailModule trails = particles.trails;
        if (trails.enabled)
        {
            // Keep every trail point alive until the effect is removed so the stopped
            // lightning silhouette fades in place instead of retracting from the tail.
            trails.lifetime = new ParticleSystem.MinMaxCurve(_totalDuration + 0.01f);
            trails.dieWithParticles = true;
            trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(fadeGradient);
        }

        particles.Play(true);
    }

    private void StopParticleMotion()
    {
        if (_movingParticles == null)
            return;

        for (int i = 0; i < _movingParticles.Length; i++)
        {
            ParticleSystem particles = _movingParticles[i];
            if (particles == null)
                continue;

            int count = particles.GetParticles(_particleBuffer);
            for (int particleIndex = 0; particleIndex < count; particleIndex++)
                _particleBuffer[particleIndex].velocity = Vector3.zero;

            particles.SetParticles(_particleBuffer, count);

            // Setting velocity to zero does not stop the Noise module from applying
            // fresh displacement on the following simulation frames.
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = false;
        }
    }
}
