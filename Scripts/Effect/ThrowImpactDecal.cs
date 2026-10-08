using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>Local aiming preview using the project's URP decal shader.</summary>
public sealed class ThrowImpactDecal : MonoBehaviour
{
    [SerializeField] private DecalProjector _projector;
    [SerializeField, Min(0f)] private float _surfaceOffset = 0.06f;

    public void Show(RaycastHit hit)
    {
        if (_projector == null) return;
        // URP projects along local +Z into the surface, including walls and slopes.
        transform.SetPositionAndRotation(hit.point + hit.normal * _surfaceOffset,
            Quaternion.LookRotation(-hit.normal, Mathf.Abs(hit.normal.y) > 0.99f ? Vector3.forward : Vector3.up));
        if (!_projector.enabled) _projector.enabled = true;
    }

    public void Hide()
    {
        if (_projector != null && _projector.enabled) _projector.enabled = false;
    }

}
