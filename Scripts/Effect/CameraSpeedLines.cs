using UnityEngine;

[DisallowMultipleComponent]
public sealed class CameraSpeedLines : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float _emissionDistance = 12f;
    [SerializeField, Range(0f, 1f)] private float _viewEdgeRadiusRatio = 0.88f;

    private Camera _camera;
    [SerializeField] private ParticleSystem _particles;

    public void Initialize(Camera camera) => _camera = camera;

    public void Play(float duration)
    {
        if (_particles == null)
            return;

        ConfigureEdgeEmission();
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = _particles.main;
        main.duration = Mathf.Max(0.05f, duration);
        _particles.Play(true);
    }

    public void Stop()
    {
        if (_particles != null)
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void ConfigureEdgeEmission()
    {
        if (_particles == null || _camera == null)
            return;

        float verticalRadius =
            Mathf.Tan(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad)
            * _emissionDistance
            * _viewEdgeRadiusRatio;

        ParticleSystem.ShapeModule shape = _particles.shape;
        shape.radius = verticalRadius;
        shape.position = Vector3.forward * _emissionDistance;
        shape.scale = new Vector3(Mathf.Max(1f, _camera.aspect), 1f, 1f);
    }

}
