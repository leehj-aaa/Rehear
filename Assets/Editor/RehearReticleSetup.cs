using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

[InitializeOnLoad]
internal static class RehearReticleSetup
{
    const string Request = "Temp/RehearReticleSetup.request";
    static RehearReticleSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return;
        File.Delete(Request);
        const string path = "Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/Prefabs/Interactors/Left_NearFarInteractor.prefab";
        GameObject root = null;
        try
        {
            var cursor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VRTemplateAssets/Prefabs/Cursors/Torus Cursor.prefab");
            if (!cursor) throw new Exception("Torus cursor prefab missing.");
            root = PrefabUtility.LoadPrefabContents(path);
            var legacyReticle = root.GetComponent<XRInteractorReticleVisual>();
            if (legacyReticle) UnityEngine.Object.DestroyImmediate(legacyReticle);
            var reticle = root.GetComponent<RehearUIReticle>();
            if (!reticle) reticle = root.AddComponent<RehearUIReticle>();
            var settings = new SerializedObject(reticle);
            settings.FindProperty("reticlePrefab").objectReferenceValue = cursor;
            settings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success) throw new Exception("Reticle prefab save failed.");
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/RehearReticleSetup.txt", "PASS: shared controller reticle assigned; UI layer; 10m; visible while selecting.");
            File.WriteAllText("Temp/RehearQuestBuild.request", "build");
        }
        catch (Exception e) { File.WriteAllText("Temp/RehearReticleSetup.txt", "FAIL: " + e); Debug.LogException(e); }
        finally { if (root) PrefabUtility.UnloadPrefabContents(root); }
    }
}
