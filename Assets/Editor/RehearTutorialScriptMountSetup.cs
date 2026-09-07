using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearTutorialScriptMountSetup
{
    const string Request = "Temp/RehearTutorialScriptMount.request";
    static double next;
    static RehearTutorialScriptMountSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialScriptMount.txt", "FAILED\n" + e); }
    }

    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var counter = all.Single(t => t.name == "Counter" && t.parent && t.parent.name == "_Props");
        var panel = (RectTransform)all.Single(t => t.name == "Panel_Script_New");
        var mesh = counter.GetComponent<MeshFilter>().sharedMesh;
        var materials = counter.GetComponent<MeshRenderer>().sharedMaterials;
        int index = Array.FindIndex(materials, m => m && m.name == "Screen");
        if (index < 0) throw new InvalidOperationException("Podium script surface not found.");
        var triangles = mesh.GetTriangles(index);
        var vertices = mesh.vertices;
        var points = triangles.Distinct().Select(i => counter.TransformPoint(vertices[i])).ToArray();
        Vector3 normal = Vector3.Cross(counter.TransformVector(vertices[triangles[1]] - vertices[triangles[0]]),
            counter.TransformVector(vertices[triangles[2]] - vertices[triangles[0]])).normalized;
        if (normal.y < 0) normal = -normal;
        if (normal.y < .5f) throw new InvalidOperationException("Unexpected podium surface orientation.");
        Vector3 up = Vector3.ProjectOnPlane(counter.forward, normal).normalized;
        Quaternion rotation = Quaternion.LookRotation(-normal, up);
        Quaternion inverse = Quaternion.Inverse(rotation);
        var bounds = new Bounds(inverse * points[0], Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(inverse * p);
        if (bounds.size.z > .005f || bounds.size.x < .2f || bounds.size.y < .2f)
            throw new InvalidOperationException("Script surface must be a flat rectangle.");
        Vector3 counterPosition = counter.position, counterScale = counter.localScale;
        Quaternion counterRotation = counter.rotation;
        var originalCanvas = panel.GetComponentInParent<Canvas>();
        var originalScale = panel.lossyScale;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Align script panel to podium reading surface");
        try
        {
            var canvas = panel.GetComponent<Canvas>();
            if (!canvas)
            {
                canvas = Undo.AddComponent<Canvas>(panel.gameObject);
                EditorUtility.CopySerialized(originalCanvas, canvas);
            }
            Undo.RecordObjects(new UnityEngine.Object[] { panel, canvas }, "Mount script panel");
            Undo.SetTransformParent(panel, counter, "Attach script to podium");
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = originalCanvas.worldCamera;
            panel.localScale = new Vector3(originalScale.x / counter.lossyScale.x,
                originalScale.y / counter.lossyScale.y, originalScale.z / counter.lossyScale.z);
            panel.sizeDelta = new Vector2((bounds.size.x - .008f) / originalScale.x, (bounds.size.y - .008f) / originalScale.y);
            Vector3 center = rotation * bounds.center + normal * .004f;
            panel.SetPositionAndRotation(center, rotation);
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4]; panel.GetWorldCorners(corners);
            if (Vector3.Angle(panel.right, counter.right) > .1f ||
                corners.Any(p => Mathf.Abs(Vector3.Dot(p - center, normal)) > .001f) ||
                counter.position != counterPosition || counter.rotation != counterRotation || counter.localScale != counterScale)
                throw new InvalidOperationException("Alignment verification failed.");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = panel.gameObject;
            if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(panel.position, panel.rotation, .55f);
            File.WriteAllText("Temp/RehearTutorialScriptMount.txt",
                $"parent=Counter\nwidthAligned=true\nsurfaceAligned=true\npodiumTransformUnchanged=true\nsize={panel.rect.size}\nworldRotation={panel.eulerAngles}\nfrontGap=0.004\nsaved=true\n");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
}
