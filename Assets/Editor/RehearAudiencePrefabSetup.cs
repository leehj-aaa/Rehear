using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearAudiencePrefabSetup
{
    const string Request = "Temp/RehearAudiencePrefabs.request";
    static RehearAudiencePrefabSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try { if (command == "create") Create(); else Inspect(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearAudiencePrefabs.txt", "FAIL\n" + e); }
    }
    static Transform[] Characters()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
        var root = scene.GetRootGameObjects().Single(g => g.name == "Audience").transform;
        var children = root.Cast<Transform>().ToArray();
        if (children.Length != 6) throw new Exception("Expected exactly six audience members.");
        return children;
    }
    static void Inspect()
    {
        var report = new StringBuilder();
        foreach (var root in Characters())
        {
            report.AppendLine($"CHARACTER {root.name}, scale={root.localScale}, active={root.gameObject.activeSelf}");
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (!c) { report.AppendLine("MISSING SCRIPT"); continue; }
                if (c is MonoBehaviour || c is Animator) report.AppendLine("  " + c.GetType().Name + " on " + c.name);
                var so = new SerializedObject(c); var p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue || EditorUtility.IsPersistent(p.objectReferenceValue)) continue;
                    var target = p.objectReferenceValue as GameObject;
                    if (p.objectReferenceValue is Component component) target = component.gameObject;
                    if (target && target.transform != root && !target.transform.IsChildOf(root))
                        report.AppendLine($"  EXTERNAL {c.GetType().Name}.{p.propertyPath} -> {target.name}");
                }
            }
            foreach (var a in root.GetComponentsInChildren<Animator>(true))
                report.AppendLine($"  ANIM avatar={a.avatar?.name}, controller={a.runtimeAnimatorController?.name}");
        }
        File.WriteAllText("Temp/RehearAudiencePrefabs.txt", report.ToString());
    }

    [MenuItem("Rehear/Audience/Create Six Reusable Audience Prefabs")]
    static void Create()
    {
        const string folder = "Assets/03_Prefabs/Audience";
        var sources = Characters();
        foreach (var root in sources)
            if (AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/" + root.name + ".prefab"))
                throw new Exception("Existing prefab will not be overwritten: " + root.name);
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/03_Prefabs", "Audience");
        var preview = EditorSceneManager.NewPreviewScene();
        var report = new StringBuilder("PASS\nsourceScene=unchanged\n");
        try
        {
            foreach (var root in sources)
            {
                var clone = UnityEngine.Object.Instantiate(root.gameObject);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(clone, preview);
                try
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(clone))
                        PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    clone.name = root.name;
                    clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    clone.transform.localScale = root.lossyScale;
                    Validate(clone);
                    var sourceRenderers = root.GetComponentsInChildren<Renderer>(true);
                    var renderers = clone.GetComponentsInChildren<Renderer>(true);
                    if (sourceRenderers.Length != renderers.Length) throw new Exception("Renderer count changed.");
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        if (!sourceRenderers[i].sharedMaterials.SequenceEqual(renderers[i].sharedMaterials))
                            throw new Exception("Material assignments changed.");
                        renderers[i].lightmapIndex = -1;
                        renderers[i].realtimeLightmapIndex = -1;
                    }
                    string path = folder + "/" + root.name + ".prefab";
                    var saved = PrefabUtility.SaveAsPrefabAsset(clone, path, out bool success);
                    if (!success || !saved) throw new Exception("Prefab save failed: " + path);
                    // Reload the saved asset into an isolated scene to verify reusable references.
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(saved, preview);
                    try
                    {
                        Validate(instance);
                        var loaded = instance.GetComponentsInChildren<Renderer>(true);
                        if (loaded.Length != renderers.Length || instance.transform.position != Vector3.zero)
                            throw new Exception("Prefab round-trip failed.");
                        for (int i = 0; i < loaded.Length; i++)
                            if (!loaded[i].sharedMaterials.SequenceEqual(renderers[i].sharedMaterials))
                                throw new Exception("Saved materials differ.");
                        report.AppendLine($"{path}: renderers={loaded.Length}, materials=preserved, roundTrip=PASS");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
                finally { UnityEngine.Object.DestroyImmediate(clone); }
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText("Temp/RehearAudiencePrefabs.txt", report.ToString());
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void Validate(GameObject root)
    {
        foreach (var c in root.GetComponentsInChildren<Component>(true))
        {
            if (!c) throw new Exception("Missing component in " + root.name);
            if (c is MeshFilter filter && !filter.sharedMesh) throw new Exception("Missing mesh in " + c.name);
            if (c is SkinnedMeshRenderer skin && (!skin.sharedMesh || skin.bones.Any(b => !b)))
                throw new Exception("Missing skinned mesh or bone in " + c.name);
            if (c is Renderer renderer && renderer.sharedMaterials.Any(m => !m))
                throw new Exception("Missing material in " + c.name);
            var so = new SerializedObject(c); var p = so.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || !p.objectReferenceValue || EditorUtility.IsPersistent(p.objectReferenceValue)) continue;
                var target = p.objectReferenceValue as GameObject;
                if (p.objectReferenceValue is Component component) target = component.gameObject;
                if (target && target != root && !target.transform.IsChildOf(root.transform))
                    throw new Exception("External scene reference: " + c.GetType().Name + "." + p.propertyPath);
            }
        }
    }
}
