using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearBoundarySoundSetup
{
    internal const string SoundPath = "Assets/08_Audio/wrong.mp3";
    const string Request = "Temp/RehearBoundarySound.request";
    static RehearBoundarySoundSetup() { EditorApplication.update += Poll; }
    internal static AudioClip LoadSound()
    {
        var importer = AssetImporter.GetAtPath(SoundPath) as AudioImporter;
        if (!importer) throw new Exception("Missing wrong.mp3.");
        importer.forceToMono = true;
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundPath);
        if (!clip || clip.length <= 0) throw new Exception("Cannot decode wrong.mp3.");
        return clip;
    }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
            var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
            var clip = LoadSound();
            Undo.RecordObject(manager, "Use wrong sound at script boundaries");
            var so = new SerializedObject(manager);
            so.FindProperty("scriptBoundarySound").objectReferenceValue = clip;
            so.ApplyModifiedProperties();
            var source = so.FindProperty("practiceAudioSource").objectReferenceValue as AudioSource;
            if (!source) throw new Exception("Missing practice audio source.");
            Undo.RecordObject(source, "Configure VR UI feedback sound");
            source.spatialBlend = 0;
            source.dopplerLevel = 0;
            source.playOnAwake = false;
            source.loop = false;
            EditorUtility.SetDirty(source);
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
            File.WriteAllText("Temp/RehearBoundarySound.txt", "PASS\nclip=" + AssetDatabase.GetAssetPath(clip) + "\nlength=" + clip.length + "\nchannels=" + clip.channels + "\nspatialBlend=" + source.spatialBlend + "\nsourceVolume=" + source.volume + "\nsaved=true");
        }
        catch (Exception ex) { File.WriteAllText("Temp/RehearBoundarySound.txt", "FAIL\n" + ex); }
    }
}
