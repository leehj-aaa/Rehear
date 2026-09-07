using System;
using System.IO;
using System.Linq;
using System.Text;
using CurvedUI;
using CurvedUI.Core.Integrations;
using LeTai.Asset.TranslucentImage;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearTutorialUIRepair
{
    private const string Request = "Temp/RehearTutorialUIRepair.request";
    private const string Report = "Temp/RehearTutorialUIRepair-validation.txt";
    private static double ready;
    static RehearTutorialUIRepair()
    {
        if (!File.Exists(Request)) return;
        ready = EditorApplication.timeSinceStartup + 5;
        EditorApplication.update += Pending;
    }
    private static void Pending()
    {
        if (EditorApplication.timeSinceStartup < ready || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        EditorApplication.update -= Pending;
        File.Delete(Request);
        try { Repair(); }
        catch (Exception e) { File.AppendAllText(Report, "FAILED\n" + e); Debug.LogException(e); }
    }
    [MenuItem("Rehear/Repair And Validate Tutorial UI")]
    public static void Repair()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" || EditorApplication.isPlaying)
            throw new InvalidOperationException("Open the tutorial in Edit mode.");
        File.WriteAllText(Report, "started=" + DateTime.UtcNow.ToString("O") + "\n");
        var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "TutorialUI");
        var view = canvas.GetComponentInChildren<TutorialFigmaView>(true);
        if (!view) throw new InvalidOperationException("Native tutorial view is missing.");
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Repair tutorial UI rendering");
        var rename = new System.Collections.Generic.Dictionary<string, string>
        {
            ["UI 버튼 선택 card"] = "Callout_Select",
            ["일시정지 card"] = "Callout_Pause",
            ["슬라이드 이동 card"] = "Callout_Slide",
            ["대본 페이지 이동 card"] = "Callout_Script"
        };
        foreach (var transform in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
        {
            if (rename.TryGetValue(transform.name, out var english)) transform.name = english;
            if (transform.name.Any(c => c >= '\uAC00' && c <= '\uD7A3'))
                File.AppendAllText(Report, "REMAINING_KOREAN_NAME=" + transform.name + "\n");
        }
        foreach (var image in canvas.GetComponentsInChildren<Image>(true))
        {
            if (!image.material || image.material.shader.name != "Rehear/UI/Rounded Translucent Panel") continue;
            image.canvasRenderer.cullTransparentMesh = false;
            if (image is TranslucentImage glass) image.material.SetFloat("_GlassTint", glass.foregroundOpacity);
            image.SetAllDirty();
            EditorUtility.SetDirty(image.material);
        }
        view.Show(0);
        RehearTutorialBlurSetup.ApplyBlur();
        foreach (var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.renderMode = TextRenderFlags.Render;
            text.SetAllDirty();
            text.ForceMeshUpdate(true, true);
            var curve = text.GetComponent<CurvedUITMP>();
            if (curve) curve.Dirty = true;
        }
        foreach (var effect in canvas.GetComponentsInChildren<CurvedUIVertexEffect>(true)) effect.SetDirty();
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        File.AppendAllText(Report, "repairSaved=true\n");
        EditorApplication.delayCall += Validate;
    }
    [MenuItem("Rehear/Validate Tutorial UI Rendering")]
    public static void Validate()
    {
        try
        {
            var scene = EditorSceneManager.GetActiveScene();
            var canvas = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "TutorialUI");
            var shader = Shader.Find("Rehear/UI/Rounded Translucent Panel");
            var report = new StringBuilder();
            report.AppendLine("shaderSupported=" + shader.isSupported);
            foreach (var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                report.AppendLine($"FONT={text.name}: {text.font.name}, {text.fontSize}, {text.text.Replace('\n', ' ')}");
            foreach (var error in ShaderUtil.GetShaderMessages(shader)) report.AppendLine($"shader={error.severity}: {error.message}");
            foreach (var renderer in canvas.GetComponentsInChildren<CanvasRenderer>(true))
            {
                var mesh = renderer.GetMesh();
                if (!mesh) continue;
                if (mesh.vertices.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)))
                    report.AppendLine("INVALID_MESH=" + renderer.name);
            }
            File.AppendAllText(Report, report.ToString());
            Capture();
        }
        catch (Exception e) { File.AppendAllText(Report, "VALIDATION_FAILED\n" + e); Debug.LogException(e); }
    }
    private static void Capture()
    {
        var camera = Camera.main;
        var view = UnityEngine.Object.FindFirstObjectByType<TutorialFigmaView>();
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        var projection = camera.projectionMatrix;
        var target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        var previous = RenderTexture.active;
        var canvas = view.GetComponentInParent<Canvas>();
        var uiCamera = camera.GetComponent<TutorialBlurCameraSync>().uiCamera;
        var eventCamera = canvas.worldCamera;
        var uiData = new SerializedObject(uiCamera.GetUniversalAdditionalCameraData());
        bool clearDepth = uiData.FindProperty("m_ClearDepth").boolValue;
        try
        {
            camera.transform.SetPositionAndRotation(view.transform.position - view.transform.forward * 1.25f, view.transform.rotation);
            camera.ResetProjectionMatrix();
            Canvas.ForceUpdateCanvases();
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new InvalidOperationException("URP render request unsupported.");
            File.AppendAllText(Report, $"canvasEnabled={canvas.enabled}, active={canvas.gameObject.activeInHierarchy}, viewport={camera.WorldToViewportPoint(view.transform.position)}\n");
            for (int variant = 0; variant < 3; variant++)
            {
                if (variant == 1) canvas.worldCamera = uiCamera;
                if (variant == 2)
                {
                    uiData.FindProperty("m_ClearDepth").boolValue = true;
                    uiData.ApplyModifiedPropertiesWithoutUndo();
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = target;
                var png = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                png.Apply();
                File.WriteAllBytes($"Temp/RehearTutorialUI-preview-{variant}.png", png.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(png);
            }
            File.AppendAllText(Report, "previews=Temp/RehearTutorialUI-preview-[0,1,2].png\n");
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.projectionMatrix = projection;
            canvas.worldCamera = eventCamera;
            uiData.FindProperty("m_ClearDepth").boolValue = clearDepth;
            uiData.ApplyModifiedPropertiesWithoutUndo();
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
