using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RehearControllerRepair
{
    const string Request = "Temp/RehearControllerRepair.request";
    const string Report = "Temp/RehearControllerRepair.txt";
    static RehearControllerRepair() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return;
        File.Delete(Request);
        try
        {
            const string path = "Assets/01_Scene/Scene_00.unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var visual = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Single(t => t.name == "Right Controller Visual");
                Undo.RecordObject(visual, "Align controller with tracked hand");
                var position = visual.localPosition;
                position.x = 0f;
                visual.localPosition = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Opening scene save failed.");
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }

            const string prefabPath = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Interactors/Left_NearFarInteractor.prefab";
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var visual = root.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c && c.GetType().Name == "CurveVisualController");
                var settings = new SerializedObject(visual);
                settings.FindProperty("m_LineDynamicsMode").intValue = 0;
                settings.FindProperty("m_ExtendLineToEmptyHit").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success) throw new Exception("Ray prefab save failed.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Report, "PASS: right visual local X=0; shared near/far ray uses Traditional mode and extends to empty hits.");
        }
        catch (Exception e) { File.WriteAllText(Report, "FAIL: " + e); Debug.LogException(e); }
    }
}
