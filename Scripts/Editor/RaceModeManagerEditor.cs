using UnityEditor;
using UnityEngine;


/// <summary>레이스 표시용 경로를 씬에서 확인하고 수정하는 편집 도구.</summary>
[CustomEditor(typeof(RaceModeManager))]
public sealed class RaceModeManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox(
            "경로 지점을 시작부터 결승 순서로 등록하세요. Scene 뷰에서 지점을 드래그할 수 있습니다. "
            + "우회한 선수도 가장 가까운 구간으로 진행도가 계산됩니다. "
            + "교차하거나 나란히 붙은 경로는 위치만으로 구분하기 어려우므로 실제 이동 동선을 따라 지점을 배치하세요. "
            + "체크포인트와 승패 판정에는 영향을 주지 않습니다.", UnityEditor.MessageType.Info);

        var race = (RaceModeManager)target;
        if (GUILayout.Button("경유 지점 추가 (결승 바로 앞)"))
            AddRoutePoint(race);

        var points = race.RoutePoints;
        for (int i = 0; i < points.Length; i++)
        {
            if (points[i] == null)
            {
                EditorGUILayout.HelpBox($"{i + 1}번 경로 참조가 비어 있습니다.", UnityEditor.MessageType.Error);
                continue;
            }
            if (i > 0 && RouteDistance(race, points[i - 1], points[i]) < 0.001f)
                EditorGUILayout.HelpBox($"{i}번과 {i + 1}번 지점이 겹칩니다. 이 구간은 계산에서 제외됩니다.", UnityEditor.MessageType.Warning);
            if (GUILayout.Button($"{i + 1:00} · {points[i].name} 위치 보기"))
            {
                Selection.activeGameObject = points[i].gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }
    }

    private void OnSceneGUI()
    {
        var race = (RaceModeManager)target;
        foreach (var point in race.RoutePoints)
        {
            if (point == null)
                continue;
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(point.position, Quaternion.identity);
            if (!EditorGUI.EndChangeCheck())
                continue;
            Undo.RecordObject(point, "레이스 경로 지점 이동");
            point.position = position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(point);
        }
    }

    // 선택하지 않아도 지점 이름과 연결선을 보여 좌표 배열을 읽지 않고 경로를 확인할 수 있게 한다.
    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
    private static void DrawRoute(RaceModeManager race, GizmoType gizmoType)
    {
        var points = race.RoutePoints;
        float total = 0f;
        for (int i = 1; i < points.Length; i++)
            total += RouteDistance(race, points[i - 1], points[i]);

        float traveled = 0f;
        using (new Handles.DrawingScope(new Color(0.2f, 0.9f, 1f)))
        {
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null)
                    continue;
                if (i > 0 && points[i - 1] != null)
                {
                    Handles.DrawLine(points[i - 1].position, points[i].position, 3f);
                    traveled += RouteDistance(race, points[i - 1], points[i]);
                }
                float size = HandleUtility.GetHandleSize(points[i].position) * 0.08f;
                Handles.SphereHandleCap(0, points[i].position, Quaternion.identity, size, EventType.Repaint);
                string role = i == 0 ? "시작" : i == points.Length - 1 ? "결승" : "경유";
                Handles.Label(points[i].position + Vector3.up * size,
                    $"{i + 1:00} {role} · {points[i].name} · {(total > 0f ? traveled / total : 0f):P0}");
            }
        }
    }

    private static float RouteDistance(RaceModeManager race, Transform a, Transform b)
    {
        if (a == null || b == null)
            return 0f;
        Vector3 delta = b.position - a.position;
        if (race.HorizontalProgress)
            delta.y = 0f;
        return delta.magnitude;
    }

    private void AddRoutePoint(RaceModeManager race)
    {
        // 완주 지점의 순서는 유지하고 그 직전 구간의 중간에 새 경유 지점을 삽입한다.
        var points = race.RoutePoints;
        if (points.Length < 2)
        {
            EditorUtility.DisplayDialog("레이스 경로", "시작과 결승 오브젝트를 먼저 배열에 지정하세요.", "확인");
            return;
        }
        Transform finish = points[points.Length - 1];
        Transform previous = points[points.Length - 2];
        if (finish == null || previous == null)
            return;

        Undo.SetCurrentGroupName("레이스 경유 지점 추가");
        int group = Undo.GetCurrentGroup();
        var point = new GameObject($"경유 지점 {points.Length - 1:00}");
        Undo.RegisterCreatedObjectUndo(point, "레이스 경유 지점 추가");
        point.transform.SetParent(finish.parent, false);
        point.transform.position = (previous.position + finish.position) * 0.5f;
        serializedObject.Update();
        var array = serializedObject.FindProperty("_routePoints");
        array.InsertArrayElementAtIndex(points.Length - 1);
        array.GetArrayElementAtIndex(points.Length - 1).objectReferenceValue = point.transform;
        serializedObject.ApplyModifiedProperties();
        Undo.CollapseUndoOperations(group);
        SceneView.RepaintAll();
    }
}

