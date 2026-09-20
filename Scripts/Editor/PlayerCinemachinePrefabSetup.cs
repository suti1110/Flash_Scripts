using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PlayerCinemachinePrefabSetup
{
    private const string PlayerPrefabPath = "Assets/Prefab/Player/Player.prefab";

    [MenuItem("Tools/Flash/Camera/Configure Player Cinemachine")]
    public static void ConfigurePlayerPrefab()
    {
        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (
            prefabStage == null
            || !string.Equals(
                prefabStage.assetPath,
                PlayerPrefabPath,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            EditorUtility.DisplayDialog(
                "Player Cinemachine 설정",
                "Player.prefab을 Prefab Mode로 연 뒤 이 메뉴를 다시 실행해 주세요.",
                "확인"
            );
            return;
        }

        GameObject prefabRoot = prefabStage.prefabContentsRoot;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Player Cinemachine 구성");

        try
        {
            PlayerCamera playerCamera = prefabRoot.GetComponent<PlayerCamera>();
            PlayerDeath playerDeath = prefabRoot.GetComponent<PlayerDeath>();
            if (playerCamera == null || playerCamera.MainCamera == null || playerCamera.CameraPivot == null)
                throw new MissingReferenceException("Player 프리팹의 PlayerCamera 참조가 완전하지 않습니다.");

            CinemachineBrain brain = GetOrAddComponent<CinemachineBrain>(
                playerCamera.MainCamera.gameObject
            );
            Undo.RecordObject(brain, "Cinemachine Brain 설정");
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(
                CinemachineBlendDefinition.Styles.EaseInOut,
                0.1f
            );

            CinemachineCamera thirdPersonCamera = GetOrCreatePassiveCamera(
                playerCamera.CameraPivot,
                "Third Person Camera",
                playerCamera.MainCamera.transform.position,
                playerCamera.MainCamera.transform.rotation,
                20
            );
            CinemachineCamera firstPersonCamera = GetOrCreatePassiveCamera(
                playerCamera.CameraPivot,
                "First Person Camera",
                playerCamera.CameraPivot.position,
                playerCamera.CameraPivot.rotation,
                10
            );

            LensSettings lens = LensSettings.FromCamera(playerCamera.MainCamera);
            Undo.RecordObject(thirdPersonCamera, "3인칭 카메라 설정");
            thirdPersonCamera.Lens = lens;
            lens.NearClipPlane = 0.03f;
            Undo.RecordObject(firstPersonCamera, "1인칭 카메라 설정");
            firstPersonCamera.Lens = lens;

            PlayerLocalVisualFader visualFader = GetOrAddComponent<PlayerLocalVisualFader>(
                prefabRoot
            );
            AssignBodyRenderers(playerDeath, visualFader);
            AssignPlayerCameraReferences(
                playerCamera,
                brain,
                thirdPersonCamera,
                firstPersonCamera,
                visualFader
            );

            EditorUtility.SetDirty(prefabRoot);
            Debug.Log(
                "Player 프리팹의 Cinemachine 카메라와 로컬 PlayerVisual 페이드를 구성했습니다. "
                    + "변경을 확인한 뒤 직접 저장하거나 Undo로 되돌리세요."
            );
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    private static T GetOrAddComponent<T>(GameObject target)
        where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static CinemachineCamera GetOrCreatePassiveCamera(
        Transform parent,
        string cameraName,
        Vector3 position,
        Quaternion rotation,
        int priority
    )
    {
        Transform child = parent.Find(cameraName);
        if (child == null)
        {
            child = new GameObject(cameraName).transform;
            Undo.RegisterCreatedObjectUndo(child.gameObject, $"{cameraName} 생성");
            Undo.SetTransformParent(child, parent, $"{cameraName} 부모 설정");
        }

        Undo.RecordObject(child, $"{cameraName} Transform 설정");
        child.SetPositionAndRotation(position, rotation);
        CinemachineCamera camera = GetOrAddComponent<CinemachineCamera>(child.gameObject);
        Undo.RecordObject(camera, $"{cameraName} 우선순위 설정");
        camera.Priority = priority;
        return camera;
    }

    private static void AssignBodyRenderers(
        PlayerDeath playerDeath,
        PlayerLocalVisualFader visualFader
    )
    {
        if (playerDeath == null)
            throw new MissingReferenceException("Player 프리팹에 PlayerDeath가 없습니다.");

        SerializedObject deathObject = new(playerDeath);
        SerializedProperty deathRenderers = deathObject.FindProperty("_renderers");
        List<Renderer> bodyRenderers = new();
        for (int i = 0; i < deathRenderers.arraySize; i++)
        {
            Renderer renderer = deathRenderers.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
            if (renderer is MeshRenderer or SkinnedMeshRenderer)
                bodyRenderers.Add(renderer);
        }

        Undo.RecordObject(visualFader, "PlayerVisual 페이드 렌더러 설정");
        SerializedObject faderObject = new(visualFader);
        SerializedProperty faderRenderers = faderObject.FindProperty("_playerVisualRenderers");
        faderRenderers.arraySize = bodyRenderers.Count;
        for (int i = 0; i < bodyRenderers.Count; i++)
            faderRenderers.GetArrayElementAtIndex(i).objectReferenceValue = bodyRenderers[i];
        faderObject.ApplyModifiedProperties();
    }

    private static void AssignPlayerCameraReferences(
        PlayerCamera playerCamera,
        CinemachineBrain brain,
        CinemachineCamera thirdPersonCamera,
        CinemachineCamera firstPersonCamera,
        PlayerLocalVisualFader visualFader
    )
    {
        Undo.RecordObject(playerCamera, "Player 카메라 참조 설정");
        SerializedObject cameraObject = new(playerCamera);
        cameraObject.FindProperty("_cinemachineBrain").objectReferenceValue = brain;
        cameraObject.FindProperty("_thirdPersonCamera").objectReferenceValue = thirdPersonCamera;
        cameraObject.FindProperty("_firstPersonCamera").objectReferenceValue = firstPersonCamera;
        cameraObject.FindProperty("_localVisualFader").objectReferenceValue = visualFader;
        cameraObject.ApplyModifiedProperties();
    }
}
