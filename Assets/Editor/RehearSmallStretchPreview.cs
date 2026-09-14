#if UNITY_EDITOR

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class RehearSmallStretchPreview
{
    private const string RequestPath = "Temp/RehearSmallStretchPreview.request";
    private const string ReportPath = "Temp/RehearSmallStretchPreview.txt";

    static RehearSmallStretchPreview() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (AssetDatabase.IsAssetImportWorkerProcess() || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            !File.Exists(RequestPath)) return;

        File.Delete(RequestPath);
        try { Show(); }
        catch (Exception exception)
        {
            File.WriteAllText(ReportPath, exception.ToString());
            Debug.LogException(exception);
        }
    }

    [MenuItem("Rehear/Debug/Preview Small Stretch")]
    private static void Show()
    {
        if (!EditorApplication.ExecuteMenuItem("Rehear/Preview Seated Presentation Audience"))
            throw new InvalidOperationException("Audience preview command was unavailable.");

        var preview = SceneManager.GetActiveScene().GetRootGameObjects()
            .Single(root => root.name == "Audience Preview (Editor Only)");
        var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>(
            "Assets/Settings/AudienceAnimationCatalog.asset");
        if (!catalog) throw new InvalidOperationException("Audience animation catalog is missing.");

        foreach (var player in preview.GetComponentsInChildren<AudienceAnimationPlayer>(true))
        {
            if (!catalog.TryGetClip("ACT_06.smallstretch", player.Gender, out var clip))
                throw new InvalidOperationException($"Small stretch is missing for {player.Gender}.");

            var actor = player.gameObject;
            var position = actor.transform.position;
            var rotation = actor.transform.rotation;
            clip.SampleAnimation(actor, Mathf.Min(3.15f, clip.length * .5f));
            actor.transform.SetPositionAndRotation(position, rotation);
        }

        var sourceClip = catalog.Entries.Single(entry =>
            entry.variationId == "ACT_06.smallstretch").maleClip;
        var curveReport = AnimationUtility.GetCurveBindings(sourceClip)
            .Select(binding =>
            {
                var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
                return $"{binding.propertyName}: " +
                    $"min={curve.keys.Min(key => key.value):F3}, " +
                    $"max={curve.keys.Max(key => key.value):F3}, keys={curve.length}";
            });

        Selection.activeGameObject = preview;
        SceneView.lastActiveSceneView?.FrameSelected();
        SceneView.RepaintAll();
        File.WriteAllText(ReportPath,
            "PASS: sampled ACT_06.smallstretch at the first arm peak on all six seated audience prefabs.\n" +
            string.Join("\n", curveReport) + "\n");
    }
}

#endif
