using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearTeethMaterialSetup
{
    const string Request = "Temp/RehearTeethMaterial.request";
    static RehearTeethMaterialSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "apply") Apply(); else Audit(); }
        catch (Exception ex) { File.WriteAllText("Temp/RehearTeethMaterial.txt", "FAIL\n" + ex); }
    }
    static bool ToothName(string name) => name.IndexOf("teeth", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("tooth", StringComparison.OrdinalIgnoreCase) >= 0;
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
    static bool EyeName(string name) => name.EndsWith("_EYE_L", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_EYE_R", StringComparison.OrdinalIgnoreCase);
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
        var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToArray();
        var targets = renderers.Where(r => PathOf(r.transform).StartsWith("Audience/Aud_") && (ToothName(r.name) || EyeName(r.name))).ToArray();
        if (targets.Length != 24 || targets.Count(r => ToothName(r.name)) != 12 || targets.Count(r => EyeName(r.name)) != 12)
            throw new Exception("Expected 12 teeth and 12 eyes across six audience members.");
        foreach (var actor in targets.GroupBy(r => r.transform.parent))
            if (actor.Count() != 4) throw new Exception("Incomplete actor: " + actor.Key.name);
        var unaffected = renderers.Except(targets).ToDictionary(r => r, r => r.sharedMaterials);
        var originals = targets.ToDictionary(r => r, r => r.sharedMaterials);
        foreach (var r in targets)
            if (originals[r].Length != (EyeName(r.name) ? 2 : 1) || originals[r].Any(m => !m)) throw new Exception("Unexpected material slots: " + r.name);
        const string folder = "Assets/04_Images/Materials/QuestFace";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/04_Images/Materials", "QuestFace");
        var copies = new Dictionary<Material, Material>();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Apply matte teeth and eyes");
        try
        {
            foreach (var r in targets)
            {
                bool eye = EyeName(r.name);
                var materials = originals[r].ToArray();
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!copies.TryGetValue(source, out var matte))
                    {
                        string name = r.transform.parent.name + "_" + source.name + (source.HasProperty("_Smoothness") ? "" : "_Converted") + "_Matte";
                        string path = folder + "/" + name + ".mat";
                        if (AssetDatabase.LoadAssetAtPath<Material>(path)) throw new Exception("Material already exists: " + path);
                        matte = new Material(source) { name = name };
                        if (!matte.HasProperty("_Smoothness"))
                        {
                            var texture = source.GetTexture("_MainTex");
                            var tint = source.GetColor("_Color");
                            var scale = source.GetTextureScale("_MainTex");
                            var offset = source.GetTextureOffset("_MainTex");
                            matte.shader = Shader.Find("Universal Render Pipeline/Lit");
                            matte.shaderKeywords = Array.Empty<string>();
                            matte.SetTexture("_BaseMap", texture);
                            matte.SetColor("_BaseColor", tint);
                            matte.SetTextureScale("_BaseMap", scale);
                            matte.SetTextureOffset("_BaseMap", offset);
                        }
                        matte.SetFloat("_Smoothness", 0);
                        if (eye) matte.SetFloat("_Metallic", 0);
                        AssetDatabase.CreateAsset(matte, path);
                        copies.Add(source, matte);
                    }
                    materials[i] = matte;
                    if (matte.GetFloat("_Smoothness") != 0 || (eye && matte.GetFloat("_Metallic") != 0)) throw new Exception("Matte verification failed.");
                    string sourceMap = source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (matte.GetTexture("_BaseMap") != source.GetTexture(sourceMap)) throw new Exception("Base texture changed.");
                    if (source.HasProperty("_BumpMap") && matte.GetTexture("_BumpMap") != source.GetTexture("_BumpMap")) throw new Exception("Normal texture changed.");
                    AssetDatabase.SaveAssetIfDirty(matte);
                }
                Undo.RecordObject(r, "Replace teeth/eye materials");
                r.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
                EditorUtility.SetDirty(r);
            }
            foreach (var item in unaffected)
                if (!item.Key.sharedMaterials.SequenceEqual(item.Value)) throw new Exception("Unrelated renderer changed.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            Audit();
            File.AppendAllText("Temp/RehearTeethMaterial.txt", "PASS\nteeth=12\neyes=12\nslots=36\nnewMaterials=" + copies.Count + "\nunrelatedMaterialsPreserved=true\nsaved=true\n");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
    static void Audit()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var report = new StringBuilder("scene=" + scene.path + "\n");
        foreach (var r in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true)))
        {
            if (!ToothName(r.name) && r.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) < 0 && !r.sharedMaterials.Any(m => m && ToothName(m.name))) continue;
            report.AppendLine("RENDERER " + PathOf(r.transform));
            int i = 0;
            foreach (var m in r.sharedMaterials)
            {
                report.AppendLine("slot=" + i++ + " material=" + (m ? m.name : "NULL") + " path=" + AssetDatabase.GetAssetPath(m));
                if (!m) continue;
                report.AppendLine("shader=" + m.shader.name + " smoothness=" + (m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness").ToString() : "NONE") + " metallic=" + (m.HasProperty("_Metallic") ? m.GetFloat("_Metallic").ToString() : "NONE"));
                foreach (var p in new[] { "_BaseMap", "_MainTex", "_BumpMap" })
                    if (m.HasProperty(p)) report.AppendLine(p + "=" + AssetDatabase.GetAssetPath(m.GetTexture(p)));
            }
        }
        File.WriteAllText("Temp/RehearTeethMaterial.txt", report.ToString());
    }
}
