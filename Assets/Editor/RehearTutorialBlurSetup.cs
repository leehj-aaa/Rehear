using System;
using System.IO;
using System.Linq;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using LeTai.Asset.TranslucentImage.UniversalRP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearTutorialBlurSetup
{
    private const string ScenePath = "Assets/01_Scene/Scene_00_5_Tutorial.unity";
    private const string RequestPath = "Temp/RehearTutorialBlur.request";
    private const string ReportPath = "Temp/RehearTutorialBlur-validation.txt";
    private const string ConfigPath = "Assets/Settings/Tutorial Quest3 Blur.asset";
    private static double readyAfter;

    static RehearTutorialBlurSetup()
    {
        // This explicit, temporary request is consumed once; ordinary domain
        // reloads must not overwrite later user tuning or start another bake.
        if (!File.Exists(RequestPath)) return;
        readyAfter = EditorApplication.timeSinceStartup + 3;
        EditorApplication.update += ApplyPending;
    }

    private static void ApplyPending()
    {
        if (EditorApplication.timeSinceStartup < readyAfter || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        EditorApplication.update -= ApplyPending;
        if (!File.Exists(RequestPath)) return;
        File.Delete(RequestPath);
        try { ApplyAndBake(); }
        catch (Exception e) { File.WriteAllText(ReportPath, "FAILED\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Apply Tutorial Blur And Bake Brighter Lights")]
    public static void ApplyAndBake()
    {
        ApplyBlur();
        var scene = EditorSceneManager.GetActiveScene();
        var lights = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>(true)).ToArray();
        var fills = lights.Where(l => l.name.StartsWith("Ceiling Fill ")).ToArray();
        var pendants = lights.Where(l => l.name.StartsWith("Pendant Area ")).ToArray();
        if (fills.Length != 6 || pendants.Length != 4) throw new InvalidOperationException("Expected 6 ceiling fills and 4 pendant lights; bake stopped.");
        foreach (var light in fills.Concat(pendants))
        {
            Undo.RecordObject(light, "Brighten tutorial ceiling lighting");
            light.intensity = light.name.StartsWith("Ceiling Fill ") ? 1.5f : 2.5f;
            EditorUtility.SetDirty(light);
            PrefabUtility.RecordPrefabInstancePropertyModifications(light);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed; bake stopped.");
        Lightmapping.bakeCompleted += OnBakeCompleted;
        if (!Lightmapping.BakeAsync())
        {
            Lightmapping.bakeCompleted -= OnBakeCompleted;
            throw new InvalidOperationException("Blur and lights saved, but Unity did not start the bake.");
        }
        File.AppendAllText(ReportPath, "\nceilingFill=1.5 x6\npendantArea=2.5 x4\nbakeStarted=true\n");
    }

    private static void OnBakeCompleted()
    {
        Lightmapping.bakeCompleted -= OnBakeCompleted;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path == ScenePath) EditorSceneManager.SaveScene(scene);
        File.AppendAllText(ReportPath, $"bakeCompleted={DateTime.UtcNow:O}\nlightmaps={LightmapSettings.lightmaps.Length}\n");
        Debug.Log("Rehear: Tutorial translucent UI and brighter ceiling lighting baked and saved. Quest device check still required.");
    }

    [MenuItem("Rehear/Apply Tutorial Translucent UI")]
    public static void ApplyBlur()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Open the tutorial scene in Edit mode with no bake running.");
        var roots = scene.GetRootGameObjects();
        var canvas = roots.SelectMany(g => g.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "TutorialUI");
        var camera = roots.SelectMany(g => g.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
        var panels = canvas.GetComponentsInChildren<TranslucentImage>(true);
        var baseData = camera.GetUniversalAdditionalCameraData();
        var uiLayer = LayerMask.NameToLayer("UI");
        if (panels.Length == 0 || uiLayer < 0 || baseData.renderType != CameraRenderType.Base)
            throw new InvalidOperationException("Build native Figma tutorial panels before applying blur.");

        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Project Configuration/Android Preset.asset");
        if (!renderer) throw new InvalidOperationException("Tutorial URP renderer missing.");
        var feature = renderer.rendererFeatures.OfType<TranslucentImageBlurSource>().FirstOrDefault();
        if (!feature)
        {
            feature = ScriptableObject.CreateInstance<TranslucentImageBlurSource>();
            feature.name = "TranslucentImageBlurSource";
            AssetDatabase.AddObjectToAsset(feature, renderer);
            renderer.rendererFeatures.Add(feature);
        }
        feature.SetActive(true);
        feature.renderOrder = TranslucentImageBlurSource.RenderOrder.AfterPostProcessing;
        feature.Create();
        renderer.SetDirty();
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(renderer);

        var config = AssetDatabase.LoadAssetAtPath<ScalableBlurConfig>(ConfigPath);
        if (!config)
        {
            config = ScriptableObject.CreateInstance<ScalableBlurConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        config.Mode = ScalableBlurConfig.BlurMode.Performance;
        config.Strength = 14;
        config.UseStrength = true;
        config.ReferenceResolution = new Vector2(1920, 1080);
        EditorUtility.SetDirty(config);

        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Apply tutorial background blur");
        Undo.RecordObjects(new Object[] { camera, baseData }, "Separate world and tutorial UI rendering");
        var source = camera.GetComponent<TranslucentImageSource>();
        if (!source) source = Undo.AddComponent<TranslucentImageSource>(camera.gameObject);
        source.BlurConfig = config;
        source.Downsample = 2;
        source.MaxUpdateRate = float.PositiveInfinity;
        source.BlurRegion = new Rect(0, 0, 1, 1);
        source.Preview = false;
        // CurvedUI bends beyond the package's flat RectTransform culling bounds.
        source.SkipCulling = true;

        foreach (var panel in panels)
        {
            panel.source = source;
            panel.textureAlphaMode = TranslucentImage.TextureAlphaMode.Alpha;
            panel.vibrancy = 1;
            panel.brightness = 0;
            panel.flatten = 0;
            var view = panel.GetComponentInParent<TutorialFigmaView>();
            bool isIntro = view && view.steps.Length > 1 && panel.transform.IsChildOf(view.steps[1].transform);
            panel.foregroundOpacity = isIntro ? RehearBlurDiagnostics.IntroWhiteTint : panel.name == "Controller guide glass" ? RehearBlurDiagnostics.GuideWhiteTint : 0.16f;
            if (panel.material.HasProperty("_GlassTint"))
                panel.material.SetFloat("_GlassTint", panel.foregroundOpacity);
            panel.raycastTarget = false;
            panel.SetAllDirty();
            EditorUtility.SetDirty(panel);
        }
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
                                            AdditionalCanvasShaderChannels.TexCoord2 |
                                            AdditionalCanvasShaderChannels.TexCoord3;
        canvas.worldCamera = camera;
        foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = uiLayer;
        canvas.GetComponent<CurvedUISettings>().AddEffectToChildren();

        var uiTransform = camera.transform.Find("Tutorial UI Overlay Camera");
        if (!uiTransform)
        {
            var go = new GameObject("Tutorial UI Overlay Camera");
            Undo.RegisterCreatedObjectUndo(go, "Create UI-only overlay camera");
            go.transform.SetParent(camera.transform, false);
            uiTransform = go.transform;
        }
        // Unity native components can return an editor "fake null"; ?? does not detect it.
        var uiCamera = uiTransform.GetComponent<Camera>();
        if (!uiCamera) uiCamera = Undo.AddComponent<Camera>(uiTransform.gameObject);
        uiCamera.CopyFrom(camera);
        uiCamera.tag = "Untagged";
        uiCamera.cullingMask = 1 << uiLayer;
        uiCamera.clearFlags = CameraClearFlags.Nothing;
        uiCamera.depth = camera.depth + 1;
        uiCamera.targetTexture = null;
        uiCamera.enabled = true;
        uiTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        var uiData = uiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Overlay;
        var serializedUiData = new SerializedObject(uiData);
        serializedUiData.FindProperty("m_ClearDepth").boolValue = false;
        serializedUiData.ApplyModifiedProperties();
        uiData.renderPostProcessing = false;
        uiData.renderShadows = false;
        uiData.allowXRRendering = true;
        uiData.requiresColorOption = CameraOverrideOption.Off;
        uiData.requiresDepthOption = CameraOverrideOption.Off;
        uiData.SetRenderer(0);
        camera.cullingMask &= ~(1 << uiLayer);
        if (!baseData.cameraStack.Contains(uiCamera)) baseData.cameraStack.Add(uiCamera);

        var sync = camera.GetComponent<TutorialBlurCameraSync>();
        if (!sync) sync = Undo.AddComponent<TutorialBlurCameraSync>(camera.gameObject);
        sync.sceneCamera = camera;
        sync.uiCamera = uiCamera;
        sync.blurSource = source;
        sync.panels = panels;
        foreach (var obj in new Object[] { camera, baseData, source, sync, uiCamera, uiData, canvas })
        {
            EditorUtility.SetDirty(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Tutorial blur scene save failed.");
        File.WriteAllText(ReportPath, $"scene={scene.path}\nblur=Translucent Image 7.0.1\ndownsample=2\nstrength={config.Strength}\n" +
            $"guideTint={RehearBlurDiagnostics.GuideWhiteTint}\nintroTint={RehearBlurDiagnostics.IntroWhiteTint}\notherTint=0.16\nworldMask={camera.cullingMask}\nuiMask={uiCamera.cullingMask}\n" +
            $"stackCount={baseData.cameraStack.Count}\ncurve={canvas.GetComponent<CurvedUISettings>().Angle}\n" +
            $"stageReferencePreserved=true\nquestDeviceTested=false\nsaved={DateTime.UtcNow:O}\n");
        Debug.Log("Rehear: Native Figma tutorial backgrounds connected to Translucent Image.", canvas);
    }
}
