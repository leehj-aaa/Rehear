using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearVignetteSetup
{
    static RehearVignetteSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string request = "Temp/RehearVignette.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !VolumeManager.instance.isInitialized) return;
        try { var command = File.ReadAllText(request).Trim(); File.Delete(request); if (command == "apply") Apply(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearVignette.txt", e.ToString()); }
    }
    [MenuItem("Rehear/Set Gentle Presentation Vignette")]
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_02_Presentation.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the presentation scene in Edit mode.");
        const string path = "Assets/Settings/Presentation Vignette.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,path); }
        if (!profile.TryGet<Vignette>(out var vignette)) { vignette = profile.Add<Vignette>(); AssetDatabase.AddObjectToAsset(vignette,profile); }
        // A vignette-only override keeps the room's authored white balance, exposure,
        // tonemapping and baked lighting untouched despite the higher volume priority.
        if (profile.components.Count != 1) throw new InvalidOperationException("Vignette profile must not contain color grading.");
        Undo.RecordObject(vignette,"Gentle presentation vignette");
        vignette.active = true; vignette.color.Override(Color.black);
        vignette.center.Override(new Vector2(.5f,.5f)); vignette.rounded.Override(false);
        vignette.intensity.Override(.20f); vignette.smoothness.Override(.70f);
        var volumes = scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Volume>(true)).ToArray();
        var volume = volumes.FirstOrDefault(v=>v.sharedProfile == profile);
        if (!volume)
        {
            var go = new GameObject("Presentation Vignette");
            Undo.RegisterCreatedObjectUndo(go,"Create vignette override");
            var parent = volumes.FirstOrDefault(v=>v.name == "Global Volume");
            if (parent) go.transform.SetParent(parent.transform,false);
            volume = go.AddComponent<Volume>();
        }
        Undo.RecordObject(volume,"Connect presentation vignette");
        volume.sharedProfile = profile; volume.isGlobal = true; volume.weight = 1; volume.enabled = true;
        volume.priority = Mathf.Max(1,volumes.Where(v=>v!=volume).Select(v=>v.priority).DefaultIfEmpty(0).Max()+1);
        volume.gameObject.SetActive(true);
        EditorUtility.SetDirty(profile); EditorUtility.SetDirty(vignette); EditorUtility.SetDirty(volume);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save vignette settings.");
        Selection.activeGameObject = volume.gameObject;
        SceneView.RepaintAll(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearVignette.request", "inspect");
    }
    static void Inspect()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var cameras = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).ToArray();
        var camera = cameras.Single(c => c.CompareTag("MainCamera"));
        var report = new StringBuilder();
        foreach (SceneView view in SceneView.sceneViews)
            report.AppendLine($"SCENE VIEW effects={view.sceneViewState.showImageEffects} allEffects={view.sceneViewState.fxEnabled} lighting={view.sceneLighting}");
        report.AppendLine($"PIPELINE {GraphicsSettings.currentRenderPipeline.name}; camera post={camera.GetUniversalAdditionalCameraData().renderPostProcessing}");
        var stack = VolumeManager.instance.CreateStack();
        try
        {
            var data = camera.GetUniversalAdditionalCameraData();
            VolumeManager.instance.Update(stack,data.volumeTrigger ? data.volumeTrigger : camera.transform,data.volumeLayerMask);
            var effective = stack.GetComponent<Vignette>();
            report.AppendLine($"EFFECTIVE intensity={effective.intensity.value} smoothness={effective.smoothness.value}");
        }
        finally { VolumeManager.instance.DestroyStack(stack); }
        var volumes = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Volume>(true)).Where(v => v.isActiveAndEnabled && v.sharedProfile).ToArray();
        var vigs = volumes.Select(v => v.profile.TryGet<Vignette>(out var vignette) ? vignette : null).Where(v => v).ToArray();
        var values = vigs.Select(v => v.intensity.value).ToArray();
        var overrides = vigs.Select(v => v.intensity.overrideState).ToArray();
        var before = Capture(camera, "Temp/RehearVignetteBefore.png");
        try
        {
            foreach (var v in vigs) v.intensity.Override(0);
            var off = Capture(camera, "Temp/RehearVignetteOff.png");
            double edge = 0, center = 0; int ne = 0, nc = 0;
            for (int y = 0; y < 720; y++) for (int x = 0; x < 1280; x++)
            {
                int i = y * 1280 + x;
                double d = (off[i].r + off[i].g + off[i].b - before[i].r - before[i].g - before[i].b) / 3.0;
                if (x < 128 || x > 1152 || y < 72 || y > 648) { edge += d; ne++; }
                if (x > 576 && x < 704 && y > 324 && y < 396) { center += d; nc++; }
            }
            report.AppendLine($"RENDER on/off average darkening (0..255): edges={edge/ne:F3}, center={center/nc:F3}");
        }
        finally { for (int i = 0; i < vigs.Length; i++) { vigs[i].intensity.value = values[i]; vigs[i].intensity.overrideState = overrides[i]; } }
        File.WriteAllText("Temp/RehearVignette.txt", report.ToString());
    }
    static Color32[] Capture(Camera camera, string path)
    {
        var rt = new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
        var old = RenderTexture.active;
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        var projection = camera.projectionMatrix;
        var data = camera.GetUniversalAdditionalCameraData(); var xr = data.allowXRRendering;
        var overlay = data.cameraStack.FirstOrDefault(c => c);
        var overlayPosition = overlay ? overlay.transform.position : Vector3.zero;
        var overlayRotation = overlay ? overlay.transform.rotation : Quaternion.identity;
        var overlayProjection = overlay ? overlay.projectionMatrix : Matrix4x4.identity;
        var sceneView = SceneView.lastActiveSceneView;
        // Compare from the view the user is inspecting, restoring the XR camera afterwards.
        if (sceneView)
        {
            camera.transform.SetPositionAndRotation(sceneView.camera.transform.position,sceneView.camera.transform.rotation);
            camera.worldToCameraMatrix = sceneView.camera.worldToCameraMatrix;
            camera.projectionMatrix = Matrix4x4.Perspective(sceneView.camera.fieldOfView,1280f/720f,camera.nearClipPlane,camera.farClipPlane);
        }
        data.allowXRRendering = false;
        var texture = new Texture2D(1280,720,TextureFormat.RGB24,false);
        try
        {
            rt.Create();
            var request = new RenderPipeline.StandardRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply();
            File.WriteAllBytes(path,texture.EncodeToPNG()); return texture.GetPixels32();
        }
        finally
        {
            camera.transform.SetPositionAndRotation(position,rotation);
            camera.ResetWorldToCameraMatrix(); camera.projectionMatrix = projection; data.allowXRRendering = xr;
            if (overlay) { overlay.transform.SetPositionAndRotation(overlayPosition,overlayRotation); overlay.projectionMatrix = overlayProjection; }
            RenderTexture.active = old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture);
        }
    }
}
