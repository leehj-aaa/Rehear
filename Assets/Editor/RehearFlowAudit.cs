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
            if (mode == "voice-only")
            {
                CheckVoiceOnlyQuestions();
                return;
            }
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
            CheckPresentationControls();
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
    static void CheckVoiceOnlyQuestions()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var scene = EditorSceneManager.OpenPreviewScene("Assets/01_Scene/Scene_02_Presentation.unity");
        var root = new GameObject("Voice-only question regression");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            foreach (var sceneRoot in scene.GetRootGameObjects())
                foreach (var item in sceneRoot.GetComponentsInChildren<Transform>(true))
                    if (item.name == "Panel_QA" || item.name == "Text_QA")
                        throw new Exception("Retired question UI remains in presentation scene.");
            var qa = root.AddComponent<QuestionAnswerManager>();
            var panel = new GameObject("Legacy panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform);
            var text = new GameObject("Legacy question", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
            text.transform.SetParent(panel.transform);
            qa.qaPanel = panel;
            qa.questionText = text.GetComponent<TMPro.TMP_Text>();
            var buttonObject = new GameObject("Shared control", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(root.transform);
            var button = buttonObject.GetComponent<Button>();
            void AssertHidden(string phase)
            {
                if (panel.activeSelf || text.activeSelf || qa.questionText.text.Length != 0)
                    throw new Exception("Question text visible during " + phase);
            }
            qa.Prepare(button);
            qa.ShowGenerating();
            AssertHidden("generation");
            qa.ShowGenerationFailed("Expected regression-test failure");
            AssertHidden("retry");
            if (!button.interactable) throw new Exception("Generation retry unavailable.");
            qa.questionContents = new[] { "This content is for speech only." };
            typeof(QuestionAnswerManager).GetField("questionCount", flags).SetValue(qa, 1);
            typeof(QuestionAnswerManager).GetMethod("PlayCurrentQuestion", flags).Invoke(qa, null);
            AssertHidden("speech preparation/failure");
            if (!button.interactable) throw new Exception("Speech retry unavailable.");
            if (qa.questionContents[0] != "This content is for speech only.") throw new Exception("Speech content lost.");
            typeof(QuestionAnswerManager).GetMethod("FinishQuestionAudio", flags).Invoke(qa, null);
            AssertHidden("ready to answer");
            if (!button.interactable) throw new Exception("Answer button unavailable.");
            File.WriteAllText("Temp/RehearVoiceOnlyQuestions.txt", "PASS: authored question panel removed; no transcript during generation, retry, speech preparation or answer readiness; speech content and shared button preserved.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
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
    static void CheckPresentationControls()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var previousSession = RuntimeSessionData.Session;
        var previousPin = RuntimeSessionData.Pin;
        var preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Presentation controls regression");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        GameObject Child(string name, params Type[] components)
        {
            var child = new GameObject(name, components);
            child.transform.SetParent(root.transform, false);
            return child;
        }
        try
        {
            var session = new SessionData { page_1 = new Page1 {
                presentation_title = "응답 기반 질문 연구", duration_minutes = 30, qa_count = 2 },
                page_3 = new Page3 { audience_scale = 6 } };
            RuntimeSessionData.Load("test-only", session);
            var controller = root.AddComponent<PresentationController>();
            var qa = root.AddComponent<QuestionAnswerManager>();
            controller.qaManager = qa;
            controller.scriptPanel = Child("Script");
            var slide = Child("Slide");
            controller.startPresentationButton = Child("Start", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            controller.endPresentationButton = Child("End", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            var label = Child("End label", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TMP_Text>();
            label.transform.SetParent(controller.endPresentationButton.transform, false);
            var ready = Child("Session ready", typeof(RectTransform)).AddComponent<PresentationSessionReady>();
            ready.controller = controller;
            controller.sessionReady = ready;
            ready.presentationTitle = Child("Title", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TMP_Text>();
            ready.questionCount = Child("Question count", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI)).GetComponent<TMPro.TMP_Text>();
            ready.Populate();
            if (ready.presentationTitle.text != session.page_1.presentation_title || ready.questionCount.text != "2개")
                throw new Exception("Cached web title / question count not shown");
            typeof(PresentationController).GetMethod("Start", flags).Invoke(controller, null);
            controller.BeginPresentation();
            if ((bool)typeof(PresentationController).GetField("hasStarted", flags).GetValue(controller))
                throw new Exception("Presentation began before session confirmation");
            ready.Confirm();
            if ((bool)typeof(PresentationController).GetField("hasStarted", flags).GetValue(controller))
                throw new Exception("Confirmation started recording/presentation");
            controller.BeginPresentation();
            if (!controller.scriptPanel.activeSelf || !slide.activeSelf || controller.startPresentationButton.gameObject.activeSelf ||
                !controller.endPresentationButton.gameObject.activeSelf || label.text != "발표 끝내기")
                throw new Exception("Start must reveal script and preserve slide with end button");
            foreach (int count in new[] { 0, 2 })
            {
                session.page_1.qa_count = count;
                RuntimeSessionData.Load("test-only", session);
                typeof(PresentationController).GetMethod("FinishTimer", flags).Invoke(controller, null);
                if (!(bool)typeof(PresentationController).GetField("isRunning", flags).GetValue(controller) ||
                    (bool)typeof(PresentationController).GetField("isQAPhaseStarted", flags).GetValue(controller) ||
                    label.text != "발표 끝내기" || !controller.scriptPanel.activeSelf || !slide.activeSelf)
                    throw new Exception("Timer expiry prematurely ended presentation: QA=" + count);
            }
            controller.PauseGame();
            if (controller.endPresentationButton.interactable) throw new Exception("Paused end button accepts input");
            controller.ResumeGame();
            if (!controller.endPresentationButton.interactable) throw new Exception("End button did not resume");
            File.WriteAllText("Temp/RehearFlowPresentationTests.txt", "PASS cached title/count -> confirmation gate -> explicit start opens script/end button -> overtime with zero/two questions preserves presentation -> pause/resume controls; slides remain visible.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
            RuntimeSessionData.Load(previousPin, previousSession);
        }
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
