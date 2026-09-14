using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Scene-only overrides: never change the shared XR rig or the default profile.
internal static class RehearTutorialRenderingSetup
{
    private const string ScenePath = "Assets/01_Scene/Scene_00_5_Tutorial.unity";
    private const string ProfilePath = "Assets/Settings/Tutorial Quest3 Volume.asset";
    private const string AllScenesReport = "Temp/RehearQuest3Scenes-validation.txt";

    [MenuItem("Rehear/Apply Tutorial Quest 3 Rendering")]
    private static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        ConfigureScene(scene);
    }

    [MenuItem("Rehear/Apply Quest 3 Rendering To All Production Scenes")]
    private static void ApplyAllScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        var report = new System.Text.StringBuilder();
        // Open additively so the user's current scene, selection and unsaved work
        // are never unloaded. Only close the extra scenes this command opens.
        foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled &&
                     s.path.StartsWith("Assets/01_Scene/Scene_")))
        {
            var scene = SceneManager.GetSceneByPath(entry.path);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
                scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
            try
            {
                ConfigureScene(scene);
                var camera = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(c => c.gameObject.scene == scene && c.CompareTag("MainCamera"));
                var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault(v => v.gameObject.scene == scene && v.name == "Global Volume");
                if (camera == null || volume == null || AssetDatabase.GetAssetPath(volume.sharedProfile) != ProfilePath)
                    throw new System.InvalidOperationException($"Quest 3 settings were not saved: {entry.path}; camera={camera}; volume={volume}; profile={(volume != null ? AssetDatabase.GetAssetPath(volume.sharedProfile) : "missing")}");
                report.AppendLine($"{entry.path}: post={camera.GetUniversalAdditionalCameraData().renderPostProcessing}, MSAA={camera.allowMSAA}, HDR={camera.allowHDR}, profile={ProfilePath}");
            }
            finally
            {
                if (!wasLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
        report.AppendLine($"saved={System.DateTime.UtcNow:O}");
        System.IO.File.WriteAllText(AllScenesReport, report.ToString());
        Debug.Log("Rehear: Quest 3 camera and shared volume settings applied to all five production scenes.");
    }

    private static void ConfigureScene(Scene scene)
    {

        var camera = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(c => c.gameObject.scene == scene && c.CompareTag("MainCamera"));
        var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(v => v.gameObject.scene == scene && v.name == "Global Volume");
        var pipeline = (QualitySettings.renderPipeline != null
            ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        if (camera == null || pipeline == null || pipeline.msaaSampleCount != 4)
        {
            Debug.LogError("Tutorial rendering: expected MainCamera, Global Volume and a 4x MSAA URP asset. No scene changes made.");
            return;
        }
        if (volume == null)
        {
            var volumeObject = new GameObject("Global Volume");
            SceneManager.MoveGameObjectToScene(volumeObject, scene);
            Undo.RegisterCreatedObjectUndo(volumeObject, "Add Quest 3 Global Volume");
            volume = volumeObject.AddComponent<Volume>();
        }

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Tutorial Quest3 Volume";
            AssetDatabase.CreateAsset(profile, ProfilePath);

            var color = Add<ColorAdjustments>(profile);
            color.postExposure.Override(0f);
            color.contrast.Override(6f);
            color.saturation.Override(4f);
            color.colorFilter.Override(Color.white);
            color.hueShift.Override(0f);
            var white = Add<WhiteBalance>(profile);
            white.temperature.Override(3f);
            white.tint.Override(0f);
            // Neutral keeps the blue/white tutorial UI closer to its original colors.
            Add<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);

            // Explicit zero overrides also neutralize effects inherited from the
            // project's default volume stack. Inactive components would not do that.
            Add<Bloom>(profile).intensity.Override(0f);
            Add<DepthOfField>(profile).mode.Override(DepthOfFieldMode.Off);
            Add<MotionBlur>(profile).intensity.Override(0f);
            Add<ChromaticAberration>(profile).intensity.Override(0f);
            Add<Vignette>(profile).intensity.Override(0f);
            Add<FilmGrain>(profile).intensity.Override(0f);
            Add<LensDistortion>(profile).intensity.Override(0f);
            foreach (var component in profile.components)
                EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(profile);
        }

        var data = camera.GetUniversalAdditionalCameraData();
        Undo.RecordObjects(new Object[] { camera, data, volume }, "Configure Tutorial Quest 3 Rendering");
        camera.allowMSAA = true;
        camera.allowHDR = true;
        camera.useOcclusionCulling = true;
        data.renderPostProcessing = true;
        data.allowXRRendering = true;
        data.allowHDROutput = true;
        // This dropdown is POST-process AA, not URP's 4x hardware MSAA.
        // FXAA softens small VR text; TAA adds history/ghosting and disables MSAA.
        data.antialiasing = AntialiasingMode.None;
        data.stopNaN = false;
        data.dithering = false;
        data.volumeLayerMask |= 1 << volume.gameObject.layer;
        data.volumeTrigger = camera.transform;
        volume.isGlobal = true;
        volume.enabled = true;
        volume.weight = 1f;
        volume.sharedProfile = profile;
        // The authored room PostFX stays above generic XR defaults regardless of
        // volume registration order when entering the tutorial from another scene.
        if (scene.path == ScenePath) volume.priority = -1f;

        foreach (var item in new Object[] { camera, data, volume })
        {
            EditorUtility.SetDirty(item);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new System.InvalidOperationException("Could not save Quest 3 rendering: " + scene.path);
        System.IO.File.WriteAllText("Temp/RehearTutorialRendering-validation.txt",
            $"scene={scene.path}\nprofile={AssetDatabase.GetAssetPath(volume.sharedProfile)}\n" +
            $"camera={camera.name}\npostProcessing={data.renderPostProcessing}\n" +
            $"cameraMSAA={camera.allowMSAA}\npipelineMSAA={pipeline.msaaSampleCount}\n" +
            $"postAA={data.antialiasing}\ncameraHDR={camera.allowHDR}\n" +
            $"volumeMask={data.volumeLayerMask.value}\nvolumeWeight={volume.weight}\n" +
            $"saved={System.DateTime.UtcNow:O}\n");
        Debug.Log($"Rehear: {scene.name} camera and Quest 3 volume saved (4x MSAA, lightweight color grading, HDR enabled).", camera);
    }

    private static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var component = profile.Add<T>();
        component.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }
}
