using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEditor.Rendering.Universal;
using UnityEditor.Rendering.Universal.ShaderGUI;

[InitializeOnLoad]
internal static class RehearHairMaterialSetup
{
    const string Request = "Temp/RehearHairMaterial.request";
    const string Report = "Temp/RehearHairMaterial-report.txt";
    static double nextCheck;
    const string Folder = "Assets/04_Images/Materials/QuestHair";
    static RehearHairMaterialSetup()
    {
        EditorApplication.update += CheckRequest;
        File.WriteAllText("Temp/RehearHairMaterial-ready.txt", "v3");
    }
    static void CheckRequest()
    {
        if (EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        var command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "audit") Audit(); else if (command == "apply") Apply(); else if (command == "refine") Refine(); }
        catch (Exception e) { File.WriteAllText(Report, e.ToString()); Debug.LogException(e); }
    }
    static bool IsHair(Material material) => material && !material.name.StartsWith("Hair_tie", StringComparison.OrdinalIgnoreCase) &&
        (AssetDatabase.GetAssetPath(material).StartsWith(Folder + "/") ||
         material.name.StartsWith("Hair", StringComparison.OrdinalIgnoreCase) ||
         material.name.StartsWith("Scalp", StringComparison.OrdinalIgnoreCase) ||
         material.name.StartsWith("Beard", StringComparison.OrdinalIgnoreCase));
    static string PathOf(Transform transform) => transform.parent ? PathOf(transform.parent) + "/" + transform.name : transform.name;
    static float Value(Material material, string property) => material.HasProperty(property) ? material.GetFloat(property) : -1;
    static Texture2D PackedHairTexture()
    {
        const string path = Folder + "/Hair_ColorOpacity_2K.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing) return existing;
        var color = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        var opacity = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D packed = null;
        try
        {
            if (!color.LoadImage(File.ReadAllBytes("Assets/Textures/Hair_401_BaseColor_4K2.png")) ||
                !opacity.LoadImage(File.ReadAllBytes("Assets/Textures/Hair_401_Opacity_4K.png")))
                throw new InvalidOperationException("Hair source textures could not be read.");
            if (color.width != 4096 || color.height != 4096 || opacity.width != 4096 || opacity.height != 4096)
                throw new InvalidOperationException("Expected matching 4K hair color and opacity maps.");
            var rgb = color.GetPixels32();
            var alpha = opacity.GetPixels32();
            var pixels = new Color32[2048 * 2048];
            // Box-filter the matching source maps together; opacity belongs in base-map alpha.
            for (int y = 0; y < 2048; y++)
            for (int x = 0; x < 2048; x++)
            {
                int i = y * 2 * 4096 + x * 2;
                pixels[y * 2048 + x] = new Color32(
                    (byte)((rgb[i].r + rgb[i+1].r + rgb[i+4096].r + rgb[i+4097].r) / 4),
                    (byte)((rgb[i].g + rgb[i+1].g + rgb[i+4096].g + rgb[i+4097].g) / 4),
                    (byte)((rgb[i].b + rgb[i+1].b + rgb[i+4096].b + rgb[i+4097].b) / 4),
                    (byte)((alpha[i].r + alpha[i+1].r + alpha[i+4096].r + alpha[i+4097].r) / 4));
            }
            packed = new Texture2D(2048, 2048, TextureFormat.RGBA32, false);
            packed.SetPixels32(pixels);
            packed.Apply();
            File.WriteAllBytes(path, packed.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(color);
            UnityEngine.Object.DestroyImmediate(opacity);
            if (packed) UnityEngine.Object.DestroyImmediate(packed);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.2f;
        importer.anisoLevel = 2;
        importer.maxTextureSize = 2048;
        importer.isReadable = false;
        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
        {
            name = "Android", overridden = true, maxTextureSize = 2048,
            format = TextureImporterFormat.ASTC_6x6, compressionQuality = 70
        });
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    [MenuItem("Rehear/Apply Natural Quest Hair To Tutorial")]
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Open the tutorial in Edit mode with baking stopped.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/04_Images/Materials", "QuestHair");
        var texture = PackedHairTexture();
        var mapped = new System.Collections.Generic.Dictionary<Material, Material>();
        int slots = 0;
        Undo.SetCurrentGroupName("Natural Quest hair materials");
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!IsHair(source)) continue;
                if (!mapped.TryGetValue(source, out var material))
                {
                    var sourcePath = AssetDatabase.GetAssetPath(source);
                    string name = sourcePath.StartsWith(Folder + "/") ? source.name :
                        Path.GetFileNameWithoutExtension(sourcePath) + "_" + source.name + "_Natural";
                    string path = Folder + "/" + name + ".mat";
                    material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    bool scalp = source.name.IndexOf("Scalp", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool cards = sourcePath.EndsWith("/Hair.mat") || source.name == "Hair_Hair_Natural";
                    if (!material)
                    {
                        material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                        material.SetColor("_BaseColor", source.GetColor("_BaseColor"));
                        material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                        material.SetTexture("_BaseMap", cards ? texture : source.GetTexture("_BaseMap"));
                        AssetDatabase.CreateAsset(material, path);
                    }
                    Undo.RecordObject(material, "Natural hair surface");
                    material.SetFloat("_WorkflowMode", 1);
                    material.SetFloat("_Surface", scalp ? 1 : 0);
                    material.SetFloat("_BlendModePreserveSpecular", 0);
                    material.SetFloat("_AlphaClip", scalp || cards ? 1 : 0);
                    material.SetFloat("_Cutoff", scalp ? 0.057f : 0.2f);
                    material.SetFloat("_AlphaToMask", cards ? 1 : 0);
                    material.SetFloat("_Cull", scalp || cards ? 0 : 2);
                    material.SetFloat("_Metallic", 0);
                    material.SetFloat("_Smoothness", scalp ? 0.08f : 0.18f);
                    material.SetFloat("_BumpScale", scalp ? 0.2f : 0.3f);
                    material.SetFloat("_EnvironmentReflections", 0);
                    material.SetFloat("_SpecularHighlights", 1);
                    material.SetFloat("_ReceiveShadows", 1);
                    material.enableInstancing = true;
                    material.doubleSidedGI = true;
                    BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
                    EditorUtility.SetDirty(material);
                    mapped.Add(source, material);
                }
                materials[i] = material;
                slots++;
                changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(renderer, "Assign natural hair");
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Tutorial scene save failed.");
        Audit();
        File.AppendAllText(Report, $"saved=true materials={mapped.Count} slots={slots}\n");
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
    static void Refine()
    {
        // Preserve the surface topology used by the source models: most are solid geometry,
        // while the existing Hair material uses cutout cards and Scalp is a blended decal.
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            bool scalp = material.name.StartsWith("Scalp_");
            bool cards = material.name == "Hair_Hair_Natural";
            Undo.RecordObject(material, "Match hair geometry surface");
            material.SetFloat("_Surface", scalp ? 1 : 0);
            material.SetFloat("_BlendModePreserveSpecular", 0);
            material.SetFloat("_AlphaClip", scalp || cards ? 1 : 0);
            material.SetFloat("_AlphaToMask", cards ? 1 : 0);
            material.SetFloat("_Cull", scalp || cards ? 0 : 2);
            if (!scalp && !cards)
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Hair_401_BaseColor_4K2.png"));
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
        Audit();
        File.AppendAllText(Report, "refined=true\n");
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
    [MenuItem("Rehear/Audit Hair Materials")]
    static void Audit()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var report = new StringBuilder("scene=" + scene.path + "\n");
        foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
        foreach (var material in renderer.sharedMaterials.Where(IsHair))
        {
            report.AppendLine($"RENDERER={PathOf(renderer.transform)} active={renderer.gameObject.activeInHierarchy}");
            report.AppendLine($"MATERIAL={material.name} path={AssetDatabase.GetAssetPath(material)} shader={material.shader.name}");
            foreach (var p in new[] { "_Surface", "_Cutoff", "_AlphaClip", "_ZWrite", "_Cull", "_BumpScale", "_Smoothness", "_Metallic", "_SpecularHighlights", "_EnvironmentReflections" })
                report.Append($"{p}={Value(material,p)} ");
            report.AppendLine();
            foreach (var p in new[] { "_BaseMap", "_BumpMap" })
                if (material.HasProperty(p)) report.AppendLine($"{p}={AssetDatabase.GetAssetPath(material.GetTexture(p))}");
        }
        File.WriteAllText(Report, report.ToString());
        Debug.Log("Rehear: hair material audit completed.");
    }
}
