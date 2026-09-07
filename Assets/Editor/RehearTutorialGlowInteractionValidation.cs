using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Isolated edit-mode regression checks: never change the open scene or start XR.
[InitializeOnLoad]
internal static class RehearTutorialGlowInteractionValidation
{
    const string Request = "Temp/RehearGlowInteractionTest.request";
    const string Report = "Temp/RehearGlowInteractionTest.txt";
    static double nextPoll;
    static RehearTutorialGlowInteractionValidation() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Validate(); }
        catch (Exception e) { File.WriteAllText(Report, "FAILED\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Validate Tutorial Glow Interaction")]
    static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the isolated checks in Edit mode.");
        var results = new List<string>();
        var go = new GameObject("Temporary Glow Interaction Test", typeof(RectTransform));
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var button = go.AddComponent<Button>();
            var glow = go.AddComponent<TutorialButtonGlow>();
            var method = typeof(TutorialButtonGlow).GetMethod("EvaluateStrength", BindingFlags.Instance | BindingFlags.NonPublic);
            var evaluate = (Func<float, float>)Delegate.CreateDelegate(typeof(Func<float, float>), glow, method);
            // Non-ExecuteAlways behaviours don't receive lifecycle callbacks in Edit mode.
            var enable = (Action)Delegate.CreateDelegate(typeof(Action), glow,
                typeof(TutorialButtonGlow).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic));
            var disable = (Action)Delegate.CreateDelegate(typeof(Action), glow,
                typeof(TutorialButtonGlow).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic));
            enable();
            var first = new PointerEventData(null) { pointerId = 1 };
            var second = new PointerEventData(null) { pointerId = 2 };
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                results.Add("PASS " + name);
            }

            Check(evaluate(0) > .9f && evaluate(.8f) < .4f, "idle cue pulses");
            glow.OnPointerEnter(first);
            Check(evaluate(1) == 0 && evaluate(2) == 0, "hover suppresses pulse");
            glow.OnPointerDown(first);
            Check(evaluate(3) == 0, "press suppresses pulse");
            glow.NotifyAcceptedClick();
            glow.OnPointerUp(first);
            Check(evaluate(Time.unscaledTime + 10) == 0, "first click stays suppressed while hovered");
            glow.NotifyAcceptedClick();
            Check(evaluate(Time.unscaledTime + 11) == 0, "second click stays suppressed while hovered");
            glow.OnPointerEnter(second);
            glow.OnPointerExit(first);
            Check(evaluate(20) == 0, "remaining pointer keeps cue suppressed");
            glow.OnPointerDown(second);
            glow.OnPointerExit(second);
            Check(evaluate(21) == 0, "dragging outside while pressed stays suppressed");
            glow.OnPointerUp(second);
            float resume = Time.unscaledTime + 30;
            Check(evaluate(resume) == 0 && evaluate(resume + .2f) == 0, "exit waits before resuming");
            Check(evaluate(resume + .36f) > 0 && evaluate(resume + 1.15f) > .9f, "cue resumes from dim phase");

            disable();
            enable();
            glow.NotifyAcceptedClick();
            Check(evaluate(Time.unscaledTime + .1f) == 0, "accepted click without hover has cooldown");
            Check(evaluate(Time.unscaledTime + .5f) > 0, "accepted click cooldown ends");
            button.interactable = false;
            Check(evaluate(resume + 40) == 0, "disabled button does not prompt");
            button.interactable = true;
            button.enabled = false;
            Check(evaluate(resume + 41) == 0, "disabled button component does not prompt");
            button.enabled = true;
            glow.OnPointerEnter(first);
            disable();
            enable();
            Check(evaluate(0) > 0, "step hide and re-enable clear pointer state");
            var rhythm = new float[400];
            for (int i = 10; i < 20; i++) rhythm[i] = 0.8f;
            for (int i = 100; i < 110; i++) rhythm[i] = 0.4f;
            var envelope = TutorialButtonGlow.BuildEnvelope(rhythm, 1, 1000);
            Check(TutorialButtonGlow.SampleEnvelope(envelope, 0) == 0, "audio silence produces no glow");
            Check(TutorialButtonGlow.SampleEnvelope(envelope, .01f) > .99f, "strong audio hit produces glow peak");
            float quietHit = TutorialButtonGlow.SampleEnvelope(envelope, .1f);
            Check(quietHit > 0 && quietHit < .6f, "weaker audio hit produces dimmer glow");
            Check(TutorialButtonGlow.SampleEnvelope(envelope, .25f) == 0, "gap between audio hits stays dark");
            File.WriteAllText(Report, string.Join("\n", results) + "\nsceneUnchanged=true\nquestDeviceTested=false\n");
            Debug.Log($"Rehear: {results.Count} isolated glow interaction checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
