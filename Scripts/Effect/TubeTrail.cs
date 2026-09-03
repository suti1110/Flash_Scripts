using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TubeTrail : MonoBehaviour
{
    [Header("Tube Settings")]
    [SerializeField]
    private float _radius = 0.1f;

    [SerializeField]
    private int _sides = 8;

    [SerializeField]
    private int _maxPoints = 50;

    [SerializeField]
    private float _minDistance = 0.1f;

    [Header("Fade Settings")]
    [SerializeField]
    private float _trailLifetime = 1.0f;

    [Header("Color")]
    [ColorUsage(true, true)]
    [SerializeField]
    private Color _trailColor = Color.white;

    [Header("Offset")]
    [SerializeField]
    private Vector3 _trailOffset = Vector3.zero; // 로컬 기준 오프셋

    private MeshFilter _meshFilter;
    private Mesh _mesh;
    private readonly List<Vector3> _points = new();
    private readonly List<float> _pointTimes = new();

    private readonly List<Vector3> _vertices = new();
    private readonly List<int> _triangles = new();
    private readonly List<Vector2> _uvs = new();
    private readonly List<Color> _colors = new();
    private Vector3[] _circleDirections;

    void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _mesh = new Mesh { name = "TubeTrail" };
        _mesh.MarkDynamic();
        _meshFilter.mesh = _mesh;

        int pointCapacity = Mathf.Max(2, _maxPoints);
        int sideCount = Mathf.Max(3, _sides);
        int vertexCapacity = pointCapacity * sideCount;

        _points.Capacity = pointCapacity;
        _pointTimes.Capacity = pointCapacity;
        _vertices.Capacity = vertexCapacity;
        _uvs.Capacity = vertexCapacity;
        _colors.Capacity = vertexCapacity;
        _triangles.Capacity = (pointCapacity - 1) * sideCount * 6;

        _circleDirections = new Vector3[sideCount];
        for (int i = 0; i < sideCount; i++)
        {
            float angle = (float)i / sideCount * Mathf.PI * 2f;
            _circleDirections[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        }
    }

    void Update()
    {
        float currentTime = Time.time;

        // 오래된 포인트 제거
        while (_pointTimes.Count > 0 && currentTime - _pointTimes[0] > _trailLifetime)
        {
            _points.RemoveAt(0);
            _pointTimes.RemoveAt(0);
        }

        // 오프셋을 월드 좌표로 변환하여 포인트 샘플링
        // TransformPoint: 로컬 오프셋을 오브젝트 회전/스케일까지 반영한 월드 위치로 변환
        Vector3 samplePos = transform.TransformPoint(_trailOffset);

        // 새 포인트 추가
        if (
            _points.Count == 0
            || (samplePos - _points[_points.Count - 1]).sqrMagnitude > _minDistance * _minDistance
        )
        {
            _points.Add(samplePos);
            _pointTimes.Add(currentTime);

            if (_points.Count > _maxPoints)
            {
                _points.RemoveAt(0);
                _pointTimes.RemoveAt(0);
            }
        }

        GenerateTubeMesh();
    }

    void GenerateTubeMesh()
    {
        if (_points.Count < 2)
            return;

        _vertices.Clear();
        _triangles.Clear();
        _uvs.Clear();
        _colors.Clear();

        for (int i = 0; i < _points.Count; i++)
        {
            Vector3 forward;
            if (i < _points.Count - 1)
                forward = (_points[i + 1] - _points[i]).normalized;
            else
                forward = (_points[i] - _points[i - 1]).normalized;

            Quaternion rotation = Quaternion.LookRotation(forward);
            float t = (float)i / (_points.Count - 1);

            // 꼬리(t=0): 가늘고 투명 / 현재 위치(t=1): 두껍고 불투명
            float currentRadius = Mathf.Lerp(0f, _radius, t);
            float alpha = Mathf.Lerp(0f, 1f, t);

            for (int j = 0; j < _sides; j++)
            {
                Vector3 circlePoint = _circleDirections[j] * currentRadius;
                Vector3 worldPos = _points[i] + rotation * circlePoint;

                // 월드 좌표 → 로컬 좌표로 변환 후 버텍스 추가
                _vertices.Add(transform.InverseTransformPoint(worldPos));
                _uvs.Add(new Vector2((float)j / _sides, t));
                _colors.Add(
                    new Color(_trailColor.r, _trailColor.g, _trailColor.b, _trailColor.a * alpha)
                );
            }
        }

        for (int i = 0; i < _points.Count - 1; i++)
        {
            for (int j = 0; j < _sides; j++)
            {
                int curr = i * _sides + j;
                int next = i * _sides + (j + 1) % _sides;
                int currNext = (i + 1) * _sides + j;
                int nextNext = (i + 1) * _sides + (j + 1) % _sides;

                _triangles.Add(curr);
                _triangles.Add(currNext);
                _triangles.Add(next);

                _triangles.Add(next);
                _triangles.Add(currNext);
                _triangles.Add(nextNext);
            }
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_colors);
        _mesh.RecalculateNormals();
    }

    void OnDestroy()
    {
        if (_mesh != null)
            Destroy(_mesh);
    }

    private void OnDisable()
    {
        _points.Clear();
        _pointTimes.Clear();
        _vertices.Clear();
        _triangles.Clear();
        _uvs.Clear();
        _colors.Clear();

        if (_mesh != null)
            _mesh.Clear();
    }

#if UNITY_EDITOR
    // 씬 뷰에서 오프셋 위치를 구체로 시각화
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(_trailOffset), _radius);
    }
#endif
}
