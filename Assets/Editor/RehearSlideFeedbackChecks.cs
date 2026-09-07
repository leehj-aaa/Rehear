using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearSlideFeedbackChecks
{
    const string Request = "Temp/RehearSlideFeedbackChecks.request";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearSlideFeedbackChecks() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        try { Run(); File.WriteAllText("Temp/RehearSlideFeedbackChecks.txt", "PASS\nfirstLeftAndLastRight=noCount\nvalidLeftRight=oneSlideOneCount\nblockedAndValidInputs=pressedFeedback\nmissingEmptyNullSlides=noCount\nhiddenDisplay=noCount\nthirdInput=delayedTransition\nrepeatedHeldInput=completionGuard\nvoidUnityEventAPI=preserved\nwrongClip=assigned\n"); }
        catch (Exception ex) { File.WriteAllText("Temp/RehearSlideFeedbackChecks.txt", "FAIL\n" + ex); }
    }
    static void Set(object o, string field, object value) => o.GetType().GetField(field, Private).SetValue(o, value);
    static object Get(object o, string field) => o.GetType().GetField(field, Private).GetValue(o);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, Private).Invoke(o, args);
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
    static void Run()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("SlideFeedbackChecks", typeof(RectTransform), typeof(Canvas));
        SceneManager.MoveGameObjectToScene(root, scene);
        var textures = new[] { new Texture2D(2, 2), new Texture2D(2, 2), new Texture2D(2, 2), new Texture2D(2, 2) };
        try
        {
            var displayObject = new GameObject("Screen", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            displayObject.transform.SetParent(root.transform, false);
            var presentation = root.AddComponent<PresentationManager>();
            presentation.slides = textures;
            presentation.deskScreen = displayObject.GetComponent<RawImage>();
            presentation.slideScreen = presentation.deskScreen;
            var leftObject = new GameObject("Left", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialDirectionArrow));
            var rightObject = new GameObject("Right", typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialDirectionArrow));
            leftObject.transform.SetParent(root.transform, false);
            rightObject.transform.SetParent(root.transform, false);
            var left = leftObject.GetComponent<TutorialDirectionArrow>();
            var right = rightObject.GetComponent<TutorialDirectionArrow>();
            Set(presentation, "previousSlideHint", left);
            Set(presentation, "nextSlideHint", right);
            var manager = root.AddComponent<TutorialManager>();
            manager.enabled = false;
            Set(manager, "presentationManager", presentation);
            Set(manager, "requiredPracticeCount", 100);
            var stepType = manager.GetType().GetField("currentStep", Private).FieldType;
            Set(manager, "currentStep", Enum.Parse(stepType, "SlidePractice"));
            int events = 0;
            presentation.SlideChanged += _ => events++;
            Check(presentation.IsAtSlideBoundary(false), "First left boundary eligible");
            Check(!(bool)Call(manager, "HandleSlidePracticeInput", -1f), "First left blocked");
            Check((int)Get(manager, "practiceCount") == 0 && (bool)Get(left, "pressed") && events == 0, "Blocked left pressed but not counted");
            for (int i = 1; i <= 3; i++)
            {
                Check((bool)Call(manager, "HandleSlidePracticeInput", 1f), "Right accepted");
                Check(presentation.CurrentSlideIndex == i && (int)Get(manager, "practiceCount") == i && presentation.deskScreen.texture == textures[i], "Actual right change counted");
            }
            Check((bool)Get(right, "pressed") && events == 3 && presentation.IsAtSlideBoundary(true), "Right feedback and last boundary");
            Check(!(bool)Call(manager, "HandleSlidePracticeInput", 1f) && (int)Get(manager, "practiceCount") == 3 && events == 3, "Last right no count/event");
            Check((bool)Call(manager, "HandleSlidePracticeInput", -1f) && presentation.CurrentSlideIndex == 2, "Valid left");
            displayObject.SetActive(false);
            Check(!(bool)Call(manager, "HandleSlidePracticeInput", -1f), "Hidden screen no count");
            displayObject.SetActive(true);
            presentation.slides = null;
            Check(!presentation.TryNextSlide() && !presentation.TryPrevSlide() && !presentation.IsAtSlideBoundary(false), "Missing deck");
            presentation.slides = Array.Empty<Texture2D>();
            Check(!presentation.TryNextSlide() && !presentation.TryPrevSlide(), "Empty deck");
            presentation.slides = new Texture2D[] { textures[0], null };
            Set(presentation, "currentIndex", 0);
            Check(!(bool)Call(manager, "HandleSlidePracticeInput", 1f) && (int)Get(manager, "practiceCount") == 4, "Missing target no count");
            presentation.slides = textures;
            Set(manager, "practiceCount", 0);
            Set(manager, "requiredPracticeCount", 3);
            for (int i = 0; i < 3; i++) Call(manager, "HandleSlidePracticeInput", 1f);
            Check((bool)Get(manager, "slideCompletionPending") && Get(manager, "currentStep").ToString() == "SlidePractice", "Final press stays visible");
            Check(!(bool)Call(manager, "HandleSlidePracticeInput", -1f), "Completion blocks extra input");
            Call(manager, "ProcessSlideCompletion", (float)Get(manager, "slideCompletionAt") + .01f);
            Check(Get(manager, "currentStep").ToString() == "RearSlidePractice" && (int)Get(manager, "practiceCount") == 0, "Advance after feedback");
            presentation.PrevSlide();
            presentation.NextSlide();
            Check(presentation.CurrentSlideIndex == 3, "Legacy API remains callable");
            Check(AssetDatabase.LoadAssetAtPath<AudioClip>(RehearBoundarySoundSetup.SoundPath), "Custom wrong clip exists");
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
            foreach (var texture in textures) UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
