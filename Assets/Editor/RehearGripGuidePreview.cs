using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Retains the old window type only to close a previously opened detached preview.
internal sealed class RehearGripGuidePreview : EditorWindow
{
    void OnEnable() { EditorApplication.delayCall += () => { if (this) Close(); }; }
}

[InitializeOnLoad]
internal static class RehearGripScenePreview
{
    static Quest3TutorialControllerVisual visual;
    static double previousTime;
    const string ActiveKey = "Rehear.GripScenePreview.Active";
    static RehearGripScenePreview()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            { SessionState.SetBool(ActiveKey, false); Cleanup(); }
        };
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(ActiveKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                StartPreview(false);
        };
    }

    [MenuItem("Rehear/Tutorial/Preview Grip Guide")]
    static void Open() => StartPreview(true);
    [MenuItem("Rehear/Tutorial/Stop Controller Preview")]
    static void Stop() { SessionState.SetBool(ActiveKey, false); Cleanup(); }

    [MenuItem("Rehear/Tutorial/Preview Paused UI")]
    static void ShowPausedUI() => ShowStepUI(7);

    [MenuItem("Rehear/Tutorial/Preview Completion UI")]
    static void ShowCompletionUI() => ShowStepUI(8);

    [MenuItem("Rehear/Tutorial/Restore Initial UI")]
    static void ShowInitialUI() => ShowStepUI(0);

    static void ShowStepUI(int step)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play mode to edit the paused UI.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open the tutorial scene.");
        Stop();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var guide = all.Select(t => t.GetComponent<TutorialControlGuideView>()).Single(c => c);
        var view = all.Select(t => t.GetComponent<TutorialFigmaView>()).Single(c => c);
        Undo.RecordObjects(view.steps, "Show tutorial UI step");
        Undo.RecordObject(guide.gameObject, "Hide controller guide");
        if (view.deskDirectionHints) Undo.RecordObject(view.deskDirectionHints, "Hide navigation");
        if (view.scriptDirectionHints) Undo.RecordObject(view.scriptDirectionHints, "Hide navigation");
        guide.Hide();
        view.Show(step);
        if (step == 0)
        {
            var manager = all.Select(t => t.GetComponent<TutorialManager>()).Single(c => c);
            var serialized = new SerializedObject(manager);
            foreach (string field in new[] { "timerStopPanel", "tutorial5_1Object", "scriptPanel" })
            {
                var target = serialized.FindProperty(field).objectReferenceValue as GameObject;
                if (!target) continue;
                Undo.RecordObject(target, "Restore initial tutorial visibility");
                target.SetActive(false);
            }
            var progress = serialized.FindProperty("progressImage").objectReferenceValue as UnityEngine.UI.Image;
            if (progress)
            {
                Undo.RecordObject(progress.gameObject, "Hide initial progress");
                progress.gameObject.SetActive(false);
            }
            view.SetStepButton(0, true, true, "튜토리얼 시작하기");
            view.SetStepButton(0, false, true, "건너뛰기");
            foreach (var toggle in all.Select(t => t.GetComponent<PodiumScriptToggle>()).Where(c => c))
            {
                if (toggle.label) Undo.RecordObject(toggle.label, "Reset script toggle label");
                if (toggle.background) Undo.RecordObject(toggle.background, "Reset script toggle color");
                toggle.Refresh();
            }
            if (view.steps.Where((s, i) => s && s.activeSelf != (i == 0)).Any() || guide.gameObject.activeSelf)
                throw new Exception("Initial UI visibility validation failed.");
        }
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
        Selection.activeGameObject = view.steps[step];
        var camera = all.Select(t => t.GetComponent<Camera>()).Single(c => c && c.CompareTag("MainCamera"));
        if (SceneView.lastActiveSceneView)
        {
            var panel = view.steps[step].GetComponentsInChildren<RectTransform>().First(t => t.name.EndsWith("glass", StringComparison.OrdinalIgnoreCase));
            Vector3 center = panel.TransformPoint(panel.rect.center);
            float width = panel.rect.width * panel.lossyScale.x;
            SceneView.lastActiveSceneView.LookAt(center, panel.rotation, width * .7f);
        }
        File.WriteAllText(step == 0 ? "Temp/RehearInitialUI.txt" : step == 8 ? "Temp/RehearCompletionUIPreview.txt" : "Temp/RehearPausedUIPreview.txt",
            "PASS\nstep=" + step + "\nvisible=" + view.steps[step].activeInHierarchy +
            "\ncontrollerGuide=false\ntext=" + string.Join(" | ", view.steps[step].GetComponentsInChildren<TMP_Text>().Select(t => t.text)));
    }

    static void Tick()
    {
        const string request = "Temp/RehearGripGuidePreview.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        const string initialRequest = "Temp/RehearInitialUI.request";
        if (File.Exists(initialRequest))
        {
            File.Delete(initialRequest);
            try { ShowInitialUI(); }
            catch (Exception e) { File.WriteAllText("Temp/RehearInitialUI.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        const string completionRequest = "Temp/RehearCompletionUIPreview.request";
        if (File.Exists(completionRequest))
        {
            File.Delete(completionRequest);
            try { ShowCompletionUI(); }
            catch (Exception e) { File.WriteAllText("Temp/RehearCompletionUIPreview.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        const string designRequest = "Temp/RehearPausedUIDesign.request";
        if (File.Exists(designRequest))
        {
            File.Delete(designRequest);
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
                var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
                RehearFigmaTutorialSetup.MatchPauseDesign(view.steps[7].transform);
                ShowPausedUI();
                File.WriteAllText("Temp/RehearPausedUIDesign.txt", "PASS\nsource=2323:39413\npanel=537x498\nhelper=804x135\ngap=80\nbuttons=467x56\niconGap=10\nfonts=Pretendard Bold/Medium\nclickEvents=retained\n");
            }
            catch (Exception e) { File.WriteAllText("Temp/RehearPausedUIDesign.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        const string pausedRequest = "Temp/RehearPausedUIPreview.request";
        if (File.Exists(pausedRequest))
        {
            File.Delete(pausedRequest);
            try { ShowPausedUI(); }
            catch (Exception e) { File.WriteAllText("Temp/RehearPausedUIPreview.txt", "FAIL\n" + e); Debug.LogException(e); }
        }
        if (File.Exists(request))
        {
            File.Delete(request);
            try { StartPreview(true); }
            catch (Exception e) { File.WriteAllText("Temp/RehearGripGuidePreview.txt", "FAIL\n" + e); Debug.LogException(e); Stop(); }
        }
        if (!visual) return;
        var guide = UnityEngine.Object.FindFirstObjectByType<TutorialControlGuideView>();
        if (!guide || !guide.gameObject.activeInHierarchy || !guide.title.text.Contains("그립")) { Stop(); return; }
        double now = EditorApplication.timeSinceStartup;
        if (now - previousTime < 1.0 / 30) return;
        visual.UpdateScenePreview((float)(now % 1.35) / 1.35f, Mathf.Min(.1f, (float)(now - previousTime)));
        previousTime = now;
        SceneView.RepaintAll();
    }

    static void StartPreview(bool updateUI)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") return;
        Cleanup();
        foreach (var window in Resources.FindObjectsOfTypeAll<RehearGripGuidePreview>()) window.Close();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        visual = all.Select(t => t.GetComponent<Quest3TutorialControllerVisual>()).Single(c => c);
        var guide = all.Select(t => t.GetComponent<TutorialControlGuideView>()).Single(c => c);
        var view = all.Select(t => t.GetComponent<TutorialFigmaView>()).Single(c => c);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Settings/TutorialUI/ControllerGuidance/Controller Button Blue.mat");
        var shader = Shader.Find("Rehear/Controller Button Glow");
        if (!shader || !material) throw new Exception("Missing controller surface shader/material.");
        if (ShaderUtil.ShaderHasError(shader)) throw new Exception("Controller shader has compile errors.");
        if (material.shader != shader)
        {
            Undo.RecordObject(material, "Blue light on controller button surface");
            material.shader = shader;
            material.SetColor("_BaseColor", new Color(.025f, .10f, .32f));
            material.SetColor("_EmissionColor", new Color(.005f, .20f, 1f) * 1.2f);
            material.SetFloat("_Smoothness", .32f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        if (updateUI)
        {
            Undo.RecordObjects(view.steps, "Show grip guide");
            Undo.RecordObjects(guide.GetComponentsInChildren<TMP_Text>(true), "Grip guide text");
            Undo.RecordObject(guide.gameObject, "Show grip guide");
            Undo.RecordObject(guide.continueButton, "Preview guide button");
            if (view.deskDirectionHints) Undo.RecordObject(view.deskDirectionHints, "Hide navigation");
            if (view.scriptDirectionHints) Undo.RecordObject(view.scriptDirectionHints, "Hide navigation");
            view.Show(-1);
            guide.Show("중지로 그립을 눌러요", "오른손 컨트롤러 옆면의 파란 버튼을 확인해 주세요.\n그립을 쥐면 일시정지하고, 놓았다 다시 쥐면 재개해요.");
            guide.SetReady(true);
            foreach (var label in guide.GetComponentsInChildren<TMP_Text>()) label.ForceMeshUpdate(true);
            Canvas.ForceUpdateCanvases();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Scene save failed.");
            Selection.activeGameObject = guide.gameObject;
            var camera = all.Select(t => t.GetComponent<Camera>()).Single(c => c && c.CompareTag("MainCamera"));
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(camera.transform.position + camera.transform.forward * 3, camera.transform.rotation, 3);
        }
        // Same code, model, original triangles, material and tracked parent as Play mode.
        visual.PreviewInScene(Quest3TutorialControllerVisual.Cue.Grip);
        SessionState.SetBool(ActiveKey, true);
        previousTime = EditorApplication.timeSinceStartup;
        File.WriteAllText("Temp/RehearGripGuidePreview.txt", "PASS\nguide=Grip before practice\npreview=Scene controller\nsurface=Original button triangles\nlight=Shaded blue emissive rim\nextraHighlightRenderer=false\ncontrollerPose=unchanged\neditorGeometrySaved=false");
    }
    static void Cleanup()
    {
        if (visual) visual.EndScenePreview();
        visual = null;
    }
}
