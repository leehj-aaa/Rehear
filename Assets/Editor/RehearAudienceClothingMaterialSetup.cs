using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearAudienceClothingMaterialSetup
{
    const string Request = "Temp/RehearAudienceClothingMaterial.request";
    const string Report = "Temp/RehearAudienceClothingMaterial.txt";
    const string Ready = "Temp/RehearAudienceClothingMaterial.ready";
    const string PrefabFolder = "Assets/03_Prefabs/Audience/Presentation";
    const string MaterialFolder = "Assets/04_Images/Materials/AudienceClothingDoubleSided";

    static readonly string[] AudiencePrefabs =
    {
        "Aud_M_01.prefab", "Aud_M_02.prefab", "Aud_M_03.prefab",
        "Aud_W_01.prefab", "Aud_W_02.prefab", "Aud_W_03.prefab"
    };

    static RehearAudienceClothingMaterialSetup()
    {
        File.WriteAllText(Ready, "v5");
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!File.Exists(Request) || AssetDatabase.IsAssetImportWorkerProcess() ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;

        try
        {
            string command = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            if (command == "audit") Audit();
            else if (command == "apply") Apply();
            else throw new InvalidOperationException("Unknown command: " + command);
        }
        catch (Exception e)
        {
            File.WriteAllText(Report, "FAILED\n" + e);
            Debug.LogException(e);
        }
    }

    static bool IsClothingRenderer(Renderer renderer)
    {
        string name = renderer.name.ToLowerInvariant();
        if (name.Contains("hair")) return false;
        string[] terms = { "clothing", "jacket", "pants", "cotton", "shirt", "dress", "skirt", "coat", "sweater", "shoe", "sneaker", "heel", "pump", "monk", "slip-on", "sock", "vest" };
        return terms.Any(name.Contains);
    }

    static string HierarchyPath(Transform transform)
        => transform.parent ? HierarchyPath(transform.parent) + "/" + transform.name : transform.name;

    static string Value(Material material, string property)
        => material.HasProperty(property) ? material.GetFloat(property).ToString("0.###") : "n/a";

    static void Audit()
    {
        var report = new StringBuilder();
        foreach (string prefabName in AudiencePrefabs)
        {
            string path = PrefabFolder + "/" + prefabName;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                report.AppendLine("PREFAB " + prefabName);
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    string kind = IsClothingRenderer(renderer) ? "CLOTHING" : "OTHER";
                    foreach (Material material in renderer.sharedMaterials.Where(m => m))
                        report.AppendLine($"{kind} | {HierarchyPath(renderer.transform)} | {material.name} | {AssetDatabase.GetAssetPath(material)} | shader={material.shader.name} | _Cull={Value(material, "_Cull")} | doubleSidedGI={material.doubleSidedGI}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        File.WriteAllText(Report, report.ToString());
    }

    static void Apply()
    {
        EnsureMaterialFolder();
        var mapped = new System.Collections.Generic.Dictionary<string, Material>();
        var report = new StringBuilder();
        foreach (string prefabName in AudiencePrefabs)
        {
            string path = PrefabFolder + "/" + prefabName;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool prefabChanged = false;
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true).Where(IsClothingRenderer))
                {
                    Material[] materials = renderer.sharedMaterials;
                    bool rendererChanged = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        Material source = materials[i];
                        if (!source) continue;
                        if (IsBodyMaterial(source))
                        {
                            Material original = RestoreOriginalBodyMaterial(source);
                            if (original != source)
                            {
                                materials[i] = original;
                                rendererChanged = true;
                                report.AppendLine($"RESTORED BODY | {HierarchyPath(renderer.transform)} | {source.name} -> {AssetDatabase.GetAssetPath(original)}");
                            }
                            continue;
                        }
                        materials[i] = DoubleSidedCopy(source, mapped, report);
                        rendererChanged = true;
                    }
                    if (!rendererChanged) continue;
                    renderer.sharedMaterials = materials;
                    rendererChanged = prefabChanged = true;
                }
                if (prefabChanged && !PrefabUtility.SaveAsPrefabAsset(root, path))
                    throw new IOException("Could not save audience prefab: " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        report.Insert(0, $"materials={mapped.Count}\n");
        File.WriteAllText(Report, report.ToString());
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearQuestBuild.request", "build");
    }

    static bool IsBodyMaterial(Material material)
    {
        string value = (material.name + " " + AssetDatabase.GetAssetPath(material)).ToLowerInvariant();
        string[] terms = { "skin", "hair", "scalp", "eyelash", "eye_", "teeth", "tongue" };
        return terms.Any(value.Contains);
    }

    static Material RestoreOriginalBodyMaterial(Material material)
    {
        string path = AssetDatabase.GetAssetPath(material);
        if (!path.StartsWith(MaterialFolder + "/", StringComparison.Ordinal)) return material;

        string sourceName = material.name;
        const string suffix = "_Audience_Both";
        if (sourceName.EndsWith(suffix, StringComparison.Ordinal))
            sourceName = sourceName.Substring(0, sourceName.Length - suffix.Length);

        Material best = null;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
        {
            string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
            if (candidatePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal)) continue;
            Material candidate = AssetDatabase.LoadAssetAtPath<Material>(candidatePath);
            if (!candidate) continue;
            bool matches = sourceName.Equals(candidate.name, StringComparison.OrdinalIgnoreCase) ||
                           sourceName.StartsWith(candidate.name + "_", StringComparison.OrdinalIgnoreCase);
            if (matches && (!best || candidate.name.Length > best.name.Length)) best = candidate;
        }
        if (best) return best;
        throw new InvalidOperationException($"Could not restore original body material for {material.name} at {path}");
    }

    static Material DoubleSidedCopy(Material source, System.Collections.Generic.Dictionary<string, Material> mapped, StringBuilder report)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (sourcePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal))
        {
            if (!source.HasProperty("_Cull"))
                throw new InvalidOperationException($"Generated clothing material has no Render Face/_Cull property: {sourcePath}");
            source.SetFloat("_Cull", 0f);
            if (source.HasProperty("_Smoothness")) source.SetFloat("_Smoothness", 0f);
            if (source.HasProperty("_Glossiness")) source.SetFloat("_Glossiness", 0f);
            source.doubleSidedGI = true;
            EditorUtility.SetDirty(source);
            report.AppendLine($"REUSED | {sourcePath} | _Cull=0 | smoothness=0 | doubleSidedGI=True");
            return source;
        }

        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
        string key = guid + ":" + localId;
        if (mapped.TryGetValue(key, out Material result)) return result;

        string fileName = Sanitize(Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source)) + "_" + source.name + "_" + localId + "_Both") + ".mat";
        string path = MaterialFolder + "/" + fileName;
        result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!result)
        {
            result = new Material(source) { name = source.name + "_Audience_Both" };
            AssetDatabase.CreateAsset(result, path);
        }
        else result.CopyPropertiesFromMaterial(source);

        if (!result.HasProperty("_Cull"))
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) throw new InvalidOperationException("Universal Render Pipeline/Lit shader was not found.");
            result.shader = lit;
            if (result.GetTexture("_BumpMap")) result.EnableKeyword("_NORMALMAP");
            report.AppendLine($"CONVERTED | source={source.name} | from={source.shader.name} | to={lit.name}");
        }
        result.SetFloat("_Cull", 0f);
        if (result.HasProperty("_Smoothness")) result.SetFloat("_Smoothness", 0f);
        if (result.HasProperty("_Glossiness")) result.SetFloat("_Glossiness", 0f);
        result.doubleSidedGI = true;
        EditorUtility.SetDirty(result);
        mapped.Add(key, result);
        report.AppendLine($"CHANGED | source={source.name} | sourcePath={AssetDatabase.GetAssetPath(source)} | copy={path} | _Cull={result.GetFloat("_Cull")} | smoothness=0 | doubleSidedGI={result.doubleSidedGI}");
        return result;
    }

    static void EnsureMaterialFolder()
    {
        const string parent = "Assets/04_Images/Materials";
        if (!AssetDatabase.IsValidFolder(parent)) AssetDatabase.CreateFolder("Assets/04_Images", "Materials");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder(parent, "AudienceClothingDoubleSided");
    }

    static string Sanitize(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace(' ', '_').Replace('-', '_');
    }
}
