using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearControllerGuideChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearControllerGuideChecks() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearControllerGuideChecks.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        try { Run(); File.WriteAllText("Temp/RehearControllerGuideChecks.txt", "PASS\nguides=trigger,frontSlide,rearSlide,script,grip\nminimumDuration=guarded\nwaiting=requiresExplicitContinue\nguideInputs=noCount\nreleaseGate=heldStickBlocked\nallPracticeCounts=zeroAfterGuide\noriginalRendererRestore=PASS\ntrackedParentAndImportedScale=PASS\nthreeBoneHighlights=PASS\n"); }
        catch (Exception e) { File.WriteAllText("Temp/RehearControllerGuideChecks.txt", "FAIL\n" + e); }
    }
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Private).SetValue(o, value);
    static object Get(object o, string name) => o.GetType().GetField(name, Private).GetValue(o);
    static object Call(object o, string name, params object[] args) => o.GetType().GetMethod(name, Private).Invoke(o, args);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static TMP_Text Label(Transform parent)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        return go.GetComponent<TMP_Text>();
    }
    static void Run()
    {
        var authored = UnityEngine.Object.FindFirstObjectByType<Quest3TutorialControllerVisual>(FindObjectsInactive.Include);
        Check(authored, "Scene visual is present");
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("ControllerGuideChecks");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            // Inactive manager avoids Awake's scene rig discovery and side effects.
            var manager = root.AddComponent<TutorialManager>();
            var guideGo = new GameObject("Guide", typeof(RectTransform), typeof(TutorialControlGuideView));
            guideGo.transform.SetParent(root.transform, false);
            var guide = guideGo.GetComponent<TutorialControlGuideView>();
            guide.title = Label(guide.transform);
            guide.description = Label(guide.transform);
            var buttonGo = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(guide.transform, false);
            guide.continueButton = buttonGo.GetComponent<Button>();
            guide.continueLabel = Label(buttonGo.transform);
            Set(manager, "controlGuide", guide);
            var stepType = manager.GetType().GetField("currentStep", Private).FieldType;
            foreach (var step in new[] { "TriggerPractice", "SlidePractice", "RearSlidePractice", "ScriptPractice", "PausePractice" })
            {
                Call(manager, "BeginDemonstration", Enum.Parse(stepType, step));
                Check((bool)Get(manager, "demonstrating") && (int)Get(manager, "practiceCount") == 0, step + " guide entered");
                manager.OnPrimaryButtonPressed();
                Check(!(bool)Get(manager, "waitingForGuideRelease"), "Too-early click rejected");
                Check(!(bool)Call(manager, "HandleSlidePracticeInput", 1f) && !(bool)Call(manager, "HandleScriptPracticeInput", -1f), "Guide blocks all stick handlers");
                Call(manager, "OnGripPerformed", default(InputAction.CallbackContext));
                Check(Get(manager, "currentStep").ToString() == step, "Guide grip does not advance");
                Call(manager, "ProcessDemonstration", (float)Get(manager, "demonstrationReadyAt") + 1);
                Check((bool)Get(manager, "demonstrating") && guide.continueButton.interactable, "No automatic advance");
                Set(manager, "demonstrationReadyAt", float.MinValue);
                manager.OnPrimaryButtonPressed();
                Call(manager, "AdvanceDemonstration", Time.unscaledTime, false, false, Vector2.right);
                Check((bool)Get(manager, "demonstrating"), "Held stick blocks guide exit");
                Call(manager, "AdvanceDemonstration", Time.unscaledTime, true, false, Vector2.zero);
                Check((bool)Get(manager, "demonstrating"), "Held trigger blocks guide exit");
                Call(manager, "AdvanceDemonstration", Time.unscaledTime, false, true, Vector2.zero);
                Check((bool)Get(manager, "demonstrating"), "Held grip blocks guide exit");
                Call(manager, "ProcessDemonstration", Time.unscaledTime);
                Check(!(bool)Get(manager, "demonstrating") && (int)Get(manager, "practiceCount") == 0, "Released input arms zero-count practice");
                Check(!guide.gameObject.activeSelf && Get(manager, "currentStep").ToString() == step, "Correct practice entered");
            }

            var hand = new GameObject("Test Right Hand");
            hand.transform.SetParent(root.transform, false);
            var originalGo = new GameObject("Original", typeof(MeshRenderer));
            originalGo.transform.SetParent(hand.transform, false);
            var original = originalGo.GetComponent<MeshRenderer>();
            var visual = root.AddComponent<Quest3TutorialControllerVisual>();
            foreach (var field in new[] { "controllerModelPrefab", "controllerAnimator", "triggerHighlight", "stickHighlight", "gripHighlight", "highlightMaterial" })
            {
                Check(Get(authored, field) is UnityEngine.Object obj && obj, "Authored reference " + field);
                Set(visual, field, Get(authored, field));
            }
            Set(visual, "rightHandTarget", hand.transform);
            Set(visual, "originalControllerRenderers", new Renderer[] { original });
            Call(visual, "BuildVisual");
            var overlay = (Transform)Get(visual, "visualRoot");
            Check(overlay.parent == hand.transform && overlay.localPosition == Vector3.zero && overlay.localRotation == Quaternion.identity, "Tracked grip pose parent");
            var prefab = (GameObject)Get(visual, "controllerModelPrefab");
            Check(overlay.GetChild(0).localScale == prefab.transform.localScale, "Imported scale retained");
            foreach (var cue in new[] { Quest3TutorialControllerVisual.Cue.Trigger, Quest3TutorialControllerVisual.Cue.StickHorizontal, Quest3TutorialControllerVisual.Cue.Grip })
            {
                visual.Show(cue);
                var highlight = (SkinnedMeshRenderer)Get(visual, "highlight");
                Check(highlight.sharedMesh && highlight.sharedMesh.triangles.Length > 0, "Nonempty highlighted button");
                Call(visual, "AnimateCue", .5f);
            }
            // Inactive root deliberately cannot suppress the scene's renderers.
            Set(visual, "previousForceOff", new[] { false });
            Set(visual, "suppressingOriginal", true);
            original.forceRenderingOff = true;
            Call(visual, "RestoreOriginal");
            Check(!original.forceRenderingOff, "Original restored after guide");
            var material = (Material)Get(visual, "runtimeHighlight");
            if (material) UnityEngine.Object.DestroyImmediate(material);
            Set(visual, "visualRoot", null);
            Set(visual, "runtimeHighlight", null);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
