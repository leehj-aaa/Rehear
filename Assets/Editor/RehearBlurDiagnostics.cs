using System;
using System.IO;
using System.Linq;
using System.Text;
using LeTai.Asset.TranslucentImage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
internal static class RehearBlurDiagnostics
{
    // User-approved glass balance over the live, linear-color VR scene.
    internal const float GuideWhiteTint = 0.10f;
    internal const float IntroWhiteTint = 0.16f;
    const string Request = "Temp/RehearBlurDiagnostics.request";
    static double next;
    static RehearBlurDiagnostics() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "tune") Tune(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearBlurDiagnostics.txt", e.ToString()); }
    }
    static void Tune()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Tune blur in Edit mode while not baking.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity")
            throw new InvalidOperationException("Open the tutorial scene.");
        var config = AssetDatabase.LoadAssetAtPath<ScalableBlurConfig>("Assets/Settings/Tutorial Quest3 Blur.asset");
        Undo.RecordObject(config, "Reveal background through tutorial glass");
        config.Strength = 12;
        config.UseStrength = true;
        EditorUtility.SetDirty(config);
        foreach (var panel in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TranslucentImage>(true)))
        {
            Undo.RecordObjects(new UnityEngine.Object[] { panel, panel.material }, "Reveal tutorial glass background");
            var view = panel.GetComponentInParent<TutorialFigmaView>();
            bool isIntro = view && view.steps.Length > 1 && panel.transform.IsChildOf(view.steps[1].transform);
            panel.foregroundOpacity = isIntro ? IntroWhiteTint : panel.name == "Controller guide glass" ? GuideWhiteTint : 0.16f;
            panel.material.SetFloat("_GlassTint", panel.foregroundOpacity);
            panel.SetAllDirty();
            EditorUtility.SetDirty(panel);
            EditorUtility.SetDirty(panel.material);
        }
        foreach (var source in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<TranslucentImageSource>(true)))
        {
            Undo.RecordObject(source, "Cover curved tutorial edges in blur source");
            source.SkipCulling = true;
            EditorUtility.SetDirty(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save tutorial glass.");
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearBlurTuning.txt", $"strength=12\nguideWhiteTint={GuideWhiteTint}\nintroWhiteTint={IntroWhiteTint}\notherWhiteTint=0.16\ncurvedEdgesCovered=true\nsaved=true\n");
    }
    [MenuItem("Rehear/Inspect Live Tutorial Blur")]
    static void Inspect()
    {
        var report = new StringBuilder();
        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"CAMERA {camera.name} active={camera.isActiveAndEnabled} mask={camera.cullingMask} type={camera.GetUniversalAdditionalCameraData().renderType}");
        foreach (var source in UnityEngine.Object.FindObjectsByType<TranslucentImageSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var rt = source.BlurredScreen;
            report.AppendLine($"SOURCE {source.name} active={source.isActiveAndEnabled} texture={(rt ? rt.name : "null")} region={source.BlurRegion} update={source.MaxUpdateRate}");
            if (!rt || !rt.IsCreated()) continue;
            report.AppendLine($"RT size={rt.width}x{rt.height} dimension={rt.dimension}");
            var previous = RenderTexture.active;
            Texture2D snapshot = null;
            try
            {
                RenderTexture.active = rt;
                snapshot = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                snapshot.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                snapshot.Apply();
                var pixels = snapshot.GetPixels();
                report.AppendLine($"RT brightness min={pixels.Min(c=>c.grayscale)} max={pixels.Max(c=>c.grayscale)} mean={pixels.Average(c=>c.grayscale)}");
                File.WriteAllBytes("Temp/RehearBlurSource.png", snapshot.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; if (snapshot) UnityEngine.Object.DestroyImmediate(snapshot); }
        }
        foreach (var panel in UnityEngine.Object.FindObjectsByType<TranslucentImage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var mat = panel.materialForRendering;
            report.AppendLine($"PANEL {panel.name} active={panel.isActiveAndEnabled} layer={panel.gameObject.layer} mat={mat.name} shader={mat.shader.name} tint={mat.GetFloat("_GlassTint")} blur={mat.GetFloat("_UseBlur")} source={panel.source?.name} tex={mat.GetTexture("_BlurTex")?.name} crop={mat.GetVector("_CropRegion")}");
        }
        File.WriteAllText("Temp/RehearBlurDiagnostics.txt", report.ToString());
    }
}
