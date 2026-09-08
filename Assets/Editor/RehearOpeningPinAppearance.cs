using System;
using System.IO;
using System.Linq;
using System.Text;
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

/// <summary>One-shot authoring and read-only verification of the opening PIN glass.</summary>
[InitializeOnLoad]
internal static class RehearOpeningPinAppearance
{
    private const string Request = "Temp/RehearOpeningPinAppearance.request";
    private const string Report = "Temp/RehearOpeningPinAppearance.txt";
    static RehearOpeningPinAppearance() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(Request).Trim();
        if (command == "apply" && EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try
        {
            if (command == "apply") Apply();
            else if (command == "verify") Verify();
        }
        catch (Exception exception)
        {
            File.WriteAllText(Report, "FAIL\n" + exception);
            Debug.LogException(exception);
        }
    }

    [MenuItem("Rehear/Apply Opening PIN Glass And Radial Light")]
    private static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "Scene_00" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open Scene_00 in Edit mode.");
        var canvas = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Canvas>(true))
            .Single(c => c.name == "OpeningPinCanvas");
        var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true))
            .Single(c => c.CompareTag("MainCamera"));
        var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipeline) throw new InvalidOperationException("Opening PIN glass requires the project's URP pipeline.");
        var baseData = camera.GetUniversalAdditionalCameraData();
        if (baseData.renderType != CameraRenderType.Base) throw new InvalidOperationException("Expected base camera.");
        int rendererIndex = new SerializedObject(baseData).FindProperty("m_RendererIndex").intValue;
        var pipelineData = new SerializedObject(pipeline);
        if (rendererIndex < 0) rendererIndex = pipelineData.FindProperty("m_DefaultRendererIndex").intValue;
        var renderer = pipelineData.FindProperty("m_RendererDataList").GetArrayElementAtIndex(rendererIndex)
            .objectReferenceValue as UniversalRendererData;
        if (!renderer) throw new InvalidOperationException("Opening camera renderer data missing.");

        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Opening PIN glass and radial light");
        Undo.RegisterFullObjectHierarchyUndo(camera.gameObject, "Opening PIN blur camera");
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

        const string configPath = "Assets/Settings/Opening PIN Blur.asset";
        var config = AssetDatabase.LoadAssetAtPath<ScalableBlurConfig>(configPath);
        if (!config)
        {
            config = ScriptableObject.CreateInstance<ScalableBlurConfig>();
            AssetDatabase.CreateAsset(config, configPath);
        }
        config.Mode = ScalableBlurConfig.BlurMode.Performance;
        config.Strength = 18;
        config.UseStrength = true;
        config.ReferenceResolution = new Vector2(1920, 1080);
        EditorUtility.SetDirty(config);
        var source = camera.GetComponent<TranslucentImageSource>();
        if (!source) source = Undo.AddComponent<TranslucentImageSource>(camera.gameObject);
        source.BlurConfig = config;
        source.Downsample = 2;
        source.MaxUpdateRate = float.PositiveInfinity;
        source.BlurRegion = new Rect(0, 0, 1, 1);
        source.Preview = false;
        source.SkipCulling = true;

        foreach (string name in new[] { "PinPanel", "LoadingPanel", "NumberKeyboard" })
        {
            var panel = canvas.transform.Find(name).gameObject;
            var oldImage = panel.GetComponent<Image>();
            var material = oldImage.material;
            var glass = oldImage as TranslucentImage;
            if (!glass)
            {
                Undo.DestroyObjectImmediate(oldImage);
                glass = Undo.AddComponent<TranslucentImage>(panel);
            }
            bool keyboard = name == "NumberKeyboard";
            glass.material = material;
            glass.color = keyboard ? new Color(.035f, .055f, .18f, 1) : new Color(.86f, .90f, 1, 1);
            glass.source = source;
            glass.textureAlphaMode = TranslucentImage.TextureAlphaMode.Alpha;
            glass.foregroundOpacity = keyboard ? .75f : .32f;
            glass.vibrancy = 1;
            glass.brightness = 0;
            glass.flatten = 0;
            glass.raycastTarget = true;
            material.SetFloat("_UseBlur", 1);
            material.SetFloat("_GlassTint", glass.foregroundOpacity);
            material.SetFloat("_TopGlowStrength", keyboard ? 0 : .70f);
            material.SetColor("_TopGlowColor", new Color(.30f, .46f, 1));
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(glass);
            glass.SetAllDirty();
            if (!keyboard) AddRadialLight(panel.transform);
        }

        int uiLayer = LayerMask.NameToLayer("UI");
        foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = uiLayer;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 | AdditionalCanvasShaderChannels.TexCoord3;
        canvas.worldCamera = camera;
        canvas.GetComponent<CurvedUISettings>()?.AddEffectToChildren();
        var overlay = camera.transform.Find("Opening PIN UI Overlay Camera");
        if (!overlay)
        {
            var go = new GameObject("Opening PIN UI Overlay Camera");
            Undo.RegisterCreatedObjectUndo(go, "Opening UI camera");
            go.transform.SetParent(camera.transform, false);
            overlay = go.transform;
        }
        var uiCamera = overlay.GetComponent<Camera>();
        if (!uiCamera) uiCamera = Undo.AddComponent<Camera>(overlay.gameObject);
        uiCamera.CopyFrom(camera);
        uiCamera.tag = "Untagged";
        uiCamera.cullingMask = 1 << uiLayer;
        uiCamera.clearFlags = CameraClearFlags.Nothing;
        uiCamera.depth = camera.depth + 1;
        uiCamera.targetTexture = null;
        uiCamera.enabled = true;
        overlay.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        var uiData = uiCamera.GetUniversalAdditionalCameraData();
        uiData.renderType = CameraRenderType.Overlay;
        var overlayData = new SerializedObject(uiData);
        overlayData.FindProperty("m_ClearDepth").boolValue = false;
        overlayData.ApplyModifiedProperties();
        uiData.renderPostProcessing = false;
        uiData.renderShadows = false;
        uiData.allowXRRendering = true;
        uiData.requiresColorOption = CameraOverrideOption.Off;
        uiData.requiresDepthOption = CameraOverrideOption.Off;
        uiData.SetRenderer(rendererIndex);
        camera.cullingMask &= ~(1 << uiLayer);
        if (!baseData.cameraStack.Contains(uiCamera)) baseData.cameraStack.Add(uiCamera);
        var sync = camera.GetComponent<TutorialBlurCameraSync>();
        if (!sync) sync = Undo.AddComponent<TutorialBlurCameraSync>(camera.gameObject);
        sync.sceneCamera = camera;
        sync.uiCamera = uiCamera;
        sync.blurSource = source;
        sync.panels = canvas.GetComponentsInChildren<TranslucentImage>(true);
        foreach (var obj in new Object[] { camera, baseData, source, sync, uiCamera, uiData, canvas })
        {
            EditorUtility.SetDirty(obj);
            PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Opening scene save failed.");
        File.WriteAllText(Report, "PASS saved: 3 live blur panels, 2 radial lights, UI overlay camera.\nRenderer=" + AssetDatabase.GetAssetPath(renderer));
    }

    private static void AddRadialLight(Transform panel)
    {
        var light = panel.Find("Top Radial Light");
        if (!light)
        {
            var go = new GameObject("Top Radial Light", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Top radial light");
            go.transform.SetParent(panel, false);
            light = go.transform;
        }
        var rect = (RectTransform)light;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(0, 440);
        rect.sizeDelta = new Vector2(1300, 650);
        rect.localScale = Vector3.one;
        rect.SetAsFirstSibling();
        const string path = "Assets/Settings/Opening PIN Radial Light.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Rehear/UI/Soft Radial Glow"));
            AssetDatabase.CreateAsset(material, path);
        }
        var image = light.GetComponent<Image>();
        image.material = material;
        image.color = new Color(.15f, .30f, 1, .22f);
        image.raycastTarget = false;
    }

    private static void Verify()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Verify in Play mode after opening PIN.");
        var report = new StringBuilder();
        // Construct the actual native database client without reading/consuming a session PIN.
        var database = Firebase.Database.FirebaseDatabase.DefaultInstance;
        var root = database.GetReference("presentation_data");
        report.AppendLine("PASS Firebase App + Database native client initialized; no PIN read or submitted.");
        var panels = Object.FindObjectsByType<TranslucentImage>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(g => g.gameObject.scene.name == "Scene_00").ToArray();
        if (panels.Length != 3) throw new InvalidOperationException("Expected all three opening glass panels.");
        foreach (var glass in panels)
        {
            if (glass.gameObject.scene.name != "Scene_00") continue;
            var texture = glass.source ? glass.source.BlurredScreen : null;
            report.AppendLine($"{glass.name}: active={glass.isActiveAndEnabled} blur={glass.material.GetFloat("_UseBlur")} " +
                $"textureReady={texture && texture.IsCreated()} radial={glass.material.GetFloat("_TopGlowStrength")}");
            if (!texture || !texture.IsCreated()) throw new InvalidOperationException("Blur source texture not rendered.");
            if (!glass.material.shader.isSupported || ShaderUtil.GetShaderMessages(glass.material.shader)
                .Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                throw new InvalidOperationException("Panel shader error.");
        }
        var manager = Object.FindObjectsByType<PinInputManager>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Single(m => m.gameObject.scene.name == "Scene_00");
        if (manager.GetFullPin().Length == 0 && manager.transform.Find("PinPanel").gameObject.activeSelf)
        {
            manager.OpenKeyboard();
            // Exercise real button bindings without submitting a complete PIN.
            foreach (string key in new[] { "Key0", "Key8" })
                manager.GetComponentsInChildren<Button>(true).Single(b => b.name == key).onClick.Invoke();
            if (manager.GetFullPin() != "08") throw new InvalidOperationException("PIN button binding changed.");
            manager.GetComponentsInChildren<Button>(true).Single(b => b.name == "ResetPin").onClick.Invoke();
            if (manager.GetFullPin() != "") throw new InvalidOperationException("PIN clear failed.");
            report.AppendLine("PASS actual keypad callbacks: leading zero, digit append, clear. No submit.");
        }
        ScreenCapture.CaptureScreenshot("Temp/RehearOpeningPin-glass.png");
        File.WriteAllText(Report, report.ToString());
    }
}
