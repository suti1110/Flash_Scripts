using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TmpFontReplacerWindow : EditorWindow
{
    private const string UndoName = "열린 씬 TMP 폰트 일괄 교체";

    [SerializeField]
    private TMP_FontAsset _targetFont;

    private int _loadedSceneCount;
    private int _tmpTextCount;
    private int _replacementCount;

    [MenuItem("Tools/Flash/TMP 폰트 일괄 교체")]
    private static void Open()
    {
        TmpFontReplacerWindow window = GetWindow<TmpFontReplacerWindow>();
        window.titleContent = new GUIContent("TMP 폰트 교체");
        window.minSize = new Vector2(420f, 210f);
        window.RefreshCounts();
        window.Show();
    }

    private void OnEnable()
    {
        Undo.undoRedoPerformed += HandleUndoRedo;
        RefreshCounts();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= HandleUndoRedo;
    }

    private void OnFocus()
    {
        RefreshCounts();
    }

    private void OnHierarchyChange()
    {
        RefreshCounts();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("열린 씬 TMP 폰트 일괄 교체", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "현재 로드된 모든 씬에서 비활성 오브젝트를 포함한 TMP_Text를 찾아 폰트와 기본 머티리얼을 교체합니다. 프리팹 에셋 자체는 변경하지 않습니다.",
            UnityEditor.MessageType.Info
        );

        EditorGUI.BeginChangeCheck();
        _targetFont = (TMP_FontAsset)
            EditorGUILayout.ObjectField(
                "적용할 TMP Font Asset",
                _targetFont,
                typeof(TMP_FontAsset),
                false
            );
        if (EditorGUI.EndChangeCheck())
            RefreshCounts();

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("로드된 씬", _loadedSceneCount.ToString());
        EditorGUILayout.LabelField("발견한 TMP", _tmpTextCount.ToString());
        EditorGUILayout.LabelField("교체 대상", _replacementCount.ToString());

        EditorGUILayout.Space(8f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("다시 검색"))
                RefreshCounts();

            using (
                new EditorGUI.DisabledScope(
                    _targetFont == null
                        || _replacementCount == 0
                        || EditorApplication.isPlayingOrWillChangePlaymode
                )
            )
            {
                if (GUILayout.Button("폰트 교체"))
                    ReplaceFonts();
            }
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox(
                "Play Mode에서는 씬 폰트를 변경할 수 없습니다.",
                UnityEditor.MessageType.Warning
            );
    }

    private void ReplaceFonts()
    {
        if (_targetFont == null || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        List<TMP_Text> targets = FindTmpTextsInLoadedScenes();
        targets.RemoveAll(text => text == null || text.font == _targetFont);
        if (targets.Count == 0)
        {
            RefreshCounts();
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "TMP 폰트 교체",
            $"열린 씬의 TMP {targets.Count}개를 '{_targetFont.name}' 폰트로 교체하시겠습니까?\n\n변경 내용은 Undo로 되돌릴 수 있으며 씬은 자동 저장되지 않습니다.",
            "교체",
            "취소"
        );
        if (!confirmed)
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        HashSet<Scene> changedScenes = new();

        for (int i = 0; i < targets.Count; i++)
        {
            TMP_Text text = targets[i];
            Undo.RecordObject(text, UndoName);
            text.font = _targetFont;
            text.fontSharedMaterial = _targetFont.material;
            EditorUtility.SetDirty(text);

            if (PrefabUtility.IsPartOfPrefabInstance(text))
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);

            changedScenes.Add(text.gameObject.scene);
        }

        foreach (Scene scene in changedScenes)
        {
            if (scene.IsValid() && scene.isLoaded)
                EditorSceneManager.MarkSceneDirty(scene);
        }

        Undo.CollapseUndoOperations(undoGroup);
        SceneView.RepaintAll();
        RefreshCounts();

        Debug.Log(
            $"[TmpFontReplacer] 열린 씬 {changedScenes.Count}개에서 TMP {targets.Count}개의 폰트를 '{_targetFont.name}'으로 교체했습니다."
        );
    }

    private void RefreshCounts()
    {
        List<TMP_Text> texts = FindTmpTextsInLoadedScenes();
        _loadedSceneCount = CountLoadedScenes();
        _tmpTextCount = texts.Count;
        _replacementCount = 0;

        if (_targetFont != null)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                if (texts[i] != null && texts[i].font != _targetFont)
                    _replacementCount++;
            }
        }

        Repaint();
    }

    private static List<TMP_Text> FindTmpTextsInLoadedScenes()
    {
        List<TMP_Text> results = new();
        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            GameObject[] roots = scene.GetRootGameObjects();
            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                results.AddRange(roots[rootIndex].GetComponentsInChildren<TMP_Text>(true));
        }

        return results;
    }

    private static int CountLoadedScenes()
    {
        int count = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.IsValid() && scene.isLoaded)
                count++;
        }

        return count;
    }

    private void HandleUndoRedo()
    {
        RefreshCounts();
    }
}
