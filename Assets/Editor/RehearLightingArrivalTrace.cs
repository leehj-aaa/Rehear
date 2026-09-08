using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Read-only, opt-in diagnostics: never changes lighting, cameras, or scene assets.
[InitializeOnLoad]
internal static class RehearLightingArrivalTrace
{
    const string Request = "Temp/RehearLightingArrivalTrace.request";
    const string Report = "Temp/RehearLightingArrivalTrace.txt";
    static double start = -1;
    static double next;
    static bool armed;
    static RehearLightingArrivalTrace() => RenderPipelineManager.endCameraRendering += Observe;
    static void Observe(ScriptableRenderContext context, Camera camera)
    {
        if (File.Exists(Request)) { File.Delete(Request); armed = true; start = -1; File.WriteAllText(Report, "Read-only arrival trace\n"); }
        if (!armed || SceneManager.GetActiveScene().name != "Scene_00_5_Tutorial" || camera.cameraType != CameraType.SceneView) return;
        double now = EditorApplication.timeSinceStartup;
        if (start < 0) { start = now; next = now; }
        if (now < next) return;
        next = now + 1;
        var stack = VolumeManager.instance.stack;
        var white = stack.GetComponent<WhiteBalance>();
        var color = stack.GetComponent<ColorAdjustments>();
        var tone = stack.GetComponent<Tonemapping>();
        var view = SceneView.sceneViews.Cast<SceneView>().FirstOrDefault(v => v.camera == camera);
        string maps = string.Join(",", LightmapSettings.lightmaps.Select(m => m.lightmapColor ? m.lightmapColor.imageContentsHash.ToString() : "none"));
        File.AppendAllText(Report, $"{now-start:F1}s play={EditorApplication.isPlaying} sceneLight={view?.sceneLighting} effects={view?.sceneViewState.showImageEffects} white={white.temperature.value} tint={white.tint.value} exposure={color.postExposure.value} filter={color.colorFilter.value} tone={tone.mode.value} maps={maps} probes={LightmapSettings.lightProbes?.count} ambient={RenderSettings.ambientIntensity} reflection={RenderSettings.reflectionIntensity} camera={camera.transform.position}\n");
        if (now - start >= 30) armed = false;
    }
}
