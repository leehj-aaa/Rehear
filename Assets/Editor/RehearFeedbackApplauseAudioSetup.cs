using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RehearFeedbackApplauseAudioSetup
{
    const string SourcePath = "Assets/08_Audio/applause_loop.wav";
    static RehearFeedbackApplauseAudioSetup()
    {
        EditorApplication.update += Poll;
        const string pending = "Temp/RehearFeedbackApplauseAudio.pending";
        if (File.Exists(pending))
        {
            File.Delete(pending);
            File.WriteAllText("Temp/RehearFeedbackApplauseAudio.request", "setup");
        }
    }
    static void Poll()
    {
        const string request = "Temp/RehearFeedbackApplauseAudio.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Run(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearFeedbackApplauseAudio.txt", e.ToString()); }
    }

    static void Run()
    {
        typeof(RehearFeedbackPreview).GetMethod("Stop", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        var importer = (AudioImporter)AssetImporter.GetAtPath(SourcePath);
        if (!importer) throw new Exception("The user-supplied applause loop is missing.");
        // Preserve the supplied seamless loop exactly; decode before playback.
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings; importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourcePath);
        if (!clip || !clip.LoadAudioData()) throw new Exception("Applause source could not be decoded.");
        var data = new float[clip.samples * clip.channels];
        if (!clip.GetData(data, 0)) throw new Exception("Applause PCM data unavailable.");
        float peak = data.Max(v => Mathf.Abs(v));
        if (peak < .00001f) throw new Exception("Applause source is silent.");
        var scene = SceneManager.GetSceneByPath("Assets/01_Scene/Scene_03_Feedback.unity");
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/01_Scene/Scene_03_Feedback.unity", OpenSceneMode.Additive);
        var owner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<FeedbackAudienceApplause>(true)).Single();
        var serialized = new SerializedObject(owner);
        serialized.FindProperty("applauseLoop").objectReferenceValue = clip;
        serialized.FindProperty("applauseVolume").floatValue = .09f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        float boundary = 0;
        for (int c = 0; c < clip.channels; c++) boundary = Mathf.Max(boundary, Mathf.Abs(data[data.Length - clip.channels + c] - data[c]));
        File.WriteAllText("Temp/RehearFeedbackApplauseAudio.txt", $"PASS user loop={clip.length:F3}s; samples={clip.samples}; channels={clip.channels}; peak={peak:F4}; boundary delta={boundary:F5}; volume=.09; fade-in=.8s; PCM preloaded; scene reference saved. Source audio unchanged.\n");
        File.WriteAllText("Temp/RehearFeedbackPreview.request", "show");
    }
}
