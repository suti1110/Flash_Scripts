using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MapCatalogEditorUtility
{
    private static bool _refreshQueued;

    [MenuItem("Tools/Flash/맵 카탈로그 새로고침")]
    public static void RefreshAllCatalogs()
    {
        _refreshQueued = false;
        List<SO_MapDefinition> maps = FindAssets<SO_MapDefinition>();
        maps.Sort(CompareMaps);

        bool changed = false;
        for (int i = 0; i < maps.Count; i++)
        {
            if (!maps[i].RefreshSceneMetadata())
                continue;

            EditorUtility.SetDirty(maps[i]);
            changed = true;
        }

        List<SO_MapCatalog> catalogs = FindAssets<SO_MapCatalog>();
        for (int i = 0; i < catalogs.Count; i++)
        {
            if (!catalogs[i].EditorSetMaps(maps))
                continue;

            EditorUtility.SetDirty(catalogs[i]);
            changed = true;
        }

        if (changed)
            AssetDatabase.SaveAssets();
    }

    public static void QueueRefresh()
    {
        if (_refreshQueued)
            return;

        _refreshQueued = true;
        EditorApplication.delayCall += RefreshAllCatalogs;
    }

    public static bool IsSceneInBuild(SO_MapDefinition map)
    {
        if (map == null || string.IsNullOrWhiteSpace(map.ScenePath))
            return false;

        EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (scenes[i].enabled && scenes[i].path == map.ScenePath)
                return true;
        }

        return false;
    }

    private static List<T> FindAssets<T>()
        where T : UnityEngine.Object
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        List<T> assets = new(guids.Length);

        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                assets.Add(asset);
        }

        return assets;
    }

    private static int CompareMaps(SO_MapDefinition left, SO_MapDefinition right)
    {
        int result = CompareModes(left.Mode, right.Mode);
        if (result != 0)
            return result;

        result = left.SortOrder.CompareTo(right.SortOrder);
        return result != 0
            ? result
            : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
    }

    private static int CompareModes(SO_GameModeDefinition left, SO_GameModeDefinition right)
    {
        if (left == right)
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        int result = left.SortOrder.CompareTo(right.SortOrder);
        return result != 0
            ? result
            : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
    }
}

[CustomEditor(typeof(SO_MapCatalog))]
public sealed class MapCatalogInspector : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty maps = serializedObject.FindProperty("_maps");

        EditorGUILayout.LabelField("등록된 맵 목록", EditorStyles.boldLabel);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.IntField("맵 개수", maps.arraySize);
        for (int i = 0; i < maps.arraySize; i++)
        {
            EditorGUILayout.PropertyField(
                maps.GetArrayElementAtIndex(i),
                new GUIContent($"맵 {i + 1}")
            );
        }
        EditorGUI.EndDisabledGroup();
        serializedObject.ApplyModifiedProperties();

        if (GUILayout.Button("맵 카탈로그 새로고침"))
            MapCatalogEditorUtility.RefreshAllCatalogs();
    }
}

[CustomEditor(typeof(SO_MapDefinition))]
public sealed class MapDefinitionInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        if (EditorGUI.EndChangeCheck())
            MapCatalogEditorUtility.QueueRefresh();

        SO_MapDefinition map = (SO_MapDefinition)target;
        if (map.Mode == null)
            EditorGUILayout.HelpBox(
                "게임 모드가 지정되지 않았습니다.",
                UnityEditor.MessageType.Warning
            );
        if (!map.HasScene)
            EditorGUILayout.HelpBox(
                "게임 씬이 지정되지 않았습니다.",
                UnityEditor.MessageType.Warning
            );
        else if (!MapCatalogEditorUtility.IsSceneInBuild(map))
            EditorGUILayout.HelpBox(
                "이 씬이 빌드 프로파일에서 활성화되지 않았습니다.",
                UnityEditor.MessageType.Warning
            );
    }
}

public sealed class MapCatalogAssetPostprocessor : AssetPostprocessor
{
    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths
    )
    {
        if (
            ContainsCatalogInput(importedAssets)
            || ContainsCatalogInput(deletedAssets)
            || ContainsCatalogInput(movedAssets)
            || ContainsCatalogInput(movedFromAssetPaths)
        )
        {
            MapCatalogEditorUtility.QueueRefresh();
        }
    }

    private static bool ContainsCatalogInput(string[] paths)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            string extension = Path.GetExtension(paths[i]);
            if (
                string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
        }

        return false;
    }
}
// MapCatalogEditor 편집기 도구는 인스펙터, 에셋 또는 빌드 설정을 안전하게 편집하는 작업을 담당한다.
// 런타임 코드와 분리하여 잘못된 설정을 가능한 한 에디터 단계에서 발견하도록 한다.
