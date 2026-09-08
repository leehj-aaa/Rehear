using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Explicit, tutorial-only finishing pass. Does not touch lighting, furniture or character materials.
[InitializeOnLoad]
internal static class RehearTutorialFinishingSetup
{
    static RehearTutorialFinishingSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearTutorialFinishing.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialFinishing.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open the tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var controller = all.Select(t => t.GetComponent<PresentationController>()).Single(c => c);
        var guide = all.Select(t => t.GetComponent<TutorialControlGuideView>()).Single(c => c);
        var camera = all.Select(t => t.GetComponent<Camera>()).Single(c => c && c.CompareTag("MainCamera"));
        var timer = controller.timerText;
        var font = RehearPretendardFontBake.Font("SemiBold");
        if (!timer || !font) throw new Exception("Missing timer location or Pretendard font.");
        Undo.IncrementCurrentGroup();
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Finish tutorial signage and gentle vignette");
        var signTransform = timer.transform.parent.Find("TutorialModeLabel");
        TMP_Text sign;
        if (!signTransform)
        {
            // Retain the old visible timer's centre (its RectTransform has unusual
            // margins), but leave its object and runtime timer reference untouched.
            timer.ForceMeshUpdate(true);
            var center = timer.transform.TransformPoint(timer.textBounds.center);
            var go = new GameObject("TutorialModeLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, "Create tutorial mode label");
            go.layer = timer.gameObject.layer;
            go.transform.SetParent(timer.transform.parent, false);
            go.transform.SetPositionAndRotation(center, timer.transform.rotation);
            sign = go.GetComponent<TextMeshProUGUI>();
            sign.text = "발표 준비하기";
            sign.rectTransform.sizeDelta = new Vector2(160, 40);
            sign.rectTransform.localScale = timer.rectTransform.localScale;
        }
        else sign = signTransform.GetComponent<TMP_Text>();
        Undo.RecordObjects(new UnityEngine.Object[] { sign, sign.gameObject, guide.continueLabel, timer.gameObject }, "Update tutorial text");
        timer.gameObject.SetActive(false);
        sign.gameObject.SetActive(true);
        sign.font = font;
        sign.fontSharedMaterial = font.material;
        sign.fontStyle = FontStyles.Normal;
        sign.fontSize = 24;
        sign.enableAutoSizing = false;
        sign.alignment = TextAlignmentOptions.Center;
        sign.textWrappingMode = TextWrappingModes.NoWrap;
        sign.margin = Vector4.zero;
        sign.color = Color.white;
        sign.raycastTarget = false;
        guide.continueLabel.text = "직접 해보기";
        sign.ForceMeshUpdate(true);
        guide.continueLabel.ForceMeshUpdate(true);
        var presentation = all.Select(t => t.GetComponent<PresentationManager>()).Single(c => c);
        var hints = presentation.deskScreen.transform.Find("SlideDirectionHints");
        Undo.RecordObjects(new UnityEngine.Object[] { hints.Find("Arrow_Left").gameObject, hints.Find("Arrow_Right").gameObject }, "Update slide arrow availability");
        presentation.RefreshSlideDirectionHints();

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Tutorial Quest3 Volume.asset");
        if (!profile || !profile.TryGet<Vignette>(out var vignette)) throw new Exception("Missing tutorial Vignette.");
        var volume = all.Select(t => t.GetComponent<Volume>()).Single(v => v && v.sharedProfile == profile);
        var data = camera.GetUniversalAdditionalCameraData();
        Undo.RecordObjects(new UnityEngine.Object[] { vignette, data, volume }, "Gentle tutorial vignette");
        vignette.active = true;
        vignette.intensity.Override(.078f);
        vignette.smoothness.Override(.55f);
        vignette.center.Override(new Vector2(.5f, .5f));
        vignette.color.Override(Color.black);
        vignette.rounded.Override(false);
        data.renderPostProcessing = true;
        data.volumeLayerMask |= 1 << volume.gameObject.layer;
        volume.enabled = true;
        volume.isGlobal = true;
        volume.weight = 1;
        EditorUtility.SetDirty(vignette);
        Canvas.ForceUpdateCanvases();
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = sign.gameObject;
        if (SceneView.lastActiveSceneView)
            SceneView.lastActiveSceneView.LookAt(camera.transform.position + camera.transform.forward * 3,
                camera.transform.rotation, 3);
        File.WriteAllText("Temp/RehearTutorialFinishing.txt", "PASS\nbutton=" + guide.continueLabel.text +
            "\nsign=" + sign.text + "\nsignVisible=" + sign.isActiveAndEnabled + "\nsignFont=" + sign.font.name +
            "\nsignWorld=" + sign.transform.position.ToString("F3") + "\nsignMeshBounds=" + sign.textBounds +
            "\ntimerReferencePreserved=" + (controller.timerText == timer) +
            "\nvignetteIntensity=" + vignette.intensity.value + "\nvignetteSmoothness=" + vignette.smoothness.value +
            "\nmainCameraPost=" + data.renderPostProcessing + "\nvolumeMask=" + data.volumeLayerMask.value + "\nsaved=true");
    }
}
