using System;
using System.IO;
using Rehear.Evc.Audio;
using Rehear.Evc.Config;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearLocalRecording
{
    const string EnabledKey = "Rehear.LocalRecording.Enabled";
    const string HttpKey = "Rehear.LocalRecording.PreviousHttp";
    const string Request = "Temp/RehearLocalRecording.request";
    static RehearLocalRecording()
    {
        Apply();
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode) Apply();
        };
        EditorApplication.quitting += Restore;
        EditorApplication.update += Poll;
    }

    [MenuItem("Rehear/PC Recording/Use Local AI Server")]
    static void Enable()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode before changing the recording server.");
        if (!SessionState.GetBool(EnabledKey, false))
            SessionState.SetInt(HttpKey, (int)PlayerSettings.insecureHttpOption);
        SessionState.SetBool(EnabledKey, true);
        PlayerSettings.insecureHttpOption = InsecureHttpOption.DevelopmentOnly;
        Apply();
        Debug.Log("[Local Recording] PC uses local Azure/Qwen server. Web reports are not saved. Start from Scene_00 manually.");
    }

    [MenuItem("Rehear/PC Recording/Restore Production Server")]
    static void Restore()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (SessionState.GetBool(EnabledKey, false))
            PlayerSettings.insecureHttpOption = (InsecureHttpOption)SessionState.GetInt(HttpKey, 0);
        SessionState.SetBool(EnabledKey, false);
        Apply();
    }

    static void Apply()
    {
        bool enabled = SessionState.GetBool(EnabledKey, false);
        EvcEnvironmentConfig.UseLocalRecordingServer = enabled;
        AzureSpeechConfig.EditorLoopbackBaseUrl = enabled ? "http://127.0.0.1:8093/odi/xreal_rehear/evc" : null;
    }

    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return;
        var command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try
        {
            if (command == "enable") Enable();
            else if (command == "disable") Restore();
            else throw new InvalidOperationException("Unknown recording command.");
            File.WriteAllText("Temp/RehearLocalRecording.txt", "PASS: " + command);
        }
        catch (Exception error) { File.WriteAllText("Temp/RehearLocalRecording.txt", "FAIL: " + error.Message); }
    }
}
