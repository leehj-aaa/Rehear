using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Explicit, isolated regression checks; never edits or saves the user's scene.
[InitializeOnLoad]
internal static class RehearScriptFeedbackChecks
{
    const string Request = "Temp/RehearScriptFeedbackChecks.request";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearScriptFeedbackChecks() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Run(); }
        catch (Exception ex) { File.WriteAllText("Temp/RehearScriptFeedbackChecks.txt", "FAIL\n" + ex); }
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Private).SetValue(target, value);
    static object Get(object target, string field) => target.GetType().GetField(field, Private).GetValue(target);
    static object Call(object target, string method, params object[] args) => target.GetType()
        .GetMethod(method, Private, null, Array.ConvertAll(args, arg => arg.GetType()), null).Invoke(target, args);
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }

    static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ScriptFeedbackChecks", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer));
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-Regular SDF.asset");
            text.text = "Test page";
            var scroller = root.AddComponent<ScriptScroller>();
            Set(scroller, "scriptText", text);
            Set(scroller, "useRuntimeSessionScript", false);
            Set(scroller, "pageCount", 4);
            Check(scroller.IsAtPageBoundary(false) && !scroller.IsAtPageBoundary(true), "First-page boundary sound eligibility");
            var up = new GameObject("Up", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialDirectionArrow));
            var down = new GameObject("Down", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialDirectionArrow));
            up.transform.SetParent(root.transform, false);
            down.transform.SetParent(root.transform, false);
            Set(scroller, "previousPageHint", up);
            Set(scroller, "nextPageHint", down);

            var manager = root.AddComponent<TutorialManager>();
            manager.enabled = false;
            Set(manager, "scriptScroller", scroller);
            Set(manager, "requiredPracticeCount", 100);
            Check(!(bool)Call(manager, "HandleScriptPracticeInput", 1f), "First-page up rejected");
            Check((int)Get(manager, "practiceCount") == 0, "Blocked input must not count");
            for (int i = 1; i <= 3; i++)
            {
                Check((bool)Call(manager, "HandleScriptPracticeInput", -1f), "Valid down accepted");
                Check(scroller.CurrentPage == i + 1 && (int)Get(manager, "practiceCount") == i, "One count per page");
            }
            Check(!down.activeSelf && up.activeSelf, "Last-page hint visibility");
            Check(scroller.IsAtPageBoundary(true), "Last-page boundary sound eligibility");
            Check(!(bool)Call(manager, "HandleScriptPracticeInput", -1f), "Last-page down rejected");
            Check((int)Get(manager, "practiceCount") == 3, "Last-page no extra count");
            Check((bool)Call(manager, "HandleScriptPracticeInput", 1f), "Valid up accepted");
            Check(scroller.CurrentPage == 3 && (int)Get(manager, "practiceCount") == 4, "Up changes exactly one page");
            textGo.SetActive(false);
            Check(!scroller.IsAtPageBoundary(true) && !scroller.IsAtPageBoundary(false), "Inactive content is not a boundary sound");
            Check(!(bool)Call(manager, "HandleScriptPracticeInput", -1f), "Inactive page rejected");
            textGo.SetActive(true);
            text.text = "";
            Check(!scroller.TryNextPage() && !scroller.TryPreviousPage(), "Empty content rejected");
            text.text = "Test page";
            Set(scroller, "currentPage", 1);
            Set(scroller, "pageCount", 1);
            Check(!scroller.TryNextPage() && !scroller.TryPreviousPage(), "Single page rejected");
            Set(manager, "scriptScroller", null);
            Check(!(bool)Call(manager, "HandleScriptPracticeInput", -1f), "Missing scroller rejected");
            Check((int)Get(manager, "practiceCount") == 4, "All rejected inputs leave count intact");

            Set(manager, "scriptScroller", scroller);
            Set(manager, "requiredPracticeCount", 3);
            Set(manager, "practiceCount", 0);
            Set(scroller, "pageCount", 4);
            var countGo = new GameObject("Remaining", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            countGo.transform.SetParent(root.transform, false);
            var remaining = countGo.GetComponent<TextMeshProUGUI>();
            remaining.font = text.font;
            Set(manager, "scriptRemainingText", remaining);
            Call(manager, "UpdateScriptRemaining");
            Check(remaining.text == "3회 남음", "Initial remaining count");
            Call(manager, "HandleScriptPracticeInput", 1f);
            Check(remaining.text == "3회 남음", "Blocked input preserves remaining count");
            for (int i = 0; i < 2; i++)
            {
                Call(manager, "HandleScriptPracticeInput", -1f);
                Check(remaining.text == (2 - i) + "회 남음", "Remaining count decrements");
            }
            Check((int)Get(manager, "practiceCount") == 2, "Before third valid input");
            Call(manager, "HandleScriptPracticeInput", -1f);
            Check(remaining.text == "완료!" && (bool)Get(manager, "scriptCompletionPending"), "Completion remains visible");
            Check(!(bool)Call(manager, "HandleScriptPracticeInput", 1f) && scroller.CurrentPage == 4, "No input during completion");
            float completionAt = (float)Get(manager, "scriptCompletionAt");
            Call(manager, "ProcessScriptCompletion", completionAt - .1f);
            Check((bool)Get(manager, "scriptCompletionPending"), "Completion delay retained");
            Call(manager, "ProcessScriptCompletion", completionAt + .1f);
            Check(Get(manager, "currentStep").ToString() == "PausePractice", "Completion then advances tutorial");

            down.SetActive(true);
            var arrow = down.GetComponent<TutorialDirectionArrow>();
            arrow.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/04_Images/Textures/UI/Next Arrow.png");
            arrow.color = Color.white;
            arrow.rectTransform.sizeDelta = new Vector2(6, 6);
            var originalScale = arrow.rectTransform.localScale;
            using (var mesh = new VertexHelper())
            {
                Call(arrow, "OnPopulateMesh", mesh);
                UIVertex normal = default;
                mesh.PopulateUIVertex(ref normal, 0);
                arrow.ShowPressedFeedback();
                Call(arrow, "OnPopulateMesh", mesh);
                UIVertex pressed = default;
                mesh.PopulateUIVertex(ref pressed, 0);
                Vector3 center = arrow.rectTransform.rect.center;
                Check(Mathf.Abs((pressed.position - center).magnitude / (normal.position - center).magnitude - .9f) < .001f, "Pressed mesh contracts 10 percent");
                Check(pressed.color.r < normal.color.r && pressed.color.a == normal.color.a, "Pressed tint darkens without transparency");
                Set(arrow, "pressUntil", float.MinValue);
                Call(arrow, "Update");
                Call(arrow, "OnPopulateMesh", mesh);
                UIVertex restored = default;
                mesh.PopulateUIVertex(ref restored, 0);
                Check(restored.position == normal.position && restored.color.Equals(normal.color), "Release restores original appearance");
                arrow.ShowPressedFeedback();
                down.SetActive(false);
                down.SetActive(true);
                Check(!(bool)Get(arrow, "pressed"), "Re-enable clears pressed state");
                Check(arrow.rectTransform.localScale == originalScale && arrow.color == Color.white, "Authored transform and tint unchanged");
            }
            File.WriteAllText("Temp/RehearScriptFeedbackChecks.txt", "PASS\nvalidDownUp=onePageOneCount\nfirstLastInactiveEmptySingleMissing=noCount\nremaining=3,2,1,complete\ncompletionDelayAndInputGuard=PASS\nboundarySoundEligibility=PASS\nthirdValidInput=PausePracticeAfterDelay\npressedMesh=90percent\npressedTint=65percent\nreleaseAndReenable=restored\nauthoredTransforms=unchanged");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
