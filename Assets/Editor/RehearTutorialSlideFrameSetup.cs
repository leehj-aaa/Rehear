using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearTutorialSlideFrameSetup
{
    const string Request = "Temp/RehearTutorialSlideFrame.request";
    static double next;
    static RehearTutorialSlideFrameSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialSlideFrame.txt", "FAILED\n" + e); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var frame = all.Single(t => t.name == "Slide" && t.GetComponent<MeshRenderer>());
        var screen = (RectTransform)all.Single(t => t.name == "SlideScreen");
        var oldCanvas = screen.GetComponentInParent<Canvas>();
        var image = screen.GetComponent<RawImage>();
        var texture = image.texture;
        var manager = UnityEngine.Object.FindFirstObjectByType<PresentationManager>();
        if (!manager || manager.slideScreen != image) throw new InvalidOperationException("Unexpected slide binding.");
        var mesh = frame.GetComponent<MeshFilter>().sharedMesh;
        var materials = frame.GetComponent<MeshRenderer>().sharedMaterials;
        int screenIndex = Array.FindIndex(materials, m => m && m.name == "Screen");
        if (screenIndex < 0) throw new InvalidOperationException("Frame screen surface not found.");
        var vertices = mesh.vertices; var indices = mesh.GetIndices(screenIndex);
        var bounds = new Bounds(frame.TransformPoint(vertices[indices[0]]), Vector3.zero);
        foreach (int index in indices) bounds.Encapsulate(frame.TransformPoint(vertices[index]));
        if (bounds.size.x > .01f || bounds.size.y < .5f || bounds.size.z < 1f)
            throw new InvalidOperationException("Unexpected screen plane orientation.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Fit UI slide inside existing frame");
        try
        {
            var canvas = screen.GetComponent<Canvas>();
            if (!canvas)
            {
                canvas = Undo.AddComponent<Canvas>(screen.gameObject);
                EditorUtility.CopySerialized(oldCanvas, canvas);
            }
            Undo.RecordObjects(new UnityEngine.Object[] { screen, canvas }, "Fit slide UI to screen surface");
            Undo.SetTransformParent(screen, frame, "Keep slide UI attached to frame");
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = oldCanvas.worldCamera;
            Vector3 scale = frame.lossyScale;
            screen.localScale = new Vector3(.01f / scale.x, .01f / scale.y, .01f / scale.z);
            // Inset by 2 mm per edge, and put UI 4 mm in front of the old white face.
            // Keep the old face as a backing surface and preserve the 3D frame.
            screen.sizeDelta = new Vector2((bounds.size.z - .004f) * 100, (bounds.size.y - .004f) * 100);
            screen.SetPositionAndRotation(bounds.center + Vector3.left * .004f, Quaternion.Euler(0, 90, 0));
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; screen.GetWorldCorners(corners);
            float width = Vector3.Distance(corners[1], corners[2]);
            float height = Vector3.Distance(corners[0], corners[1]);
            if (Mathf.Abs(width - (bounds.size.z - .004f)) > .001f || Mathf.Abs(height - (bounds.size.y - .004f)) > .001f)
                throw new InvalidOperationException("UI dimensions do not match frame.");
            if (image.texture != texture || manager.slideScreen != image) throw new InvalidOperationException("Slide binding changed.");
            EditorUtility.SetDirty(screen); EditorUtility.SetDirty(canvas);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = screen.gameObject;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(screen.position, Quaternion.Euler(0, 90, 0), 2.1f);
            File.WriteAllText("Temp/RehearTutorialSlideFrame.txt", $"parent={screen.parent.name}\nframePreserved=true\nslideBindingPreserved=true\ntexturePreserved=true\nwidth={width:F4}\nheight={height:F4}\nfrontGap=0.004\nsaved=true\n");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
}
