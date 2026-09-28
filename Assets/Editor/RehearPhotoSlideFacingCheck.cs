#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearPhotoSlideFacingCheck
{
    const string Request = "Temp/RehearPhotoSlideFacingCheck.request";
    const string Report = "Temp/RehearPhotoSlideFacingCheck.txt";

    static RehearPhotoSlideFacingCheck() => EditorApplication.update += Poll;

    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
        var command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { Run(command); }
        catch (System.Exception exception) { File.WriteAllText(Report, "FAIL\n" + exception); }
    }

    static string PathOf(Transform transform)
    {
        var path = transform.name;
        while (transform.parent) { transform = transform.parent; path = transform.name + "/" + path; }
        return path;
    }

    static void Run(string command)
    {
        var scene = EditorSceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var bindings = all.Select(transform => transform.GetComponent<PresentationAudienceBindings>()).First(component => component);
        if (command.Equals("bind", System.StringComparison.OrdinalIgnoreCase))
        {
            var slide = all.Single(transform => transform.name == "Slide" && transform.Find("SlideScreen"));
            Undo.RecordObject(bindings, "Bind audience slide target to Slide");
            bindings.slideTarget = slide;
            EditorUtility.SetDirty(bindings);
            EditorSceneManager.MarkSceneDirty(scene);
            foreach (var gaze in all.Select(transform => transform.GetComponent<AudienceGazeController>()).Where(gaze => gaze))
                gaze.ConfigureTargets(bindings.presenterTarget, bindings.slideTarget, bindings.aroundTargets);
            EditorSceneManager.SaveScene(scene);
        }
        var report = new StringBuilder();
        report.AppendLine($"binding slide={bindings.slideTarget.position:F3} path={PathOf(bindings.slideTarget)}");
        report.AppendLine($"presenter={bindings.presenterTarget.position:F3} path={PathOf(bindings.presenterTarget)}");
        foreach (var transform in all.Where(transform =>
                     transform.name.IndexOf("slide", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     transform.name.IndexOf("screen", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     transform.name.IndexOf("counter", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                     transform.name.IndexOf("podium", System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            var renderer = transform.GetComponent<Renderer>();
            report.AppendLine($"OBJECT {PathOf(transform)} pos={transform.position:F3} forward={transform.forward:F3}" +
                              (renderer ? $" bounds={renderer.bounds.center:F3}" : ""));
        }
        foreach (var seat in bindings.seating.seats)
        {
            var toSlide = Vector3.ProjectOnPlane(bindings.slideTarget.position - seat.transform.position, Vector3.up);
            report.AppendLine($"SEAT {seat.name} pos={seat.transform.position:F3} forward={seat.transform.forward:F3} slideYaw={Vector3.SignedAngle(seat.transform.forward, toSlide, Vector3.up):F2}");
        }
        File.WriteAllText(Report, report.ToString());
    }
}
#endif
