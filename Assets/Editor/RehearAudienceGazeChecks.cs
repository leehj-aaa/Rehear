using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearAudienceGazeChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Request = "Temp/RehearAudienceGazeChecks.request";
    const string Report = "Temp/AudienceGazeChecks.txt";
    static RehearAudienceGazeChecks() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(Request);
        File.WriteAllText(Report, "Actual presentation prefabs and animation graphs; state-dependent gaze checks.\n");
        var scene = EditorSceneManager.NewPreviewScene();
        var randomState = UnityEngine.Random.state;
        try
        {
            foreach (string name in new[] { "Aud_M_01_MY2_SmartCasual", "Aud_M_02_MU2_Casual", "Aud_M_03_MU2_SmartCasual", "Aud_W_01_FT2_Business", "Aud_W_02_FK2_Business", "Aud_W_03_FF2_Casual" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/03_Prefabs/Audience/Presentation/{name}.prefab");
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var body = actor.GetComponent<AudienceAnimationPlayer>();
                var gaze = actor.GetComponent<AudienceGazeController>();
                var head = (Transform)typeof(AudienceGazeController).GetField("headBone", Private).GetValue(gaze);
                var forward = (Transform)typeof(AudienceGazeController).GetField("gazeForwardReference", Private).GetValue(gaze);
                foreach (var animator in actor.GetComponentsInChildren<Animator>()) { animator.enabled = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
                if (!(bool)typeof(AudienceAnimationPlayer).GetMethod("CreateAnimationGraph", Private).Invoke(body, null)) throw new Exception(name + ": no animation graph");
                var graph = (PlayableGraph)typeof(AudienceAnimationPlayer).GetField("playableGraph", Private).GetValue(body);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    graph.Evaluate(0);
                    var presenter = new GameObject("Presenter check").transform;
                    var slide = new GameObject("Slide check").transform;
                    SceneManager.MoveGameObjectToScene(presenter.gameObject, scene);
                    SceneManager.MoveGameObjectToScene(slide.gameObject, scene);
                    // Anatomical eye bones independently validate the authored face direction.
                    Transform eyes = null;
                    foreach (var t in actor.GetComponentsInChildren<Transform>()) if (t.name == "eyes") eyes = t;
                    Vector3 face = eyes ? Vector3.ProjectOnPlane(eyes.forward, actor.transform.up).normalized : actor.transform.forward;
                    if (face.sqrMagnitude < .5f) face = actor.transform.forward;
                    float axisError = Vector3.Angle(Vector3.ProjectOnPlane(forward.forward, actor.transform.up), face);
                    Require(axisError < 15f, name + " face axis " + axisError);
                    File.AppendAllText(Report, $"PASS {name} face axis error={axisError:F1}\n");
                    presenter.position = head.position + Quaternion.AngleAxis(20f, actor.transform.up) * face * 3f;
                    slide.position = head.position + Quaternion.AngleAxis(-25f, actor.transform.up) * face * 3f;
                    gaze.ConfigureTargets(presenter, slide, new[] { slide });
                    gaze.ResetManualGaze();
                    Check(gaze, graph, head, forward, presenter, name, "presenter", 1, 1, true);
                    Check(gaze, graph, head, forward, presenter, name, "presenter", .2f, 1, false);
                    Check(gaze, graph, head, forward, presenter, name, "presenter", 1, .2f, false);
                    Check(gaze, graph, head, forward, slide, name, "slide", 1, 1, true);
                    Check(gaze, graph, head, forward, slide, name, "off_target", 1, 1, false);
                    ((Rehear.Evc.Audience.IAudienceQuestionWaitReceiver)gaze).SetQuestionGenerationWaiting(true);
                    Check(gaze, graph, head, forward, presenter, name, "off_target", .2f, .2f, true);
                    Check(gaze, graph, head, forward, presenter, name, "slide", .2f, .2f, true);
                    gaze.SetQuestionGenerationWaiting(false);
                    Check(gaze, graph, head, forward, presenter, name, "presenter", .2f, .2f, false);
                    // Nodding remains visible while the gaze controller keeps the
                    // audience generally oriented toward the presenter.
                    gaze.ApplyAudienceState(1f, 1f);
                    gaze.ApplyServerGaze("presenter", 10f, 0f);
                    Require(body.PlayServerVariation("CT_01.comprehension_nod", 4f, 1f), name + " nod command rejected");
                    var advanceBody = typeof(AudienceAnimationPlayer).GetMethod("Advance", Private);
                    for (int frame = 0; frame < 120; frame++)
                    {
                        advanceBody.Invoke(body, new object[] { 1f / 60f });
                        graph.Evaluate(1f / 60f);
                        gaze.ApplyGaze(1f / 60f, frame / 60f);
                    }
                    float nodTargetError = Vector3.Angle(forward.forward, presenter.position - forward.position);
                    Require(body.HeadNodWeight > .9f, name + " nod did not reach full weight");
                    Require(gaze.CurrentGazeWeight > .45f && gaze.CurrentGazeWeight < .65f,
                        name + " nod gaze blend " + gaze.CurrentGazeWeight);
                    Require(nodTargetError < 20f, name + " looked away while nodding: " + nodTargetError);
                    File.AppendAllText(Report, $"PASS {name} presenter+nodding weight={gaze.CurrentGazeWeight:F2} targetError={nodTargetError:F1}\n");
                    body.StopAction();
                    // Exercise the actual phase wiring, including the wait flag clearing
                    // when questions become ready and restoring normal gaze afterwards.
                    var coordinatorObject = new GameObject("Q&A coordinator check");
                    SceneManager.MoveGameObjectToScene(coordinatorObject, scene);
                    var coordinator = coordinatorObject.AddComponent<Rehear.Evc.Audience.AudienceReactionCoordinator>();
                    coordinator.ConfigureAgents(new[] { actor.GetComponent<Rehear.Evc.Audience.AudienceAgent>() });
                    coordinator.EnterQuestionPhase();
                    coordinator.SetQuestionGenerationWaiting(false);
                    Check(gaze, graph, head, forward, presenter, name, "off_target", .2f, .2f, true);
                    Check(gaze, graph, head, forward, presenter, name, "slide", 1f, .2f, true);
                    coordinator.SetServerMode(false);
                    Check(gaze, graph, head, forward, presenter, name, "presenter", .2f, .2f, false);
                    coordinator.EnterQuestionPhase();
                    // Runtime-only MonoBehaviours do not receive OnDisable in
                    // an edit-mode preview, so exercise that callback explicitly.
                    typeof(Rehear.Evc.Audience.AudienceReactionCoordinator).GetMethod("OnDisable", Private).Invoke(coordinator, null);
                    Check(gaze, graph, head, forward, presenter, name, "presenter", .2f, .2f, false);
                    Object.DestroyImmediate(coordinatorObject);
                    Object.DestroyImmediate(presenter.gameObject); Object.DestroyImmediate(slide.gameObject);
                }
                finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(actor); }
            }
            File.AppendAllText(Report, "PASS: 6 face axes; 72 gaze scenarios plus presenter-facing nod preservation, including Q&A focus, wait completion, phase release, disable cleanup, low E/C and animation release.\n");
        }
        catch (Exception error) { File.AppendAllText(Report, "FAIL: " + error + "\n"); }
        finally { EditorSceneManager.ClosePreviewScene(scene); UnityEngine.Random.state = randomState; }
    }
    static void Check(AudienceGazeController gaze, PlayableGraph graph, Transform head, Transform forward, Transform target, string name, string action, float engagement, float clarity, bool tracking)
    {
        gaze.ApplyAudienceState(engagement, clarity);
        gaze.ApplyServerGaze(action, 10, 0);
        float rotationChange = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            graph.Evaluate(1f / 60f);
            var animated = head.rotation;
            gaze.ApplyGaze(1f / 60f, frame / 60f);
            rotationChange = Quaternion.Angle(animated, head.rotation);
        }
        float error = Vector3.Angle(forward.forward, target.position - forward.position);
        if (tracking) Require(gaze.CurrentGazeWeight > .9f && error < 10, name + " " + action + " tracking error " + error);
        else Require(gaze.CurrentGazeWeight == 0f && rotationChange < .01f, name + " animation was overridden");
        File.AppendAllText(Report, $"PASS {name} {action} E={engagement:F1} C={clarity:F1} weight={gaze.CurrentGazeWeight:F2} targetError={error:F1} appliedRotation={rotationChange:F2}\n");
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
