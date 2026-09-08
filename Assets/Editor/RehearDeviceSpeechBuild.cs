using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearDeviceSpeechBuild
{
    const string Request="Temp/RehearDeviceSpeechBuild.request";
    const string Report="Temp/LocalSpeechCheck/build_report.txt";
    const string TestScene="Assets/RehearDeviceSpeechDiagnostic.unity";
    static RehearDeviceSpeechBuild(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(!File.Exists(Request)||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
        {
            File.WriteAllText(Report,"Switching to Android ARM64…");
            if(!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android))
            {File.Delete(Request);File.AppendAllText(Report,"\nFAIL switch target");}
            return;
        }
        File.Delete(Request);
        string originalScene=EditorSceneManager.GetActiveScene().path;
        string originalId=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        string originalName=PlayerSettings.productName;
        var originalHttp=PlayerSettings.insecureHttpOption;
        bool originalBundle=EditorUserBuildSettings.buildAppBundle;
        try
        {
            if(string.IsNullOrEmpty(originalScene))throw new Exception("Save the current scene first");
            if(EditorSceneManager.GetActiveScene().isDirty&&!EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()))throw new Exception("Scene save failed");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener)).GetComponent<Camera>();
            camera.tag="MainCamera";camera.transform.position=new Vector3(0,1.4f,0);
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.04f,.06f,.1f);
            var light=new GameObject("Diagnostic light",typeof(Light)).GetComponent<Light>();light.type=LightType.Directional;light.intensity=1.5f;light.transform.rotation=Quaternion.Euler(40,-30,0);
            var check=new GameObject("Speech diagnostic",typeof(RehearDeviceSpeechCheck)).GetComponent<RehearDeviceSpeechCheck>();
            check.audiencePrefabs=Directory.GetFiles("Assets/03_Prefabs/Audience/Presentation","Aud_*.prefab").OrderBy(p=>p).Select(p=>AssetDatabase.LoadAssetAtPath<GameObject>(p.Replace('\\','/'))).ToArray();
            EditorSceneManager.SaveScene(scene,TestScene);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.boracles.rehear.speechcheck");
            PlayerSettings.productName="ReHear Speech Check";
            PlayerSettings.insecureHttpOption=InsecureHttpOption.DevelopmentOnly;
            EditorUserBuildSettings.buildAppBundle=false;
            Directory.CreateDirectory("Builds/SpeechCheck");
            File.WriteAllText(Report,"Building isolated diagnostic APK; Azure keys are not included.\n");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{TestScene},locationPathName="Builds/SpeechCheck/Rehear-SpeechCheck.apk",target=BuildTarget.Android,options=BuildOptions.Development,extraScriptingDefines=new[]{"REHEAR_SPEECH_SMOKE"}});
            File.AppendAllText(Report,$"{result.summary.result}: errors={result.summary.totalErrors}, size={result.summary.totalSize}, time={result.summary.totalTime}\n");
            foreach(var step in result.steps)foreach(var message in step.messages)
                if(message.type==LogType.Error||message.type==LogType.Exception)File.AppendAllText(Report,message.content+"\n");
        }
        catch(Exception e){File.AppendAllText(Report,"FAIL "+e);}
        finally
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,originalId);
            PlayerSettings.productName=originalName;
            PlayerSettings.insecureHttpOption=originalHttp;
            EditorUserBuildSettings.buildAppBundle=originalBundle;
            if(!string.IsNullOrEmpty(originalScene))EditorSceneManager.OpenScene(originalScene);
            AssetDatabase.DeleteAsset(TestScene);
            AssetDatabase.SaveAssets();
        }
    }
}
