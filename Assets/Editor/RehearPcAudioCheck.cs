using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR.Management;

// A short, isolated audition that measures the listener mix as well as lip sync.
[InitializeOnLoad]
internal static class RehearPcAudioCheck
{
    const string Key = "RehearPcAudioCheck";
    const string Report = "Temp/LocalSpeechCheck/pc_audio_report.txt";
    static RehearPcAudioCheck()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += Changed;
    }
    static void Poll()
    {
        const string desktopOutput = "Temp/RehearPcAudioOutput.request";
        if (File.Exists(desktopOutput) && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            File.Delete(desktopOutput);
            UseDesktopOutput();
        }
        const string restart = "Temp/RehearPcAudioRestart.request";
        if (File.Exists(restart) && !BuildPipeline.isBuildingPlayer && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            if (!EditorSceneManager.SaveOpenScenes()) return;
            File.Delete(restart);
            EditorApplication.OpenProject(Directory.GetCurrentDirectory());
            return;
        }
        const string request = "Temp/RehearPcAudio.request";
        if (!File.Exists(request) || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        var scene = EditorSceneManager.GetActiveScene();
        if (EditorSceneManager.sceneCount != 1 || string.IsNullOrEmpty(scene.path)) return;
        if (scene.isDirty && !EditorSceneManager.SaveScene(scene)) return;
        SessionState.SetString(Key + ".scene", scene.path);
        var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (xr)
        {
            SessionState.SetBool(Key + ".xr", xr.InitManagerOnStart);
            xr.InitManagerOnStart = false;
            EditorUtility.SetDirty(xr);
            AssetDatabase.SaveAssets();
        }
        SessionState.SetBool(Key, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }
    [MenuItem("Rehear/Audio/PC Speakers (without Quest Link)")]
    static void UseDesktopOutput() => SetEditorXr(false);

    [MenuItem("Rehear/Audio/Quest Link (headset audio and tracking)")]
    static void UseQuestLink() => SetEditorXr(true);

    static void SetEditorXr(bool enabled)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        // Play Mode uses Standalone XR settings even when Android is the build target.
        // Leave Android initialization enabled for the Quest APK.
        var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
        if (!xr) return;
        xr.InitManagerOnStart = enabled;
        EditorUtility.SetDirty(xr);
        AssetDatabase.SaveAssets();
        Debug.Log(enabled
            ? "[Rehear Audio] Quest Link enabled for Editor Play Mode. Restart Unity if its output is still attached to the previous device."
            : "[Rehear Audio] PC speakers selected; Editor XR auto-start disabled. Quest Android settings unchanged. Restart Unity if audio is still attached to Oculus.");
    }
    static void Changed(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode) Run();
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Key, false);
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr)
            {
                xr.InitManagerOnStart = SessionState.GetBool(Key + ".xr", true);
                EditorUtility.SetDirty(xr);
                AssetDatabase.SaveAssets();
            }
            EditorSceneManager.OpenScene(SessionState.GetString(Key + ".scene", ""));
        }
    }
    static async void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Report));
        var log = new StringBuilder();
        bool mute = EditorUtility.audioMasterMute;
        float volume = AudioListener.volume;
        bool paused = AudioListener.pause;
        bool background = Application.runInBackground;
        try
        {
            log.AppendLine($"Initial editorMute={mute}, listenerVolume={volume}, listenerPause={paused}, XR={XRGeneralSettings.Instance?.Manager?.activeLoader}");
            // Desktop audition uses the Windows output, without a headset redirect.
            var manager = XRGeneralSettings.Instance?.Manager;
            if (manager && manager.activeLoader)
            {
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            }
            log.AppendLine("Reset Windows output: " + AudioSettings.Reset(AudioSettings.GetConfiguration()));
            EditorUtility.audioMasterMute = false;
            Application.runInBackground = true;
            AudioListener.pause = false;
            AudioListener.volume = 1;
            var camera = new GameObject("PC audition camera", typeof(Camera), typeof(AudioListener));
            camera.transform.position = new Vector3(0, 1.4f, 0);
            var actor = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/03_Prefabs/Audience/Presentation/Aud_W_03.prefab"));
            actor.transform.SetPositionAndRotation(new Vector3(0, 0, 3), Quaternion.Euler(0, 180, 0));
            var light = new GameObject("Audition light", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40, -30, 0);
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(Path.GetFullPath("Builds/SpeechCheck/Aud_W_03-YuJin.wav")).AbsoluteUri, AudioType.WAV);
            var operation = request.SendWebRequest();
            while (!operation.isDone) await Task.Yield();
            if (request.result != UnityWebRequest.Result.Success) throw new Exception(request.error);
            var speaker = actor.GetComponent<AudienceQuestionSpeaker>();
            var clip = DownloadHandlerAudioClip.GetContent(request);
            if (!speaker.Play(clip)) throw new Exception("Could not start audience speech");
            speaker.Voice.loop = true; // Keep the Windows app mixer entry alive for inspection.
            float peak = 0;
            var samples = new float[1024];
            double deadline = EditorApplication.timeSinceStartup + 45;
            do
            {
                AudioListener.GetOutputData(samples, 0);
                foreach (float value in samples) peak = Mathf.Max(peak, Mathf.Abs(value));
                File.WriteAllText(Report, log + $"Playing YuJin: listener peak={peak:F6}, sourceVolume={speaker.Voice.volume}, sourceMute={speaker.Voice.mute}\n");
                await Task.Delay(50);
            } while (EditorApplication.isPlaying && speaker.IsSpeaking && EditorApplication.timeSinceStartup < deadline);
            log.AppendLine($"{(peak > .0001f ? "PASS" : "FAIL")} Unity listener output peak={peak:F6}. Physical speaker audibility requires listening.");
            speaker.StopSpeech();
            UnityEngine.Object.Destroy(clip);
        }
        catch (Exception e) { log.AppendLine("FAIL " + e); }
        finally
        {
            File.WriteAllText(Report, log.ToString());
            EditorUtility.audioMasterMute = mute;
            AudioListener.volume = volume;
            AudioListener.pause = paused;
            Application.runInBackground = background;
            EditorApplication.ExitPlaymode();
        }
    }
}
