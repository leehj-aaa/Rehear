using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicit setup only: preserve authored arrow images, placement, and display transforms.
[InitializeOnLoad]
internal static class RehearSlideFeedbackSetup
{
    const string Request = "Temp/RehearSlideFeedback.request";
    static RehearSlideFeedbackSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception ex) { File.WriteAllText("Temp/RehearSlideFeedback.txt", "FAIL\n" + ex); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var view = all.Select(t => t.GetComponent<TutorialFigmaView>()).Single(v => v);
        var manager = all.Select(t => t.GetComponent<TutorialManager>()).Single(v => v);
        var presentation = all.Select(t => t.GetComponent<PresentationManager>()).Single(v => v);
        var desk = presentation.deskScreen;
        if (!desk || !desk.gameObject.activeInHierarchy) throw new Exception("DeskScreen must be visible.");
        var hints = desk.transform.Find("SlideDirectionHints");
        var left = hints.Find("Arrow_Left").GetComponent<TutorialDirectionArrow>();
        var right = hints.Find("Arrow_Right").GetComponent<TutorialDirectionArrow>();
        if (!left.sprite || !right.sprite) throw new Exception("Missing Next Arrow sprites.");
        var sound = AssetDatabase.LoadAssetAtPath<AudioClip>(RehearBoundarySoundSetup.SoundPath);
        if (!sound) throw new Exception("Missing wrong.mp3.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect slide direction feedback");
        Undo.RecordObjects(new UnityEngine.Object[] { presentation, manager, view, hints.gameObject, left.gameObject, right.gameObject, view.scriptDirectionHints }, "Connect slide feedback");
        Undo.RecordObjects(view.steps, "Preview slide practice");
        var p = new SerializedObject(presentation);
        p.FindProperty("previousSlideHint").objectReferenceValue = left;
        p.FindProperty("nextSlideHint").objectReferenceValue = right;
        p.ApplyModifiedProperties();
        var m = new SerializedObject(manager);
        m.FindProperty("slideBoundarySound").objectReferenceValue = sound;
        m.ApplyModifiedProperties();
        view.deskDirectionHints = hints.gameObject;
        left.gameObject.SetActive(true);
        right.gameObject.SetActive(true);
        view.Show(3);
        if (!left.isActiveAndEnabled || !right.isActiveAndEnabled) throw new Exception("Slide hints still hidden.");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = desk.gameObject;
        if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(desk.transform.position, desk.transform.rotation, .7f);
        File.WriteAllText("Temp/RehearSlideFeedback.txt", "PASS\nleftVisible=true\nrightVisible=true\narrowStyleAndPlacement=preserved\npreview=Step 3\nsound=wrong.mp3\nslides=" + presentation.slides.Length + "\nsaved=true");
    }
}
