using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

internal static class RehearPretendardFontBake
{
    public const string Folder = "Assets/07_Fonts/PretendardTMP";
    private const string Report = "Temp/RehearPretendardFontBake-validation.txt";
    private static readonly string[] Weights = { "Medium", "Bold", "Regular", "Thin", "ExtraLight", "Light", "SemiBold", "ExtraBold", "Black" };
    private static int nextWeight;
    private static Action onComplete;
    private static bool running;

    public static TMP_FontAsset Font(string weight) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{Folder}/Pretendard-{weight} SDF.asset");

    [MenuItem("Rehear/Bake All Pretendard TMP Fonts And Apply")]
    public static void Begin() => Begin(null);

    public static void Begin(Action completed)
    {
        if (running || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/07_Fonts", "PretendardTMP");
        nextWeight = 0;
        onComplete = completed;
        running = true;
        File.WriteAllText(Report, $"started={DateTime.UtcNow:O}\nsize=32, padding=4, atlas=4096 Alpha8, static\n");
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (nextWeight < Weights.Length)
            {
                Bake(Weights[nextWeight++]);
                return;
            }
            EditorApplication.update -= Tick;
            ApplyToProjectUI();
            running = false;
            File.AppendAllText(Report, $"completed={DateTime.UtcNow:O}\n");
            onComplete?.Invoke();
        }
        catch (Exception e)
        {
            running = false;
            EditorApplication.update -= Tick;
            File.AppendAllText(Report, "FAILED\n" + e);
            Debug.LogException(e);
        }
    }

    private static void Bake(string weight)
    {
        var path = weight == "SemiBold" ? "Assets/07_Fonts/PRETENDARD-SEMIBOLD.OTF" : $"Assets/07_Fonts/Pretendard-{weight}.otf";
        var source = AssetDatabase.LoadAssetAtPath<Font>(path);
        if (!source) throw new InvalidOperationException("Missing font: " + path);
        var font = Font(weight);
        if (font && Enumerable.Range(0xAC00, 11172).All(c => font.HasCharacter(c)))
        {
            File.AppendAllText(Report, $"{weight}: existing complete Hangul atlas retained\n");
            return;
        }
        if (!font)
        {
            font = TMP_FontAsset.CreateFontAsset(source, 32, 4, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
            if (!font) throw new InvalidOperationException("Could not load font face: " + path);
            font.name = $"Pretendard-{weight} SDF";
            AssetDatabase.CreateAsset(font, $"{Folder}/{font.name}.asset");
        }
        else if (!font.material || font.atlasTextures.Length == 0 || !font.atlasTextures[0])
        {
            // Recover only an empty generated shell left by an interrupted first bake.
            if (font.characterTable.Count != 0)
                throw new InvalidOperationException("Non-empty font has missing dependencies; retained for inspection: " + weight);
            var fresh = TMP_FontAsset.CreateFontAsset(source, 32, 4, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
            EditorUtility.CopySerialized(fresh, font);
            Object.DestroyImmediate(fresh);
            font.name = $"Pretendard-{weight} SDF";
        }
        // Persist subasset identities before expensive generation, so a restart can resume safely.
        foreach (var atlas in font.atlasTextures.Where(t => t))
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        var fontSettings = new SerializedObject(font);
        fontSettings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        fontSettings.ApplyModifiedPropertiesWithoutUndo();
        var chars = new SortedSet<uint>();
        void Add(uint first, uint last)
        {
            for (uint c = first; c <= last; c++) if (!font.HasCharacter((int)c)) chars.Add(c);
        }
        // Complete modern Hangul, jamo, Latin, punctuation and font-supported UI symbols.
        Add(0x20, 0x024F); Add(0x1100, 0x11FF); Add(0x2000, 0x27FF);
        Add(0x3000, 0x318F); Add(0xAC00, 0xD7A3); Add(0xFF00, 0xFFEF);
        // Bounded batches avoid the expensive global glyph-packing search for a complete CJK font.
        var requested = chars.ToArray();
        for (int offset = 0; offset < requested.Length; offset += 256)
        {
            var batch = requested.Skip(offset).Take(256).ToArray();
            font.TryAddCharacters(batch, out var unsupported, false);
            if (offset % 2048 == 0)
                File.AppendAllText(Report, $"{weight}: packing {Math.Min(offset + 256, requested.Length)}/{requested.Length}\n");
        }
        var missingHangul = Enumerable.Range(0xAC00, 11172).Count(c => !font.HasCharacter(c));
        if (missingHangul != 0) throw new InvalidOperationException($"{weight}: {missingHangul} Hangul syllables missing.");
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var atlas in font.atlasTextures.Where(t => t))
        {
            atlas.filterMode = FilterMode.Bilinear;
            if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            EditorUtility.SetDirty(atlas);
        }
        if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(font.material);
        AssetDatabase.SaveAssets();
        File.AppendAllText(Report, $"{weight}: characters={font.characterTable.Count}, atlases={font.atlasTextureCount}, missingHangul=0\n");
        Debug.Log($"Rehear: Pretendard {weight} TMP baked ({font.characterTable.Count} characters, full Hangul).");
    }

    public static void ApplyToProjectUI()
    {
        var regular = Font("Regular");
        if (!regular) throw new InvalidOperationException("Bake Pretendard before assigning it.");
        var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
        var so = new SerializedObject(settings);
        so.FindProperty("m_defaultFontAsset").objectReferenceValue = regular;
        so.ApplyModifiedProperties();
        var active = EditorSceneManager.GetActiveScene();
        foreach (var path in Directory.GetFiles("Assets/01_Scene", "*.unity"))
        {
            var scenePath = path.Replace('\\', '/');
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            int count = 0;
            foreach (var root in scene.GetRootGameObjects()) count += Assign(root);
            if (count > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            File.AppendAllText(Report, $"{scenePath}: TMP labels={count}\n");
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
        if (AssetDatabase.IsValidFolder("Assets/03_Prefabs"))
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/03_Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try { if (Assign(root) > 0) PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        AssetDatabase.SaveAssets();
    }

    private static int Assign(GameObject root)
    {
        var labels = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (var label in labels)
        {
            var oldName = label.font ? label.font.name : "";
            var weight = Weights.OrderByDescending(w => w.Length).FirstOrDefault(w => oldName.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
            weight ??= (label.fontStyle & FontStyles.Bold) != 0 || (int)label.fontWeight >= 700 ? "Bold" : "Regular";
            var font = Font(weight);
            Undo.RecordObject(label, "Use Pretendard TMP");
            label.font = font;
            label.fontSharedMaterial = font.material;
            label.fontStyle &= ~FontStyles.Bold;
            label.fontWeight = FontWeight.Regular;
            EditorUtility.SetDirty(label);
            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        }
        return labels.Length;
    }
}
