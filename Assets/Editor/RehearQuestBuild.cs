using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

[InitializeOnLoad]
internal static class RehearQuestBuild
{
    const string Request = "Temp/RehearQuestBuild.request";
    const string Report = "Builds/QuestCheck/build-report.txt";
    static RehearQuestBuild() { EditorApplication.update += Poll; }

    [MenuItem("Rehear/Build Quest Single Pass APK")]
    static void RequestBuild() { File.WriteAllText(Request, "build"); }

    static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() || !File.Exists(Request) ||
            EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Directory.CreateDirectory("Builds/QuestCheck");
        if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
        {
            File.WriteAllText(Report, "Waiting for Edit mode and lighting bake completion.");
            return;
        }
        try
        {
            if (!EditorSceneManager.SaveOpenScenes()) throw new Exception("Could not save open scenes.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                File.WriteAllText(Report, "Switching active build target to Android.");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new Exception("Android target switch failed.");
                return;
            }
            File.Delete(Request);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (!xr) throw new Exception("Android OpenXR settings missing.");
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            var touchPlus = xr.GetFeature<MetaQuestTouchPlusControllerProfile>();
            if (!touchPlus) throw new Exception("Quest Touch Plus controller profile missing.");
            touchPlus.enabled = true;
            EditorUtility.SetDirty(touchPlus);
            EditorUtility.SetDirty(xr);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            AssetDatabase.SaveAssets();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new Exception("No enabled build scenes.");
            File.WriteAllText(Report, "Building current project: " + DateTime.Now.ToString("O") +
                "\nSinglePassInstanced / ARM64 / IL2CPP / Development\n" + string.Join("\n", scenes) + "\n");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes, target = BuildTarget.Android,
                locationPathName = "Builds/QuestCheck/Rehear-SinglePass.apk",
                options = BuildOptions.Development,
                extraScriptingDefines = new[] { "REHEAR_QUEST_DIAGNOSTICS" }
            });
            File.AppendAllText(Report, $"\n{result.summary.result}: errors={result.summary.totalErrors}, bytes={result.summary.totalSize}, duration={result.summary.totalTime}\n");
            foreach (var step in result.steps)
                foreach (var message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        File.AppendAllText(Report, message.content + "\n");
        }
        catch (Exception e)
        {
            if (File.Exists(Request)) File.Delete(Request);
            File.AppendAllText(Report, "\nFAILED: " + e);
            Debug.LogException(e);
        }
    }
}
