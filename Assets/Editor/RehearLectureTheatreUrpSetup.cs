using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Rendering.Universal;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RehearLectureTheatreUrpSetup
{
    const string ScenePath = "Assets/01_Scene/UniversityLectureTheatre.unity";
    const string MaterialFolder = "Assets/UniversityLectureTheatre/Mesh/Materials";
    const string RequestPath = "Temp/RehearLectureTheatreUrp.request";
    const string ReportPath = "Temp/RehearLectureTheatreUrp.txt";

    static RehearLectureTheatreUrpSetup() => EditorApplication.update += Poll;

    [MenuItem("Rehear/Lecture Theatre/Convert Materials To URP")]
    static void RequestApply() => File.WriteAllText(RequestPath, "apply");

    [MenuItem("Rehear/Lecture Theatre/Audit Materials")]
    static void RequestAudit() => File.WriteAllText(RequestPath, "audit");

    static void Poll()
    {
        if (!File.Exists(RequestPath) || AssetDatabase.IsAssetImportWorkerProcess() ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command != "apply" && command != "audit")
                throw new ArgumentException("Unknown command: " + command);
            Run(command == "apply");
        }
        catch (Exception exception)
        {
            File.WriteAllText(ReportPath, "FAILED\n" + exception);
            Debug.LogException(exception);
        }
    }

    static bool IsUrp(Material material) => material.shader &&
        material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal);

    static void ConvertUnlit(Material material, string oldShader)
    {
        Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
        Vector2 scale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
        Vector2 offset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
        Color color = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
        float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : 0.5f;
        material.shader = Shader.Find("Universal Render Pipeline/Unlit");
        material.shaderKeywords = Array.Empty<string>();
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Cutoff", cutoff);
        material.SetFloat("_Surface", oldShader == "Unlit/Transparent" ? 1 : 0);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_AlphaClip", oldShader == "Unlit/Transparent Cutout" ? 1 : 0);
    }

    static void Validate(Material material)
    {
        string guiName = material.shader.name == "Universal Render Pipeline/Unlit" ? "UnlitShader" : "LitShader";
        Type type = typeof(StandardUpgrader).Assembly.GetType("UnityEditor.Rendering.Universal.ShaderGUI." + guiName, true);
        ((ShaderGUI)Activator.CreateInstance(type, true)).ValidateMaterial(material);
        if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Unsupported or failed shader: " + material.name);
    }

    static void Run(bool apply)
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool preview = !scene.IsValid() || !scene.isLoaded;
        if (preview) scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        try
        {
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToArray();
            var materials = new HashSet<Material>(renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material));
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
                materials.Add(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)));
            var report = new StringBuilder();
            report.AppendLine("Scene: " + ScenePath);
            report.AppendLine("Renderers (including inactive): " + renderers.Length);
            report.AppendLine("Unique materials including asset pack: " + materials.Count);
            int converted = 0;
            foreach (Material material in materials.OrderBy(AssetDatabase.GetAssetPath))
            {
                string path = AssetDatabase.GetAssetPath(material);
                string oldShader = material.shader ? material.shader.name : "MISSING";
                report.AppendLine($"MATERIAL | {material.name} | {path} | {oldShader}");
                if (!apply || IsUrp(material)) continue;
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Material needs extraction before conversion: " + path);
                bool standard = oldShader == "Standard" || oldShader == "Standard (Specular setup)";
                bool unlit = oldShader == "Unlit/Color" || oldShader == "Unlit/Texture" ||
                    oldShader == "Unlit/Transparent" || oldShader == "Unlit/Transparent Cutout";
                if (!standard && !unlit)
                    throw new InvalidOperationException("Unsupported source shader: " + oldShader + " at " + path);
                string backup = "Temp/RehearLectureTheatreUrpBackup/" + AssetDatabase.AssetPathToGUID(path) + ".mat";
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (!File.Exists(backup)) File.Copy(path, backup);
                Undo.RecordObject(material, "Convert Lecture Theatre Material To URP");
                if (standard) new StandardUpgrader(oldShader).Upgrade(material, MaterialUpgrader.UpgradeFlags.None);
                else ConvertUnlit(material, oldShader);
                Validate(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                converted++;
                report.AppendLine($"CONVERTED | {material.name} | {material.shader.name} | keywords={string.Join(",", material.shaderKeywords)}");
            }
            int slots = 0;
            int missing = 0;
            int incompatible = 0;
            foreach (Renderer renderer in renderers)
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    slots++;
                    if (!material) { missing++; report.AppendLine("MISSING SLOT | " + renderer.name); }
                    else if (!IsUrp(material)) { incompatible++; report.AppendLine("NON URP SLOT | " + renderer.name + " | " + material.name); }
                }
            }
            report.AppendLine($"SUMMARY | converted={converted} | slots={slots} | missing={missing} | nonUrp={incompatible}");
            report.AppendLine(apply && missing == 0 && incompatible == 0 ? "PASS" : apply ? "INCOMPLETE" : "AUDIT COMPLETE");
            File.WriteAllText(ReportPath, report.ToString());
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
            Debug.Log("Lecture Theatre material audit: " + ReportPath);
        }
        finally { if (preview) EditorSceneManager.ClosePreviewScene(scene); }
    }
}