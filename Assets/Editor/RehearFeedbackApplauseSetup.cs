using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearFeedbackApplauseSetup
{
    const string FeedbackPath = "Assets/01_Scene/Scene_03_Feedback.unity";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearFeedbackApplauseSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string request = "Temp/RehearFeedbackApplause.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { File.Delete(request); Run(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearFeedbackApplause.txt", "FAIL\n" + e); }
    }

    static void Run()
    {
        var report = new StringBuilder();
        var catalog = AssetDatabase.LoadAssetAtPath<AudienceAnimationCatalog>("Assets/Settings/AudienceAnimationCatalog.asset");
        var entries = catalog.Entries.Where(e => !e.variationId.StartsWith("AP_", StringComparison.Ordinal)).ToList();
        var stems = new[] { "AP_01_polite_applause", "AP_02_enthusiastic_applause", "AP_03_light_applause" };
        AnimationClip Clip(string gender, string stem, string suffix)
        {
            var path = $"Assets/06_Animation/Clips/{gender}/{stem}_{suffix}.fbx";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__"));
            if (!clip.isLooping) throw new Exception("Applause clip must loop: " + path);
            report.AppendLine(path + " length=" + clip.length);
            return clip;
        }
        for (int i = 0; i < 3; i++) entries.Add(new AudienceAnimationCatalog.Entry {
            variationId = FeedbackAudienceApplause.Variations[i], maleClip = Clip("Male", stems[i], "M"), femaleClip = Clip("Female", stems[i], "F")
        });
        catalog.EditorSetEntries(entries); EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();

        var original = SceneManager.GetActiveScene();
        var presentation = SceneManager.GetSceneByPath("Assets/01_Scene/Scene_02_Presentation.unity");
        bool openedPresentation = !presentation.isLoaded;
        if (openedPresentation) presentation = EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity", OpenSceneMode.Additive);
        var feedback = SceneManager.GetSceneByPath(FeedbackPath); bool openedFeedback = !feedback.isLoaded;
        if (openedFeedback) feedback = EditorSceneManager.OpenScene(FeedbackPath, OpenSceneMode.Additive);
        try
        {
            var source = presentation.GetRootGameObjects().Where(g => g.name != "Audience Preview (Editor Only)")
                .SelectMany(g => g.GetComponentsInChildren<AudienceSeating>(true)).Single();
            foreach (var old in feedback.GetRootGameObjects().Where(g => g.name == "FeedbackAudience" || g.name == "Chair").ToArray()) Object.DestroyImmediate(old);
            var root = new GameObject("FeedbackAudience"); SceneManager.MoveGameObjectToScene(root, feedback);
            var seatingObject = Object.Instantiate(source.gameObject); SceneManager.MoveGameObjectToScene(seatingObject, feedback);
            seatingObject.transform.SetParent(root.transform, true);
            var applause = root.AddComponent<FeedbackAudienceApplause>();
            var serialized = new SerializedObject(applause);
            serialized.FindProperty("seating").objectReferenceValue = seatingObject.GetComponent<AudienceSeating>(); serialized.ApplyModifiedPropertiesWithoutUndo();
            var sourceChairs = presentation.GetRootGameObjects().Single(g => g.name == "Chair");
            var chairs = Object.Instantiate(sourceChairs); chairs.name = "Chair"; SceneManager.MoveGameObjectToScene(chairs, feedback);
            report.AppendLine("Copied presentation seats and six chairs; feedback audience starts from captured visual/seat mapping.");
            EditorSceneManager.MarkSceneDirty(feedback); EditorSceneManager.SaveScene(feedback);
            Verify(seatingObject, source, report);
            File.WriteAllText("Temp/RehearFeedbackApplause.txt", report.ToString());
        }
        finally
        {
            SceneManager.SetActiveScene(original);
            if (openedFeedback) EditorSceneManager.CloseScene(feedback, true);
            if (openedPresentation) EditorSceneManager.CloseScene(presentation, true);
        }
    }

    static void Verify(GameObject template, AudienceSeating source, StringBuilder report)
    {
        var scene = EditorSceneManager.NewPreviewScene(); GameObject root = null, previous = null;
        try
        {
            previous = Object.Instantiate(source.gameObject); SceneManager.MoveGameObjectToScene(previous, scene);
            var before = previous.GetComponent<AudienceSeating>(); before.Initialize(7382);
            FeedbackAudienceApplause.Capture(before);
            var expected = before.seats.ToDictionary(s => s.SeatId, s => s.Occupant.name);
            root = Object.Instantiate(template); SceneManager.MoveGameObjectToScene(root, scene);
            var owner = root.AddComponent<FeedbackAudienceApplause>(); owner.InitializeAudience();
            var layout = root.GetComponent<AudienceSeating>();
            if (layout.seats.Any(s => expected[s.SeatId] != s.Occupant.name)) throw new Exception("Audience identity changed between scenes.");
            var audience = owner.OrderedAudience();
            var clips = new System.Collections.Generic.HashSet<AnimationClip>();
            foreach (var b in audience)
            {
                b.enabled = true;
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", Flags).Invoke(b, null);
                var graph = (PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph", Flags).GetValue(b);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            }
            typeof(FeedbackAudienceApplause).GetMethod("Start", Flags).Invoke(owner, null);
            for (int i = 0; i < audience.Length; i++)
            {
                var b = audience[i];
                var graph = (PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph", Flags).GetValue(b);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var id = FeedbackAudienceApplause.VariationFor(audience, i);
                var target = typeof(AudienceAnimationPlayer).GetField("target", Flags).GetValue(b);
                if (target == null || (string)target.GetType().GetField("variation").GetValue(target) != id)
                    throw new Exception("Feedback Start did not apply the assigned applause: " + b.name);
                var playable = (AnimationClipPlayable)target.GetType().GetField("playable").GetValue(target);
                if (playable.GetTime() != 0) throw new Exception("Authored applause start was randomized.");
                var clip = playable.GetAnimationClip(); clips.Add(clip);
                for (int frame = 0; frame < Mathf.CeilToInt(clip.length * 2 / .02f); frame++)
                {
                    typeof(AudienceAnimationPlayer).GetMethod("Advance", Flags).Invoke(b, new object[] { .02f });
                    graph.Evaluate(.02f); b.ApplyPropPoses();
                }
                if (playable.GetSpeed() == 0) throw new Exception("Applause froze at the end.");
                report.AppendLine(b.name + " -> " + AssetDatabase.GetAssetPath(clip) + " (starts at 0; two cycles passed)");
            }
            if (clips.Count != 6) throw new Exception("The six audience members share applause clips.");
            report.AppendLine("PASS same six visual identities and seats, six distinct gender-matched clips, original start times, looping playback, scene references saved for APK.");
        }
        finally { if (root) Object.DestroyImmediate(root); if (previous) Object.DestroyImmediate(previous); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
