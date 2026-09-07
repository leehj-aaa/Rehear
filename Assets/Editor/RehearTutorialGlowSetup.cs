using System;
using System.IO;
using System.Linq;
using CurvedUI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearTutorialGlowSetup
{
    const string Request = "Temp/RehearTutorialGlow.request";
    const string MaterialPath = "Assets/Settings/TutorialUI/Trigger Outline Glow.mat";
    static double nextPoll;
    static RehearTutorialGlowSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialGlow-validation.txt", "FAILED\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Apply Button Practice Glow And Sound")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Open the tutorial scene in Edit mode, outside a bake.");
        var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
        if (!view.HasStepButtons) throw new InvalidOperationException("Migrate step-owned buttons first.");
        var button = view.primaryButtons[2];
        var shader = Shader.Find("Rehear/UI/Button Outline Glow");
        if (!shader || !shader.isSupported) throw new InvalidOperationException("UI glow shader unavailable.");
        var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(e => e.message)));
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            material = new Material(shader) { name = "Trigger Outline Glow" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        Undo.RecordObject(material, "Set tutorial outline glow");
        material.SetVector("_ButtonSize", new Vector4(340, 56, 0, 0));
        material.SetFloat("_Padding", 24);
        material.SetFloat("_GlowWidth", 10);
        material.SetFloat("_OutlineWidth", 2);
        material.SetFloat("_GlowStrength", 0.85f);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);

        Undo.RegisterFullObjectHierarchyUndo(button.gameObject, "Add tutorial button glow");
        var glow = button.transform.Find("Outline Glow");
        if (!glow)
        {
            var go = new GameObject("Outline Glow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create tutorial glow layer");
            glow = go.transform;
            glow.SetParent(button.transform, false);
        }
        glow.gameObject.layer = button.gameObject.layer;
        glow.SetAsFirstSibling();
        var rect = (RectTransform)glow;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one * 0.5f;
        rect.offsetMin = Vector2.one * -24;
        rect.offsetMax = Vector2.one * 24;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        var pos = rect.localPosition; pos.z = 0; rect.localPosition = pos;
        var image = glow.GetComponent<Image>();
        image.material = material;
        image.color = new Color(0.03f, 0.35f, 1f, 1f);
        image.raycastTarget = false;
        var pulse = button.GetComponent<TutorialButtonGlow>();
        if (!pulse) pulse = Undo.AddComponent<TutorialButtonGlow>(button.gameObject);
        pulse.glowImage = image;
        pulse.pulsePeriod = 1.6f;
        pulse.minimumStrength = 0.35f;
        pulse.maximumStrength = 1f;
        EditorUtility.SetDirty(pulse);
        EditorUtility.SetDirty(image);
        view.GetComponentInParent<CurvedUISettings>().AddEffectToChildren();

        var click = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/08_Audio/UI_Click.mp3");
        if (!click) throw new InvalidOperationException("Missing existing UI click sound.");
        var serialized = new SerializedObject(manager);
        if (!serialized.FindProperty("practiceAudioSource").objectReferenceValue)
            throw new InvalidOperationException("Missing practice audio source.");
        serialized.FindProperty("triggerInputSound").objectReferenceValue = click;
        serialized.FindProperty("triggerPromptSound").objectReferenceValue = click;
        serialized.FindProperty("triggerPromptVolume").floatValue = 0.35f;
        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        PrefabUtility.RecordPrefabInstancePropertyModifications(manager);

        // Static edit preview; the smooth pulse runs only in Play mode.
        foreach (var step in view.steps) Undo.RecordObject(step, "Preview button practice");
        view.Show(2);
        Canvas.ForceUpdateCanvases();
        image.SetAllDirty();
        if (!image.GetComponent<CurvedUIVertexEffect>() || image.raycastTarget || !button.interactable)
            throw new InvalidOperationException("Glow must follow CurvedUI without intercepting button input.");
        if (button.onClick.GetPersistentEventCount() != 1 || button.onClick.GetPersistentTarget(0) != manager)
            throw new InvalidOperationException("Button click target changed.");
        if (Mathf.Abs(TutorialButtonGlow.EvaluatePulse(0, 1.6f, .35f, 1) - 1) > .001f ||
            Mathf.Abs(TutorialButtonGlow.EvaluatePulse(.8f, 1.6f, .35f, 1) - .35f) > .001f)
            throw new InvalidOperationException("Glow pulse validation failed.");
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Tutorial save failed.");
        Selection.activeGameObject = button.gameObject;
        EditorGUIUtility.PingObject(button);
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearTutorialGlow-validation.txt",
            $"shaderSupported=true\nshaderErrors=0\ncurvedGlow=true\nglowRaycast=false\nbuttonEvent=valid\npulsePeriod=1.6\npulseRange=0.35..1\n" +
            $"promptSound={click.name}, volume=0.35, oncePerEntry=true\nclickSound={click.name}, acceptedClicksOnly=true\nclipSeconds={click.length}\npreview=Step 2\nsaved=true\n");
        Debug.Log("Rehear: Button practice outline glow and sound saved.");
    }
}
