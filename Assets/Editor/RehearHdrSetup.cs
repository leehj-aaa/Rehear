using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[InitializeOnLoad]
internal static class RehearHdrSetup
{
    const string RequestPath = "Temp/RehearHdrSetup.request";
    const string ReportPath = "Temp/RehearHdrSetup.txt";

    static readonly string[] PipelinePaths =
    {
        "Assets/Settings/Project Configuration/Performance URP Config.asset",
        "Assets/Settings/Project Configuration/Quality URP Config.asset"
    };

    static RehearHdrSetup() => EditorApplication.update += Poll;

    [MenuItem("Rehear/Enable HDR For ACES")]
    static void Request() => File.WriteAllText(RequestPath, "apply");

    static void Poll()
    {
        if (!File.Exists(RequestPath) || AssetDatabase.IsAssetImportWorkerProcess() ||
            EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            return;

        try
        {
            File.Delete(RequestPath);
            Apply();
        }
        catch (Exception e)
        {
            File.WriteAllText(ReportPath, "FAILED\n" + e);
            UnityEngine.Debug.LogException(e);
        }
    }

    static void Apply()
    {
        var report = new StringBuilder();
        foreach (string path in PipelinePaths)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (!pipeline) throw new FileNotFoundException("URP asset not found", path);
            Undo.RecordObject(pipeline, "Enable HDR for ACES");
            pipeline.supportsHDR = true;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssetIfDirty(pipeline);
            report.AppendLine($"{pipeline.name}: supportsHDR={pipeline.supportsHDR}");
        }

        AssetDatabase.SaveAssets();
        var current = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        report.AppendLine($"Current pipeline: {(current ? current.name : "none")}, supportsHDR={(current && current.supportsHDR)}");
        File.WriteAllText(ReportPath, report.ToString());
        File.WriteAllText("Temp/RehearQuestBuild.request", "build");
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
}
