using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

public static class MapPropPrefabBuilder
{
    // 아트 리소스가 준비되기 전에도 기획 수치와 네트워크 동작을 검증할 수 있도록
    // 필수 Collider, Rigidbody, NetworkBehaviour 조합을 갖춘 플레이스홀더를 생성한다.
    private const string PrefabFolder = "Assets/Prefab/MapProps";

    [MenuItem("Tools/Flash/맵 소품/플레이스홀더 프리팹 생성")]
    public static void CreatePlaceholderPrefabs()
    {
        EnsureFolder("Assets/Prefab", "MapProps");
        CreatePendulumMace();
        CreateEscapePit();
        CreateSniperDrone();
        CreateGrabbableObject();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"맵 소품 플레이스홀더 4종을 생성했습니다: {PrefabFolder}");
    }

    private static void CreatePendulumMace()
    {
        GameObject root = new("PendulumMace");
        GameObject swingRoot = new("SwingRoot");
        swingRoot.transform.SetParent(root.transform, false);

        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Arm";
        arm.transform.SetParent(swingRoot.transform, false);
        arm.transform.localPosition = new Vector3(0f, -2f, 0f);
        arm.transform.localScale = new Vector3(0.2f, 4f, 0.2f);
        Object.DestroyImmediate(arm.GetComponent<Collider>());

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(swingRoot.transform, false);
        head.transform.localPosition = new Vector3(0f, -4f, 0f);
        head.transform.localScale = Vector3.one * 1.25f;
        Rigidbody headBody = head.AddComponent<Rigidbody>();
        headBody.isKinematic = true;
        headBody.useGravity = false;
        head.AddComponent<PendulumMaceHitbox>();

        PendulumMace mace = root.AddComponent<PendulumMace>();
        SerializedObject serializedMace = new(mace);
        serializedMace.FindProperty("_swingRoot").objectReferenceValue = swingRoot.transform;
        serializedMace.ApplyModifiedPropertiesWithoutUndo();
        SaveAndDestroy(root, "PendulumMace.prefab");
    }

    private static void CreateEscapePit()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "EscapePit";
        root.transform.localScale = new Vector3(4f, 1f, 4f);
        BoxCollider trigger = root.GetComponent<BoxCollider>();
        trigger.isTrigger = true;

        Transform trapPoint = CreatePoint(root.transform, "TrapPoint", Vector3.zero);
        Transform escapePoint = CreatePoint(root.transform, "EscapePoint", new Vector3(0f, 1.5f, 0.625f));
        EscapePit pit = root.AddComponent<EscapePit>();
        SerializedObject serializedPit = new(pit);
        serializedPit.FindProperty("_trapPoint").objectReferenceValue = trapPoint;
        serializedPit.FindProperty("_escapePoint").objectReferenceValue = escapePoint;
        serializedPit.ApplyModifiedPropertiesWithoutUndo();
        SaveAndDestroy(root, "EscapePit.prefab");
    }

    private static void CreateSniperDrone()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        root.name = "SniperDrone";
        root.transform.localScale = new Vector3(0.8f, 0.4f, 0.8f);
        Object.DestroyImmediate(root.GetComponent<Collider>());

        Transform muzzle = CreatePoint(root.transform, "Muzzle", new Vector3(0f, 0f, 1f));
        root.AddComponent<NetworkObject>();
        LineRenderer line = root.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = 0.025f;
        line.endWidth = 0.01f;
        line.startColor = Color.red;
        line.endColor = new Color(1f, 0f, 0f, 0.1f);
        line.enabled = false;

        SniperDrone drone = root.AddComponent<SniperDrone>();
        SerializedObject serializedDrone = new(drone);
        serializedDrone.FindProperty("_muzzle").objectReferenceValue = muzzle;
        serializedDrone.FindProperty("_aimLine").objectReferenceValue = line;
        serializedDrone.ApplyModifiedPropertiesWithoutUndo();
        SaveAndDestroy(root, "SniperDrone.prefab");
    }

    private static void CreateGrabbableObject()
    {
        GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        root.name = "GrabbableObject";
        root.AddComponent<NetworkObject>();
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = 2f;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        root.AddComponent<NetworkTransform>();
        root.AddComponent<NetworkRigidbody>();
        root.AddComponent<GrabbableObject>();
        SaveAndDestroy(root, "GrabbableObject.prefab");
    }

    private static Transform CreatePoint(Transform parent, string name, Vector3 localPosition)
    {
        GameObject point = new(name);
        point.transform.SetParent(parent, false);
        point.transform.localPosition = localPosition;
        return point.transform;
    }

    private static void SaveAndDestroy(GameObject root, string fileName)
    {
        PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{fileName}");
        Object.DestroyImmediate(root);
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = $"{parent}/{child}";
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }
}
// MapPropPrefabBuilder 편집기 도구는 인스펙터, 에셋 또는 빌드 설정을 안전하게 편집하는 작업을 담당한다.
// 런타임 코드와 분리하여 잘못된 설정을 가능한 한 에디터 단계에서 발견하도록 한다.
