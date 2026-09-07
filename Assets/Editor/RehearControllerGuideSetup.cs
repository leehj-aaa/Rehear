using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearControllerGuideSetup
{
    const string Folder = "Assets/Settings/TutorialUI/ControllerGuidance";
    static RehearControllerGuideSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        const string request = "Temp/RehearControllerGuideSetup.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearControllerGuideSetup.txt", "FAIL\n" + e); Debug.LogException(e); }
    }
    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new Exception("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var manager = all.Select(t => t.GetComponent<TutorialManager>()).Single(v => v);
        var view = all.Select(t => t.GetComponent<TutorialFigmaView>()).Single(v => v);
        var visual = all.Select(t => t.GetComponent<Quest3TutorialControllerVisual>()).Single(v => v);
        var right = all.Single(t => t.name == "Right Controller");
        var original = right.Find("Right Controller Visual");
        if (!original) throw new Exception("Right controller visual missing.");
        if (all.Any(t => t.GetComponent<PresentationInputController>() is { enabled: true }))
            throw new Exception("Disable duplicate presentation inputs in the tutorial first.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Settings/TutorialUI", "ControllerGuidance");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("deed68a7fd2e9234899298cf80fae7ad"));
        var body = model.transform.Find("oculus_controller_r_MeshX").GetComponent<SkinnedMeshRenderer>();
        var trigger = Highlight(body, "right_b_trigger_front", "Trigger Highlight");
        var stick = Highlight(body, "right_b_thumbstick", "Stick Highlight");
        var grip = Highlight(body, "right_b_trigger_grip", "Grip Highlight");
        var highlightPath = Folder + "/Controller Button Blue.mat";
        var highlight = AssetDatabase.LoadAssetAtPath<Material>(highlightPath);
        if (!highlight)
        {
            highlight = new Material(Shader.Find("Rehear/Controller Button Glow")) { name = "Controller Button Blue" };
            highlight.SetColor("_BaseColor", new Color(.025f, .10f, .32f));
            highlight.SetColor("_EmissionColor", new Color(.005f, .20f, 1f) * 1.2f);
            AssetDatabase.CreateAsset(highlight, highlightPath);
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add controller demonstrations before practice");
        Undo.RecordObjects(new UnityEngine.Object[] { manager, visual }, "Connect controller guides");
        var vs = new SerializedObject(visual);
        vs.FindProperty("rightHandTarget").objectReferenceValue = right;
        var renderers = original.GetComponentsInChildren<Renderer>(true);
        var array = vs.FindProperty("originalControllerRenderers");
        array.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        vs.FindProperty("gripPoseOffset").vector3Value = Vector3.zero;
        vs.FindProperty("gripPoseEuler").vector3Value = Vector3.zero;
        vs.FindProperty("triggerHighlight").objectReferenceValue = trigger;
        vs.FindProperty("stickHighlight").objectReferenceValue = stick;
        vs.FindProperty("gripHighlight").objectReferenceValue = grip;
        vs.FindProperty("highlightMaterial").objectReferenceValue = highlight;
        vs.ApplyModifiedProperties();

        var existing = view.transform.Find("Control Demonstration");
        var guide = existing ? existing.GetComponent<TutorialControlGuideView>() : null;
        if (!guide)
        {
            var go = UnityEngine.Object.Instantiate(view.steps[1], view.transform);
            go.name = "Control Demonstration";
            Undo.RegisterCreatedObjectUndo(go, "Create controller guide panel");
            guide = go.AddComponent<TutorialControlGuideView>();
            var panel = go.GetComponentInChildren<TranslucentImage>(true);
            var rect = (RectTransform)panel.transform;
            TutorialFigmaView.Place(rect, 198, 278, 804, 310);
            var mat = new Material(panel.material) { name = "Controller Demonstration Glass" };
            mat.SetVector("_PanelSize", new Vector4(804, 310, 0, 0));
            AssetDatabase.CreateAsset(mat, Folder + "/Controller Demonstration Glass.mat");
            panel.material = mat;
            guide.title = panel.transform.Find("Title").GetComponent<TMP_Text>();
            guide.description = panel.transform.Find("Description").GetComponent<TMP_Text>();
            TutorialFigmaView.Place(guide.title.rectTransform, 33, 40, 738, 48);
            TutorialFigmaView.Place(guide.description.rectTransform, 25, 104, 754, 78);
            guide.description.fontSize = 26;
            guide.description.enableAutoSizing = false;
            guide.continueButton = panel.GetComponentInChildren<Button>(true);
            guide.continueButton.name = "Btn_BeginPractice";
            TutorialFigmaView.Place((RectTransform)guide.continueButton.transform, 182, 218, 440, 56);
            var buttonImage = guide.continueButton.GetComponent<Image>();
            var buttonMat = new Material(buttonImage.material) { name = "Controller Demonstration Button" };
            buttonMat.SetVector("_PanelSize", new Vector4(440, 56, 0, 0));
            buttonMat.SetFloat("_BorderWidth", 0);
            AssetDatabase.CreateAsset(buttonMat, Folder + "/Controller Demonstration Button.mat");
            buttonImage.material = buttonMat;
            var collider = guide.continueButton.GetComponent<BoxCollider>();
            if (collider) { collider.center = new Vector3(220, -28, 0); collider.size = new Vector3(440, 56, 1); }
            guide.continueLabel = guide.continueButton.GetComponentInChildren<TMP_Text>(true);
            guide.continueLabel.fontSize = 24;
            guide.continueButton.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(guide.continueButton.onClick, manager.OnPrimaryButtonPressed);
            go.GetComponentInParent<CurvedUISettings>().AddEffectToChildren();
        }
        var ms = new SerializedObject(manager);
        ms.FindProperty("controlGuide").objectReferenceValue = guide;
        ms.FindProperty("demonstrationDuration").floatValue = 2.7f;
        ms.ApplyModifiedProperties();
        Undo.RecordObjects(view.steps, "Preview trigger guide");
        if (view.deskDirectionHints) Undo.RecordObject(view.deskDirectionHints, "Preview guide");
        if (view.scriptDirectionHints) Undo.RecordObject(view.scriptDirectionHints, "Preview guide");
        view.Show(-1);
        guide.Show("검지로 트리거를 눌러요", "오른손 컨트롤러 앞쪽의 파란 버튼을 확인해 주세요.\n버튼을 가리킨 뒤 검지로 트리거를 눌러 선택해요.");
        guide.SetReady(true);
        foreach (var text in guide.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed.");
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = guide.gameObject;
        File.WriteAllText("Temp/RehearControllerGuideSetup.txt", "PASS\nprePracticeGuides=5\nrightHand=" + right.name +
            "\noriginalRenderers=" + renderers.Length + "\nhighlightTriangles=" + trigger.triangles.Length / 3 + "," + stick.triangles.Length / 3 + "," + grip.triangles.Length / 3 +
            "\nmodelImportedScale=" + model.transform.localScale + "\nselfPaced=true\nminimumSeconds=2.7\nsaved=true");
    }
    static Mesh Highlight(SkinnedMeshRenderer body, string boneName, string name)
    {
        string path = Folder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing) return existing;
        int index = Array.FindIndex(body.bones, b => b.name == boneName);
        if (index < 0) throw new Exception("Missing bone " + boneName);
        var source = body.sharedMesh;
        var weights = source.boneWeights;
        bool Selected(int i)
        {
            var w = weights[i];
            return (w.boneIndex0 == index ? w.weight0 : 0) + (w.boneIndex1 == index ? w.weight1 : 0) +
                (w.boneIndex2 == index ? w.weight2 : 0) + (w.boneIndex3 == index ? w.weight3 : 0) > .5f;
        }
        var triangles = source.triangles;
        var selected = new List<int>();
        for (int i = 0; i < triangles.Length; i += 3)
            if (Selected(triangles[i]) && Selected(triangles[i + 1]) && Selected(triangles[i + 2]))
                selected.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
        if (selected.Count == 0) throw new Exception("Empty highlight " + boneName);
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = name;
        mesh.subMeshCount = 1;
        mesh.SetTriangles(selected, 0);
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        // FBX is authored in centimetres; 0.03 units = a 0.3 mm overlay shell.
        for (int i = 0; i < vertices.Length; i++) vertices[i] += normals[i] * .03f;
        mesh.vertices = vertices;
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}
