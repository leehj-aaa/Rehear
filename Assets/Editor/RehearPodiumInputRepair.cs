using System;
using System.IO;
using System.Linq;
using CurvedUI;
using CurvedUI.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearPodiumInputRepair
{
    const string Request = "Temp/RehearPodiumInputRepair.request";
    static RehearPodiumInputRepair() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || AssetDatabase.IsAssetImportWorkerProcess()) return;
        File.Delete(Request);
        try
        {
            EditScene("Assets/01_Scene/Scene_02_Presentation.unity", scene => {
                var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
                var controls = all.Single(t => t.name == "Presentation Controls");
                foreach (var nested in controls.GetComponentsInChildren<Canvas>(true).Where(c => c.transform != controls))
                {
                    foreach (var caster in nested.GetComponents<BaseRaycaster>()) UnityEngine.Object.DestroyImmediate(caster);
                    UnityEngine.Object.DestroyImmediate(nested);
                }
                ConfigureCanvas(controls.GetComponent<Canvas>());
                var toggle = all.Select(t => t.GetComponent<PodiumScriptToggle>()).Single(t => t);
                ConfigureCanvas(toggle.GetComponent<Canvas>());
                if (!toggle.scriptPanel || !toggle.label || toggle.transform.IsChildOf(toggle.scriptPanel.transform))
                    throw new Exception("Script toggle must remain outside the hidden script panel.");
                bool wasVisible = toggle.scriptPanel.activeSelf;
                try {
                    toggle.ToggleScript();
                    if (toggle.scriptPanel.activeSelf == wasVisible) throw new Exception("Script toggle failed.");
                    toggle.ToggleScript();
                    if (toggle.scriptPanel.activeSelf != wasVisible) throw new Exception("Script restore failed.");
                }
                finally { toggle.scriptPanel.SetActive(wasVisible); toggle.Refresh(); }
                foreach (var button in controls.GetComponentsInChildren<Button>(true))
                    if (button.GetComponent<Canvas>() || button.GetComponentInParent<Canvas>() != controls.GetComponent<Canvas>())
                        throw new Exception("Presentation button still has a nested canvas.");
            });
            EditScene("Assets/01_Scene/Scene_00_5_Tutorial.unity", scene => {
                var button = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Button>(true))
                    .Single(b => b.name == "Btn_SkipTutorial");
                button.targetGraphic = button.GetComponentInChildren<TMP_Text>(true);
                if (!button.targetGraphic) throw new Exception("Skip label missing.");
                var colors = button.colors;
                colors.normalColor = colors.selectedColor = Color.white;
                colors.highlightedColor = new Color(.25f, .85f, 1f, 1f);
                colors.pressedColor = new Color(.1f, .5f, 1f, 1f);
                button.colors = colors;
                EditorUtility.SetDirty(button);
            });
            File.WriteAllText("Temp/RehearPodiumInputRepair.txt", "PASS: controls share one input canvas; script toggle off/on restored; skip hover targets visible text.");
            File.WriteAllText("Temp/RehearQuestBuild.pending", "build");
            File.Move("Temp/RehearQuestBuild.pending", "Temp/RehearQuestBuild.request");
        }
        catch (Exception e) { File.WriteAllText("Temp/RehearPodiumInputRepair.txt", "FAIL: " + e); Debug.LogException(e); }
    }
    static void ConfigureCanvas(Canvas canvas)
    {
        if (!canvas) throw new Exception("Podium canvas missing.");
        var curve = canvas.GetComponent<CurvedUISettings>() ?? canvas.gameObject.AddComponent<CurvedUISettings>();
        // CurvedUI uses one degree for near-flat surfaces with XR ray mapping.
        curve.Angle = 1;
        curve.Interactable = curve.BlocksRaycasts = true;
        curve.AddEffectToChildren();
        if (!canvas.GetComponent<CurvedUIRaycaster>()) canvas.gameObject.AddComponent<CurvedUIRaycaster>();
        foreach (var caster in canvas.GetComponents<BaseRaycaster>())
            caster.enabled = caster is CurvedUIRaycaster;
        EditorUtility.SetDirty(canvas);
        EditorUtility.SetDirty(curve);
    }
    static void EditScene(string path, Action<Scene> edit)
    {
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try { edit(scene); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }
}
