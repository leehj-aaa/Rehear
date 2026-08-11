using System;
using System.Linq;
using CurvedUI;
using CurvedUI.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[InitializeOnLoad]
internal static class RehearCurvedUISetup
{
    private const string ScenePath = "Assets/01_Scene/Scene_00.unity";
    private const string CanvasName = "Canvas";
    private const int CurveAngle = 35;

    static RehearCurvedUISetup()
    {
        EditorApplication.delayCall += ConfigureOpenScene;
    }

    [MenuItem("Rehear/Apply Curved UI To Scene 00")]
    private static void ConfigureOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureRequiredDefines();

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            return;

        var canvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.name == CanvasName);
        if (canvas == null)
        {
            Debug.LogError("Rehear CurvedUI setup: Scene_00의 Canvas를 찾지 못했습니다.");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Apply Rehear Curved UI");
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.gameObject.layer = LayerMask.NameToLayer("UI");

        var settings = GetOrAdd<CurvedUISettings>(canvas.gameObject);
        settings.Shape = CurvedUISettings.CurvedUIShape.CYLINDER;
        settings.Angle = CurveAngle;
        settings.PreserveAspect = true;
        settings.Quality = 1.5f;
        settings.Interactable = true;
        settings.BlocksRaycasts = true;
        settings.AddEffectToChildren();

        GetOrAdd<CurvedUIRaycaster>(canvas.gameObject);
        ArrangeOpeningUi(canvas.transform);
        ConfigureEventSystem();

        EditorUtility.SetDirty(canvas.gameObject);
        EditorUtility.SetDirty(settings);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Rehear: Scene_00 UI에 CurvedUI(원통형 35°, Unity XR 입력)를 적용했습니다.", canvas);
    }

    private static void ConfigureEventSystem()
    {
        var eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        GameObject eventSystemObject;
        if (eventSystem == null)
        {
            eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<CurvedUIEventSystem>();
        }
        else
        {
            // Cache this before changing m_Script: the old component handle becomes invalid.
            eventSystemObject = eventSystem.gameObject;
            if (eventSystem is not CurvedUIEventSystem)
            {
                var temporary = new GameObject("CurvedUI EventSystem Script Source");
                var replacement = temporary.AddComponent<CurvedUIEventSystem>();
                var curvedEventSystemScript = MonoScript.FromMonoBehaviour(replacement);
                UnityEngine.Object.DestroyImmediate(temporary);

                var serializedEventSystem = new SerializedObject(eventSystem);
                serializedEventSystem.FindProperty("m_Script").objectReferenceValue = curvedEventSystemScript;
                serializedEventSystem.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var inputModule = GetOrAdd<CurvedUIInputModule>(eventSystemObject);
        var serializedModule = new SerializedObject(inputModule);
        serializedModule.FindProperty("controlMethod").intValue = (int)ControlMethod.UNITY_XR;
        serializedModule.FindProperty("usedHand").enumValueIndex = (int)Hand.Right;
        serializedModule.FindProperty("raycastLayerMask").intValue = 1 << LayerMask.NameToLayer("UI");

        var unityXr = serializedModule.FindProperty("unityXRControlMethod");
        AssignController(unityXr.FindPropertyRelative("rightController"), "Right");
        AssignController(unityXr.FindPropertyRelative("leftController"), "Left");
        AssignAction(unityXr.FindPropertyRelative("rightClickActionReference"), "XRI Right Interaction", "UI Press");
        AssignAction(unityXr.FindPropertyRelative("leftClickActionReference"), "XRI Left Interaction", "UI Press");
        serializedModule.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(eventSystemObject);
    }

    private static void ArrangeOpeningUi(Transform canvasTransform)
    {
        var logo = canvasTransform.Find("Rehear Logo Intro");
        var button = canvasTransform.Find("Btn_Scene00_to_Scene01") as RectTransform;
        if (logo == null || button == null)
            return;

        Undo.RecordObject(logo, "Arrange Rehear opening UI");
        logo.localPosition = new Vector3(-120f, 1020f, 0f);
        logo.localRotation = Quaternion.identity;
        logo.localScale = Vector3.one;
        EditorUtility.SetDirty(logo);
    }

    private static void AssignController(SerializedProperty property, string hand)
    {
        if (property == null || property.objectReferenceValue != null)
            return;

        var controllerType = Type.GetType(
            "UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ControllerInputActionManager, " +
            "Unity.XR.Interaction.Toolkit.Samples.StarterAssets");
        if (controllerType == null)
            return;

        var controller = Resources.FindObjectsOfTypeAll(controllerType)
            .OfType<Component>()
            .FirstOrDefault(item => item.gameObject.scene.IsValid() &&
                                    item.name.Contains(hand, StringComparison.OrdinalIgnoreCase));
        if (controller != null)
            property.objectReferenceValue = controller;
    }

    private static void AssignAction(SerializedProperty property, string mapName, string actionName)
    {
        if (property == null || property.objectReferenceValue != null)
            return;

        var actionGuid = AssetDatabase.FindAssets("t:InputActionAsset")
            .FirstOrDefault(guid => AssetDatabase.GUIDToAssetPath(guid).EndsWith("XRI Default Input Actions.inputactions"));
        if (string.IsNullOrEmpty(actionGuid))
            return;

        var path = AssetDatabase.GUIDToAssetPath(actionGuid);
        var actionReference = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<InputActionReference>()
            .FirstOrDefault(reference => reference.action != null &&
                                         reference.action.actionMap.name == mapName &&
                                         reference.action.name == actionName);
        if (actionReference != null)
            property.objectReferenceValue = actionReference;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        return target.TryGetComponent<T>(out var component) ? component : Undo.AddComponent<T>(target);
    }

    private static void EnsureRequiredDefines()
    {
        var namedTarget = NamedBuildTarget.FromBuildTargetGroup(
            BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
        var defines = PlayerSettings.GetScriptingDefineSymbols(namedTarget)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        var changed = false;
        foreach (var define in new[] { "CURVEDUI_NEW_INPUT", "CURVEDUI_UNITY_XR" })
        {
            if (defines.Contains(define))
                continue;
            defines.Add(define);
            changed = true;
        }

        if (changed)
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, string.Join(";", defines));
    }
}
