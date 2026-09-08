using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearFlowAudit
{
    static TestRunnerApi runner;
    static RehearFlowAudit() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists("Temp/RehearFlowAudit.request")) return;
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) return;
        string mode = File.ReadAllText("Temp/RehearFlowAudit.request").Trim();
        File.Delete("Temp/RehearFlowAudit.request");
        File.WriteAllText("Temp/RehearFlowFailures.txt", "");
        try
        {
            if (mode == "pin")
            {
                EditorSceneManager.OpenScene("Assets/01_Scene/Scene_00.unity");
                File.WriteAllText("Temp/RehearPinReadback.request", "prepare");
                return;
            }
            if(mode == "repair-pads")
            {
                var original = EditorSceneManager.GetSceneManagerSetup();
                try {
                    foreach(var path in new[]{"Assets/01_Scene/Scene_00_5_Tutorial.unity", "Assets/01_Scene/Scene_01_Intro.unity", "Assets/01_Scene/Scene_02_Presentation.unity", "Assets/01_Scene/Scene_03_Feedback.unity"})
                    {
                        var scene = EditorSceneManager.OpenScene(path);
                        bool changed = false;
                        foreach(var root in scene.GetRootGameObjects())
                            foreach(var child in root.GetComponentsInChildren<Transform>(true))
                                if(child.name == "Pad" && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0)
                                { GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject); changed = true; }
                        if(changed) EditorSceneManager.SaveScene(scene);
                    }
                } finally { EditorSceneManager.RestoreSceneManagerSetup(original); }
                return;
            }
            CheckPause();
            foreach (var entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;
                var preview = EditorSceneManager.OpenPreviewScene(entry.path);
                try {
                    foreach(var root in preview.GetRootGameObjects())
                        foreach(var child in root.GetComponentsInChildren<Transform>(true))
                            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) != 0)
                                throw new Exception("Missing script: " + entry.path + ":" + child.name);
                } finally { EditorSceneManager.ClosePreviewScene(preview); }
            }
            File.WriteAllText("Temp/RehearFlowSceneTests.txt", "PASS all enabled build scenes have no missing scripts.");
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter { testMode = mode == "play" ? TestMode.PlayMode : TestMode.EditMode, assemblyNames = new[]{ mode == "play" ? "Rehear.Evc.Tests.PlayMode" : "Rehear.Evc.Tests.EditMode"} }));
        }
        catch(Exception e) { File.WriteAllText("Temp/RehearFlowAudit.txt", e.ToString()); }
    }
    static void CheckPause()
    {
        var go = new GameObject("QA pause regression", typeof(RectTransform), typeof(Image), typeof(Button));
        try
        {
            var qa = go.AddComponent<QuestionAnswerManager>();
            var button = go.GetComponent<Button>();
            qa.Prepare(button);
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var field = typeof(QuestionAnswerManager).GetField("state", flags);
            field.SetValue(qa, Enum.Parse(field.FieldType,"ReadyToAnswer"));
            qa.SetPaused(true); qa.OnActionButtonClick();
            if (button.interactable || field.GetValue(qa).ToString() != "ReadyToAnswer") throw new Exception("Paused QA advanced");
            qa.SetPaused(false); qa.OnActionButtonClick();
            if (field.GetValue(qa).ToString() != "Answering" || button.interactable) throw new Exception("Answer lock failed");
            typeof(QuestionAnswerManager).GetField("answerUnlockTime", flags).SetValue(qa, Time.unscaledTime-1);
            qa.SetPaused(true);
            typeof(QuestionAnswerManager).GetMethod("Update",flags).Invoke(qa,null);
            if(button.interactable) throw new Exception("Answer unlocked during pause");
            qa.SetPaused(false);
            if(!button.interactable) throw new Exception("Answer did not resume");
            File.WriteAllText("Temp/RehearFlowPauseTests.txt","PASS paused QA rejects clicks, answer cooldown, no unlock during pause, resume restores button.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    sealed class Results : ICallbacks
    {
        public void RunStarted(ITestAdaptor t) { File.WriteAllText("Temp/RehearFlowAudit.txt","RUNNING"); }
        public void TestStarted(ITestAdaptor t) { }
        public void TestFinished(ITestResultAdaptor r) { if(r.TestStatus == TestStatus.Failed) File.AppendAllText("Temp/RehearFlowFailures.txt",r.Name+"\n"+r.Message+"\n"+r.StackTrace+"\n"); }
        public void RunFinished(ITestResultAdaptor r) {
            TestRunnerApi.SaveResultToFile(r,"Temp/RehearFlowAudit.xml");
            File.WriteAllText("Temp/RehearFlowAudit.txt",$"{r.TestStatus}: passed={r.PassCount}, failed={r.FailCount}, skipped={r.SkipCount}");
        }
    }
}
