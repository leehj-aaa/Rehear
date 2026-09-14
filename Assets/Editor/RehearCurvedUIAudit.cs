using System;
using System.IO;
using System.Linq;
using System.Text;
using CurvedUI;
using CurvedUI.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

[InitializeOnLoad]
internal static class RehearCurvedUIAudit
{
    static RehearCurvedUIAudit() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearCurvedUIAudit.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            var report = new StringBuilder();
            if (command == "signage" || command == "signage-size-220")
            {
                var controller = UnityEngine.Object.FindFirstObjectByType<PresentationController>();
                if (command == "signage-size-220")
                {
                    var settings = new SerializedObject(controller);
                    settings.FindProperty("startPromptFontSize").floatValue = 220f;
                    settings.ApplyModifiedPropertiesWithoutUndo();
                }
                var method = typeof(PresentationController).GetMethod("UpdateTimerDisplay", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var temporary = new GameObject("Signage validation");
                temporary.SetActive(false);
                try
                {
                    var test = temporary.AddComponent<PresentationController>();
                    var labelObject = new GameObject("Label", typeof(RectTransform));
                    labelObject.transform.SetParent(temporary.transform);
                    test.timerText = labelObject.AddComponent<TMPro.TextMeshProUGUI>();
                    test.qaManager = temporary.AddComponent<QuestionAnswerManager>();
                    method.Invoke(test, null);
                    if (test.timerText.text != "발표 시작하기") throw new Exception("Waiting label failed");
                    typeof(PresentationController).GetField("hasStarted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(test, true);
                    method.Invoke(test, null);
                    if (test.timerText.text != "01:00") throw new Exception("Timer label failed: " + test.timerText.text);
                    typeof(QuestionAnswerManager).GetProperty("IsQAPhaseActive").GetSetMethod(true).Invoke(test.qaManager, new object[] { true });
                    method.Invoke(test, null);
                    if (test.timerText.text != "Q&A") throw new Exception("QA label failed");
                }
                finally { UnityEngine.Object.DestroyImmediate(temporary); }
                method.Invoke(controller, null);
                controller.timerText.ForceMeshUpdate();
                if (controller.timerText.isTextOverflowing) throw new Exception("Wall label overflows its rectangle.");
                report.AppendLine($"Wall label size={controller.timerText.fontSize}, max={controller.timerText.fontSizeMax}, lines={controller.timerText.textInfo.lineCount}");
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(controller.timerText);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(controller.gameObject.scene);
                report.AppendLine("PASS: waiting / running / Q&A labels; current waiting label saved.");
            }
            foreach (var b in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b && (b.GetType().Name.Contains("ControllerInputActionManager") || b.GetType().Name == "OVRCameraRig"))
                    report.AppendLine($"Rig {b.name}: {b.GetType().AssemblyQualifiedName}");
            if (command == "fix")
            {
                if (EditorApplication.isPlaying) throw new Exception("Edit mode required.");
                typeof(RehearCurvedUISetup).GetMethod("ConfigureEventSystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, null);
                var input = UnityEngine.Object.FindFirstObjectByType<CurvedUIInputModule>();
                var serializedInput = new SerializedObject(input);
                serializedInput.FindProperty("mainEventCamera").objectReferenceValue = Camera.main;
                serializedInput.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(input);
                var xr = new SerializedObject(input).FindProperty("unityXRControlMethod");
                foreach (var name in new[] { "rightController", "leftController", "rightClickActionReference", "leftClickActionReference" })
                    if (!xr.FindPropertyRelative(name).objectReferenceValue) throw new Exception("Missing " + name);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(input.gameObject.scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(input.gameObject.scene);
                report.AppendLine("PASS: Unity XR controllers, UI Press actions and event camera assigned; scene saved.");
            }
            report.AppendLine("Selection: " + Selection.activeGameObject);
            foreach (var e in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.AppendLine($"EventSystem {e.name}: {e.GetType()} active={e.isActiveAndEnabled}");
            foreach (var m in UnityEngine.Object.FindObjectsByType<CurvedUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                report.AppendLine($"Input {m.name}: active={m.isActiveAndEnabled} camera={m.EventCamera} mask={m.RaycastLayerMask.value} settings={EditorJsonUtility.ToJson(m)}");
            foreach (var c in UnityEngine.Object.FindObjectsByType<CurvedUISettings>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.gameObject.scene.IsValid()) continue;
                var canvas = c.GetComponent<Canvas>();
                report.AppendLine($"Canvas {c.name} active={c.isActiveAndEnabled} layer={c.gameObject.layer} angle={c.Angle} interact={c.Interactable} camera={canvas.worldCamera} parentCanvas={c.transform.parent?.GetComponentInParent<Canvas>()}");
                foreach (var s in c.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
                {
                    var p = c.transform.InverseTransformPoint(s.transform.position);
                    if (Mathf.Abs(p.z) > .001f || !((RectTransform)c.transform).rect.Contains(p))
                        report.AppendLine($"  INVALID selectable {s.name}: {p}");
                }
            }
            File.WriteAllText("Temp/RehearCurvedUIAudit.txt", report.ToString());
        }
        catch (Exception e) { File.WriteAllText("Temp/RehearCurvedUIAudit.txt", e.ToString()); }
    }
}
