using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearSeatAdjustChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static RehearSeatAdjustChecks() => EditorApplication.update += Poll;
    static void Poll()
    {
        const string request = "Temp/RehearSeatAdjustChecks.request";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        try { File.Delete(request); Run(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearSeatAdjustChecks.txt", e.ToString()); }
    }

    static void Run()
    {
        var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
        var source = roots.Where(g => g.name != "Audience Preview (Editor Only)")
            .SelectMany(g => g.GetComponentsInChildren<AudienceSeating>(true)).Single();
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null, chairs = null;
        var report = new StringBuilder();
        try
        {
            root = Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var chairSource = roots.FirstOrDefault(g => g.name == "Chair");
            if (chairSource) { chairs = Object.Instantiate(chairSource); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(chairs, scene); }
            var layout = root.GetComponent<AudienceSeating>(); layout.Initialize(1234);
            var bodies = root.GetComponentsInChildren<AudienceAnimationPlayer>();
            var graphs = bodies.Select(b => {
                b.enabled = true;
                typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", Flags).Invoke(b, null);
                var graph = (PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph", Flags).GetValue(b);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); return graph;
            }).ToArray();
            var hips = bodies.Select(b => b.GetComponentsInChildren<Transform>().Single(t => t.name == "pelvis")).ToArray();
            var spines = bodies.Select(b => b.GetComponentsInChildren<Transform>().Single(t => t.name == "spine_01")).ToArray();
            var necks = bodies.Select(b => b.GetComponentsInChildren<Transform>().Single(t => t.name == "neck_01")).ToArray();
            var hands = bodies.Select(b => b.GetComponentsInChildren<Transform>().Where(t => t.name == "hand_l" || t.name == "hand_r").ToArray()).ToArray();
            var rawMin = Enumerable.Repeat(90f, bodies.Length).ToArray();
            var fixedMin = Enumerable.Repeat(90f, bodies.Length).ToArray();
            var jumps = new float[bodies.Length]; var previous = new Vector3[bodies.Length];
            float Pitch(int i) { var d = necks[i].position - spines[i].position; return Mathf.Atan2(Vector3.Dot(d, bodies[i].transform.forward), Vector3.Dot(d, bodies[i].transform.up)) * Mathf.Rad2Deg; }
            foreach (var b in bodies)
                if (!b.PlayServerVariation("ACT_05.seatadjust", 6f, 1f)) throw new Exception("Rejected " + b.name);
            for (int frame = 0; frame < 500; frame++)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    typeof(AudienceAnimationPlayer).GetMethod("Advance", Flags).Invoke(bodies[i], new object[] { .02f });
                    graphs[i].Evaluate(.02f);
                    rawMin[i] = Mathf.Min(rawMin[i], Pitch(i));
                }
                if (frame == 100) Capture(bodies[0], "before");
                for (int i = 0; i < bodies.Length; i++)
                {
                    var rawNeck = necks[i].position;
                    var rawHands = hands[i].Select(t => t.position).ToArray();
                    bodies[i].ApplyPropPoses();
                    for (int h = 0; h < rawHands.Length; h++)
                        if (Vector3.Distance(rawHands[h], hands[i][h].position) > .01f)
                            throw new Exception("Authored hand contact moved " + bodies[i].name);
                    if (frame >= 60 && frame < 280)
                    {
                        fixedMin[i] = Mathf.Min(fixedMin[i], Pitch(i));
                        if (Pitch(i) < -4.05f) throw new Exception("Backrest lean limit failed " + bodies[i].name);
                        var pose = bodies[i].GetComponent<AudienceSeatedPose>();
                        if (bodies[i].transform.InverseTransformPoint(hips[i].position).z < pose.localHip.z - .0001f)
                            throw new Exception("Pelvis slid behind calibrated seat " + bodies[i].name);
                    }
                    if (frame > 0) jumps[i] = Mathf.Max(jumps[i], Vector3.Distance(previous[i], necks[i].position));
                    previous[i] = necks[i].position;
                    if (frame > 450 && Vector3.Distance(rawNeck, necks[i].position) > .00001f)
                        throw new Exception("Correction remained after baseline return");
                }
                if (frame == 100) Capture(bodies[0], "after");
            }
            for (int i = 0; i < bodies.Length; i++)
            {
                report.AppendLine($"{bodies[i].name}: rawMinPitch={rawMin[i]:F2} correctedMinPitch={fixedMin[i]:F2} maxNeckStep={jumps[i]:F4}m");
                if (jumps[i] > .06f) throw new Exception("Pose discontinuity " + bodies[i].name + " " + jumps[i]);
            }
            report.AppendLine("PASS six rigs, full action and baseline return; rear pelvis travel limited; backward torso lean <=4 degrees; no residual correction. Chair contact still requires image review.");
            File.WriteAllText("Temp/RehearSeatAdjustChecks.txt", report.ToString());
        }
        finally { if (root) Object.DestroyImmediate(root); if (chairs) Object.DestroyImmediate(chairs); EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void Capture(AudienceAnimationPlayer body, string suffix)
    {
        foreach (var skin in body.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<SkinnedMeshRenderer>()))
        { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
        var hip = body.GetComponentsInChildren<Transform>().Single(t => t.name == "pelvis");
        var center = hip.position + Vector3.up * .2f;
        var go = new GameObject("Seat adjust inspection camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, body.gameObject.scene);
        var camera = go.AddComponent<Camera>(); camera.scene = go.scene;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.gray;
        camera.transform.position = center + body.transform.right * 1.7f + body.transform.forward * .65f;
        camera.transform.LookAt(center); camera.nearClipPlane = .01f;
        camera.orthographic = true; camera.orthographicSize = .65f;
        var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
        var rt = new RenderTexture(1000, 1000, 24); var old = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0); tex.Apply();
            File.WriteAllBytes("Temp/RehearSeatAdjust-" + suffix + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        }
        finally { RenderTexture.active = old; camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); }
    }
}
