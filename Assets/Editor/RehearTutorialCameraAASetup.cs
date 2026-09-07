using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

internal static class RehearTutorialCameraAASetup
{
    private const string Report = "Temp/RehearTutorialCameraAA-validation.txt";
    [MenuItem("Rehear/Apply Tutorial SMAA Low Preview")]
    private static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        var camera = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(c => c.gameObject.scene == scene && c.CompareTag("MainCamera"));
        var pipeline = (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline :
            GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        if (camera == null || pipeline == null || pipeline.msaaSampleCount != 4)
        {
            Debug.LogError("Camera AA setup requires the tutorial MainCamera and existing 4x MSAA pipeline.");
            return;
        }
        var data = camera.GetUniversalAdditionalCameraData();
        Undo.RecordObjects(new Object[] { camera, data }, "Preview SMAA Low on Tutorial camera");
        camera.allowMSAA = true;
        camera.allowDynamicResolution = false;
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.Low;
        foreach (var item in new Object[] { camera, data })
        {
            EditorUtility.SetDirty(item);
            PrefabUtility.RecordPrefabInstancePropertyModifications(item);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("Camera AA scene save failed.");
        System.IO.File.WriteAllText(Report,
            $"scene={scene.path}\ncamera={camera.name}\npostAA={data.antialiasing}\nquality={data.antialiasingQuality}\n" +
            $"MSAA={pipeline.msaaSampleCount}\nallowMSAA={camera.allowMSAA}\ndynamicResolution={camera.allowDynamicResolution}\n" +
            $"devicePerformanceTested=false\nsaved={System.DateTime.UtcNow:O}\n");
        Selection.activeGameObject = camera.gameObject;
        Debug.Log("Rehear: Tutorial SMAA Low preview saved. 4x MSAA retained; Dynamic Resolution off. Quest GPU timing still needs testing.", camera);
    }
}
