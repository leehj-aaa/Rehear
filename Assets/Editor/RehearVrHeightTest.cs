using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using Unity.XR.CoreUtils;
using Object = UnityEngine.Object;

[InitializeOnLoad]
internal static class RehearVrHeightTest
{
    const string Key = "Rehear.VrHeightTest";
    const string Report = "Temp/VRHeightTest/report.txt";
    static double next;
    static RehearVrHeightTest() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (File.Exists("Temp/RehearVrHeightTest.request"))
        {
            bool stop = File.ReadAllText("Temp/RehearVrHeightTest.request").Trim() == "stop";
            File.Delete("Temp/RehearVrHeightTest.request");
            if (stop)
            {
                SessionState.SetString(Key, "");
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                return;
            }
            Directory.CreateDirectory("Temp/VRHeightTest");
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            { File.WriteAllText(Report, "BLOCKED: stop playback or save the open scene first."); return; }
            EditorSceneManager.OpenScene("Assets/01_Scene/Scene_02_Presentation.unity");
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            File.WriteAllText(Report, "Two playback runs; validate seated eye offset, gravity, grounding, tracking and XR display.\n");
            SessionState.SetInt(Key + "run", 1);
            SessionState.SetString(Key, "enter");
            EditorApplication.EnterPlaymode();
        }
        var phase = SessionState.GetString(Key, "");
        if (phase == "") return;
        if (phase == "enter" && EditorApplication.isPlaying)
        {
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetFloat(Key + "start", (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(Key + "samples", 0);
            SessionState.SetInt(Key + "failures", 0);
            SessionState.SetString(Key, "sample");
        }
        if (phase == "sample" && EditorApplication.isPlaying && EditorApplication.timeSinceStartup >= next)
        {
            next = EditorApplication.timeSinceStartup + 1;
            double elapsed = EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "start", 0);
            if (elapsed < 8) return;
            var origin = Object.FindFirstObjectByType<XROrigin>();
            var displays = new List<XRDisplaySubsystem>(); SubsystemManager.GetSubsystems(displays);
            bool display = displays.Exists(d => d.running && d.GetRenderPassCount() > 0);
            var hmd = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            bool tracked = hmd.TryGetFeatureValue(CommonUsages.isTracked, out bool tracking) && tracking;
            foreach (var gaze in Object.FindObjectsByType<AudienceGazeController>(FindObjectsSortMode.None))
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var type = typeof(AudienceGazeController);
                var target = type.GetField("currentTarget", flags).GetValue(gaze) as Transform;
                var forward = type.GetField("gazeForwardReference", flags).GetValue(gaze) as Transform;
                var state = type.GetField("currentState", flags).GetValue(gaze);
                var body = gaze.GetComponent<AudienceAnimationPlayer>();
                var presenter = type.GetField("presenterTarget", flags).GetValue(gaze) as Transform;
                File.AppendAllText(Report, $"GAZE {gaze.name} enabled={gaze.enabled} state={state} target={target?.name ?? "NULL"} presenter={presenter?.name ?? "NULL"} cameraTarget={target == origin?.Camera.transform} error={(target && forward ? Vector3.Angle(forward.forward, target.position-forward.position) : -1):F1} typing={body?.TypingLookTarget != null} phone={body?.DeviceLookTarget != null} conversation={body?.ConversationWeight:F2}\n");
            }
            if (SessionState.GetInt(Key + "samples", 0) == 0)
            {
                if ((!display || !tracked) && elapsed < 120) return;
                if (display && tracked)
                {
                    SessionState.SetFloat(Key + "start", (float)EditorApplication.timeSinceStartup - 8);
                    elapsed = 8;
                }
            }
            bool grounded = false, gravityOn = false;
            if (origin) foreach (var g in origin.GetComponentsInChildren<GravityProvider>()) { grounded |= g.isGrounded; gravityOn |= g.useGravity; }
            float floor = float.NaN, height = float.NaN, neutral = float.NaN;
            if (origin && origin.Camera)
            {
                foreach (var hit in Physics.RaycastAll(origin.Camera.transform.position, Vector3.down, 10))
                    if (hit.collider.name.StartsWith("Floor")) floor = hit.point.y;
                height = origin.Camera.transform.position.y - floor;
                neutral = height - origin.Camera.transform.localPosition.y;
            }
            bool pass = origin && origin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Device &&
                gravityOn && grounded && display && tracked && Mathf.Abs(neutral - 1.45f) < .08f;
            int run = SessionState.GetInt(Key + "run", 1);
            File.AppendAllText(Report, $"run={run} t={elapsed:F1}s {(pass ? "PASS" : "FAIL")} mode={origin?.CurrentTrackingOriginMode} gravity={gravityOn} grounded={grounded} display={display} tracked={tracked} eye={height:F3} neutralEye={neutral:F3} originY={origin?.transform.position.y:F3}\n");
            SessionState.SetInt(Key + "samples", SessionState.GetInt(Key + "samples", 0) + 1);
            if (!pass) SessionState.SetInt(Key + "failures", SessionState.GetInt(Key + "failures", 0) + 1);
            if (elapsed >= 25)
            {
                File.AppendAllText(Report, $"RUN {run} COMPLETE: samples={SessionState.GetInt(Key + "samples", 0)}, failures={SessionState.GetInt(Key + "failures", 0)}\n");
                SessionState.SetString(Key, "exit");
                EditorApplication.ExitPlaymode();
            }
        }
        if (phase == "exit" && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            int run = SessionState.GetInt(Key + "run", 1);
            if (run >= 2) { SessionState.SetString(Key, ""); File.AppendAllText(Report, "TEST COMPLETE\n"); }
            else { SessionState.SetInt(Key + "run", run + 1); SessionState.SetString(Key, "enter"); EditorApplication.EnterPlaymode(); }
        }
    }
}
