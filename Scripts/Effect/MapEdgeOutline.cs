using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Hard edges of authored map meshes. Coplanar triangulation edges are omitted.</summary>
public sealed class MapEdgeOutline : MonoBehaviour
{
    [SerializeField] private MeshFilter[] _surfaces = System.Array.Empty<MeshFilter>();
    [SerializeField] private MeshFilter _outlinePrefab;
    private readonly Dictionary<Mesh, Mesh> _meshes = new();
    private readonly List<GameObject> _objects = new();

    private void Start()
    {
        if (_outlinePrefab == null) return;
        foreach (var surface in _surfaces)
        {
            if (surface == null || surface.sharedMesh == null) continue;
            if (!surface.TryGetComponent(out MeshRenderer sourceRenderer) || !sourceRenderer.enabled) continue;
            var source = surface.sharedMesh;
            if (!_meshes.TryGetValue(source, out Mesh edges))
            {
                if (!source.isReadable)
                {
                    Debug.LogWarning($"Outline mesh is not readable: {source.name}", surface);
                    continue;
                }
                edges = BuildEdges(source);
                _meshes.Add(source, edges);
            }
            var filter = Instantiate(_outlinePrefab, surface.transform);
            var child = filter.gameObject;
            child.layer = surface.gameObject.layer;
            filter.sharedMesh = edges;
            var renderer = child.GetComponent<MeshRenderer>();
            renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            _objects.Add(child);
        }
    }

    private sealed class Edge
    {
        public Vector3 A, B, Normal;
        public int Faces;
        public bool Hard;
    }

    public static Mesh BuildEdges(Mesh source)
    {
        var vertices = source.vertices;
        var triangles = source.triangles;
        var welded = new Dictionary<Vector3Int, int>();
        var ids = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i] * 10000f;
            var key = new Vector3Int(Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));
            if (!welded.TryGetValue(key, out int id)) { id = welded.Count; welded.Add(key, id); }
            ids[i] = id;
        }
        var edges = new Dictionary<(int, int), Edge>();
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
            if (normal.sqrMagnitude < 0.5f) continue;
            Add(a, b, normal); Add(b, c, normal); Add(c, a, normal);
        }
        void Add(int a, int b, Vector3 normal)
        {
            if (ids[a] == ids[b]) return;
            var key = ids[a] < ids[b] ? (ids[a], ids[b]) : (ids[b], ids[a]);
            if (!edges.TryGetValue(key, out Edge edge))
            {
                edge = new Edge { A = vertices[a], B = vertices[b], Normal = normal };
                edges.Add(key, edge);
            }
            else if (Vector3.Dot(edge.Normal, normal) < 0.9063f) edge.Hard = true; // 25 degrees
            edge.Faces++;
        }
        var positions = new List<Vector3>();
        var others = new List<Vector4>();
        var sides = new List<Vector2>();
        var indices = new List<int>();
        foreach (var edge in edges.Values)
        {
            if (edge.Faces > 1 && !edge.Hard) continue;
            int n = positions.Count;
            positions.Add(edge.A); positions.Add(edge.A); positions.Add(edge.B); positions.Add(edge.B);
            others.Add(edge.B); others.Add(edge.B); others.Add(edge.A); others.Add(edge.A);
            sides.Add(new(-1f, 0f)); sides.Add(new(1f, 0f)); sides.Add(new(1f, 0f)); sides.Add(new(-1f, 0f));
            indices.Add(n); indices.Add(n + 2); indices.Add(n + 1);
            indices.Add(n + 1); indices.Add(n + 2); indices.Add(n + 3);
        }
        var mesh = new Mesh { name = source.name + " Hard Edges", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(positions); mesh.SetTangents(others); mesh.SetUVs(0, sides);
        mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        foreach (var child in _objects) if (child != null) Destroy(child);
        foreach (var mesh in _meshes.Values) if (mesh != null) Destroy(mesh);

    }
}
