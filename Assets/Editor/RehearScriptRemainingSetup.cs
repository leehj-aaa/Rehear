using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearScriptRemainingSetup
{
    const string Request = "Temp/RehearScriptRemaining.request";
    const string SoundPath = "Assets/08_Audio/Tutorial_PageBoundary.wav";
    static RehearScriptRemainingSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception ex) { File.WriteAllText("Temp/RehearScriptRemaining.txt", "FAIL\n" + ex); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
        var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
        var glass = view.steps[5].transform.Find("Practice glass");
        var description = glass.Find("Description").GetComponent<TextMeshProUGUI>();
        var font = RehearPretendardFontBake.Font("Medium");
        if (!font || !font.HasCharacters("0123456789회 남음완료!")) throw new Exception("Missing baked Pretendard glyphs.");
        var sound = EnsureBoundarySound();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add script remaining-count feedback");
        try
        {
            var existing = glass.Find("RemainingCount");
            TextMeshProUGUI remaining;
            if (existing) remaining = existing.GetComponent<TextMeshProUGUI>();
            else
            {
                var go = new GameObject("RemainingCount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                Undo.RegisterCreatedObjectUndo(go, "Create remaining count");
                go.transform.SetParent(glass, false);
                go.layer = glass.gameObject.layer;
                remaining = go.GetComponent<TextMeshProUGUI>();
            }
            Undo.RecordObjects(new UnityEngine.Object[] { description.rectTransform, remaining, remaining.rectTransform, manager }, "Configure remaining count");
            // Keep the text positions; reserve 46 px below the counter instead of 14 px.
            var glassRect = (RectTransform)glass;
            var glassGraphic = glass.GetComponent<UnityEngine.UI.Graphic>();
            Undo.RecordObjects(new UnityEngine.Object[] { glassRect, glassGraphic }, "Restore lower panel padding");
            glassRect.sizeDelta = new Vector2(glassRect.sizeDelta.x, 255);
            const string glassMaterialPath = "Assets/Settings/TutorialUI/Script Practice Glass.mat";
            var glassMaterial = AssetDatabase.LoadAssetAtPath<Material>(glassMaterialPath);
            if (!glassMaterial)
            {
                glassMaterial = new Material(glassGraphic.material) { name = "Script Practice Glass" };
                AssetDatabase.CreateAsset(glassMaterial, glassMaterialPath);
            }
            Undo.RecordObject(glassMaterial, "Resize script practice glass only");
            glassMaterial.SetVector("_PanelSize", new Vector4(glassRect.rect.width, glassRect.rect.height, 0, 0));
            glassGraphic.material = glassMaterial;
            EditorUtility.SetDirty(glassMaterial);
            EditorUtility.SetDirty(glassGraphic);
            AssetDatabase.SaveAssetIfDirty(glassMaterial);
            description.rectTransform.sizeDelta = new Vector2(description.rectTransform.sizeDelta.x, 42);
            TutorialFigmaView.Place(remaining.rectTransform, 25, 177, 754, 32);
            remaining.font = font;
            remaining.fontSharedMaterial = font.material;
            remaining.fontSize = 24;
            remaining.enableAutoSizing = false;
            remaining.fontStyle = FontStyles.Normal;
            remaining.fontWeight = FontWeight.Medium;
            remaining.alignment = TextAlignmentOptions.Center;
            remaining.color = new Color(0, .2f, 1, 1);
            remaining.raycastTarget = false;
            remaining.text = "3회 남음";
            if (!remaining.GetComponent<CurvedUI.CurvedUIVertexEffect>()) Undo.AddComponent<CurvedUI.CurvedUIVertexEffect>(remaining.gameObject);
            if (!remaining.GetComponent<CurvedUI.Core.Integrations.CurvedUITMP>()) Undo.AddComponent<CurvedUI.Core.Integrations.CurvedUITMP>(remaining.gameObject);
            var so = new SerializedObject(manager);
            so.FindProperty("scriptRemainingText").objectReferenceValue = remaining;
            so.FindProperty("scriptBoundarySound").objectReferenceValue = sound;
            so.ApplyModifiedProperties();
            remaining.text = so.FindProperty("requiredPracticeCount").intValue + "회 남음";
            remaining.ForceMeshUpdate(true, true);
            if (remaining.isTextOverflowing) throw new Exception("Remaining count overflows its layout.");
            EditorUtility.SetDirty(remaining);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
            Undo.CollapseUndoOperations(group);
            File.WriteAllText("Temp/RehearScriptRemaining.txt", "PASS\nlabel=RemainingCount\nfont=Pretendard Medium 24\ncolor=#0033FF\nremaining=3\npanelHeight=255\nlowerPadding=46\nscriptOnlyMaterial=true\nboundarySound=Tutorial_PageBoundary.wav\nsound=mono 22050Hz 0.18s\nsaved=true");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
    static AudioClip EnsureBoundarySound()
    {
        if (!File.Exists(SoundPath))
        {
            const int rate = 22050;
            int samples = (int)(rate * .18f);
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / rate;
                double local = t < .065 ? t : t >= .095 && t < .16 ? t - .095 : -1;
                double sample = local < 0 ? 0 : .25 * Math.Pow(Math.Sin(Math.PI * local / .065), 2) * Math.Sin(2 * Math.PI * (t < .065 ? 523.25 : 392) * local);
                writer.Write((short)(sample * short.MaxValue));
            }
            File.WriteAllBytes(SoundPath, stream.ToArray());
        }
        AssetDatabase.ImportAsset(SoundPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (AudioImporter)AssetImporter.GetAtPath(SoundPath);
        importer.forceToMono = true;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
    }
}
