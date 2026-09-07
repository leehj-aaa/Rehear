using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CurvedUI;
using LeTai.Asset.TranslucentImage;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One-time, explicit migration from shared controls to editable step-owned buttons.</summary>
[InitializeOnLoad]
internal static class RehearTutorialStepButtons
{
    const string Request = "Temp/RehearTutorialStepButtons.request";
    const string Report = "Temp/RehearTutorialStepButtons-validation.txt";
    const string Materials = "Assets/Settings/TutorialUI/";
    static double nextPoll;

    static RehearTutorialStepButtons() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { if (command == "button-copy") UpdateButtonInstruction(); else Apply(); }
        catch (Exception e) { File.WriteAllText(Report, "FAILED\n" + e); Debug.LogException(e); }
    }

    [MenuItem("Rehear/Update Button Practice Instruction")]
    static void UpdateButtonInstruction()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Open the tutorial scene in Edit mode, outside a bake.");
        var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
        var title = view.steps[2].transform.Find("Practice glass/Title").GetComponent<TMP_Text>();
        Undo.RecordObject(title, "Clarify button practice instruction");
        title.text = "아래 버튼을 선택해보세요";
        title.ForceMeshUpdate(true, true);
        EditorUtility.SetDirty(title);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Tutorial save failed.");
        Selection.activeGameObject = title.gameObject;
        EditorGUIUtility.PingObject(title);
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText("Temp/RehearTutorialButtonCopy.txt", $"title={title.text}\nsaved=true\n");
    }

    [MenuItem("Rehear/Repair Step-Owned Tutorial Buttons")]
    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning)
            throw new InvalidOperationException("Open the tutorial scene in Edit mode, outside a bake.");
        var view = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialFigmaView>(true)).Single();
        var manager = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
        Undo.RegisterFullObjectHierarchyUndo(view.gameObject, "Separate tutorial step buttons");
        Configure(view, manager);
        AlignCanvasPlane(view);
        // Complete this migration's tracking and coplanarity adjustments, including
        // a scene that was saved by the first pass before an assembly refresh.
        foreach (var button in view.primaryButtons.Concat(view.secondaryButtons).Where(b => b))
        {
            var position = button.transform.localPosition;
            position.z = 0;
            button.transform.localPosition = position;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            label.characterSpacing = -1;
            EditorUtility.SetDirty(label);
        }
        foreach (var label in view.steps[1].GetComponentsInChildren<TMP_Text>(true))
        {
            label.characterSpacing = -1;
            EditorUtility.SetDirty(label);
        }
        var report = Validate(view, manager);
        view.Show(1);
        Canvas.ForceUpdateCanvases();
        foreach (var text in view.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
        foreach (var graphic in view.GetComponentsInChildren<Graphic>(true)) graphic.SetAllDirty();
        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Tutorial save failed.");
        Selection.activeGameObject = view.steps[1].transform.Find("Practice glass").gameObject;
        EditorGUIUtility.PingObject(Selection.activeGameObject);
        SceneView.RepaintAll();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        File.WriteAllText(Report, report + "\npreview=Step 1\nsaved=true\n");
        Debug.Log("Rehear: Step-owned tutorial buttons verified and saved. Practice Intro is ready to edit.");
    }

    public static void Configure(TutorialFigmaView view, TutorialManager manager)
    {
        if (view.HasStepButtons) return; // Never overwrite subsequent designer edits.
        if (view.steps.Length != 9 || !view.primary || !view.secondary)
            throw new InvalidOperationException("Expected the existing nine-step Figma tutorial and both button templates.");
        var guide = view.steps[0].transform.Find("Controller guide glass");
        var intro = view.steps[1].transform.Find("Practice glass");
        var complete = view.steps[8].transform.Find("Complete glass");
        if (!guide || !intro || !complete) throw new InvalidOperationException("Tutorial panel missing.");

        var primary = view.primary.GetComponent<Button>();
        var secondary = view.secondary.GetComponent<Button>();
        // Clone existing controls so XR and uGUI events retain their verified manager target.
        var introButton = Clone(primary, intro, "Btn_StartPractice");
        var triggerButton = Clone(primary, view.steps[2].transform, "Btn_TriggerPractice");
        var completeButton = Clone(primary, complete, "Btn_StartSession");
        var replayButton = Clone(secondary, complete, "Btn_ReplayTutorial");
        Undo.SetTransformParent(primary.transform, guide, "Move guide start button inside its panel");
        Undo.SetTransformParent(secondary.transform, guide, "Move guide skip button inside its panel");
        primary.name = "Btn_StartTutorial";
        secondary.name = "Btn_SkipTutorial";

        SetButton(primary, 232, 707, 735, "튜토리얼 시작하기", "Primary", false, false);
        SetButton(secondary, 232, 775, 735, "건너뛰기", "Secondary", true, false);
        SetButton(introButton, 234, 188, 340, "시작하기", "Practice Primary", false, false);
        // The trigger exercise needs its own physical target, outside the instruction panel.
        SetButton(triggerButton, 430, 552, 340, "눌러보기", "Practice Primary", false, false);
        SetButton(completeButton, 407, 199, 340, "세션 시작하기", "Practice Primary", false, false);
        SetButton(replayButton, 55, 199, 340, "처음으로 돌아가기", "Complete Secondary", true, true);

        view.primaryButtons = new Button[9];
        view.secondaryButtons = new Button[9];
        view.primaryButtons[0] = primary;
        view.secondaryButtons[0] = secondary;
        view.primaryButtons[1] = introButton;
        view.primaryButtons[2] = triggerButton;
        view.primaryButtons[8] = completeButton;
        view.secondaryButtons[8] = replayButton;

        // Figma 2323:39465 / 2323:39467. All coordinates below are panel-local.
        TutorialFigmaView.Place((RectTransform)intro, 198, 322, 804, 282);
        SetIntroText(intro.Find("Title").GetComponent<TMP_Text>(), "기본 조작을 연습해볼까요?",
            35, 67, 738, 43, 36, "Bold", new Color32(3, 8, 18, 255));
        SetIntroText(intro.Find("Description").GetComponent<TMP_Text>(), "오른손 컨트롤러로 간단한 조작을 따라 해보세요",
            24.5f, 126, 754, 31, 26, "Medium", new Color32(53, 56, 65, 255));
        var glass = intro.GetComponent<TranslucentImage>();
        glass.foregroundOpacity = RehearBlurDiagnostics.IntroWhiteTint;
        glass.material.SetFloat("_GlassTint", RehearBlurDiagnostics.IntroWhiteTint);
        glass.material.SetVector("_PanelSize", new Vector4(804, 282, 0, 0));
        glass.material.SetFloat("_Radius", 48);
        glass.material.SetFloat("_BorderWidth", 2);
        EditorUtility.SetDirty(glass.material);
        EditorUtility.SetDirty(glass);
        view.GetComponentInParent<CurvedUISettings>().AddEffectToChildren();
        EditorUtility.SetDirty(view);
    }

    static void AlignCanvasPlane(TutorialFigmaView view)
    {
        var canvas = view.GetComponentInParent<Canvas>();
        Require(view.transform.parent == canvas.transform, "Expected tutorial content directly inside its canvas");
        if (Quaternion.Angle(view.transform.localRotation, Quaternion.identity) < .001f) return;
        // CurvedUI hit testing requires UI geometry to lie on the canvas plane.
        // Preserve the authored content's world pose; put its tilt on the canvas,
        // rather than throwing away the designer's chosen viewing direction.
        Undo.RecordObjects(new UnityEngine.Object[] { canvas.transform, view.transform }, "Align curved tutorial interaction plane");
        Vector3 worldPosition = view.transform.position;
        canvas.transform.rotation = view.transform.rotation;
        view.transform.localRotation = Quaternion.identity;
        canvas.transform.position += worldPosition - view.transform.position;
        EditorUtility.SetDirty(canvas.transform);
        EditorUtility.SetDirty(view.transform);
        canvas.GetComponent<CurvedUISettings>().AddEffectToChildren();
        Canvas.ForceUpdateCanvases();
    }

    static Button Clone(Button template, Transform parent, string name)
    {
        var clone = UnityEngine.Object.Instantiate(template.gameObject, parent, false);
        Undo.RegisterCreatedObjectUndo(clone, "Create step-owned tutorial button");
        clone.name = name;
        return clone.GetComponent<Button>();
    }

    static void SetButton(Button button, float x, float y, float width, string label, string key, bool transparent, bool border)
    {
        TutorialFigmaView.Place((RectTransform)button.transform, x, y, width, 56);
        var position = button.transform.localPosition;
        position.z = 0;
        button.transform.localPosition = position;
        button.gameObject.SetActive(true);
        var image = button.GetComponent<Image>();
        var path = Materials + key + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(image.material) { name = key };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetVector("_PanelSize", new Vector4(width, 56, 0, 0));
        material.SetFloat("_Radius", 40);
        material.SetFloat("_BorderWidth", border ? 2 : 0);
        material.SetFloat("_UseBlur", 0);
        image.material = material;
        image.color = transparent ? Color.clear : new Color32(0, 51, 255, 255);
        image.canvasRenderer.cullTransparentMesh = false;
        image.raycastTarget = true;
        var text = button.GetComponentInChildren<TMP_Text>(true);
        SetIntroText(text, label, 0, 0, width, 56, 22, "Medium", Color.white);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.pivot = new Vector2(.5f, .5f);
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(button);
        EditorUtility.SetDirty(image);
    }

    static void SetIntroText(TMP_Text text, string value, float x, float y, float w, float h, float size, string weight, Color color)
    {
        TutorialFigmaView.Place(text.rectTransform, x, y, w, h);
        text.text = value;
        text.font = RehearPretendardFontBake.Font(weight);
        if (!text.font) throw new InvalidOperationException("Missing baked Pretendard " + weight);
        text.fontSharedMaterial = text.font.material;
        text.fontSize = size;
        text.characterSpacing = -1; // Figma tracking: -1% (-0.36 / -0.26 / -0.22 px).
        text.enableAutoSizing = false;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.margin = Vector4.zero;
        text.color = color;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);
    }

    static string Validate(TutorialFigmaView view, TutorialManager manager)
    {
        var report = new StringBuilder("stepOwnedButtons=true\n");
        Require(view.HasStepButtons, "Step arrays missing");
        Require(!view.transform.Cast<Transform>().Any(t => t.GetComponent<Button>()), "Shared root button remains");
        for (int step = 0; step < 9; step++)
        {
            view.Show(step);
            Require(view.steps.Count(s => s.activeSelf) == 1, "More than one visible step");
            foreach (var pair in new[] { (view.primaryButtons[step], "OnPrimaryButtonPressed"), (view.secondaryButtons[step], "OnSecondaryButtonPressed") })
            {
                var button = pair.Item1;
                if (!button) continue;
                Require(button.transform.IsChildOf(view.steps[step].transform), "Button belongs to another step");
                var canvas = button.GetComponentInParent<Canvas>();
                float canvasZ = canvas.transform.InverseTransformPoint(button.transform.position).z;
                if (Mathf.Abs(canvasZ) >= .1f)
                {
                    var chain = "";
                    for (var t = button.transform; t && t != canvas.transform; t = t.parent)
                        chain += $" / {t.name}: pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale}";
                    throw new InvalidOperationException($"CurvedUI {button.name} canvas={canvas.name} z={canvasZ} chain={chain}");
                }
                Require(button.onClick.GetPersistentEventCount() == 1 && button.onClick.GetPersistentTarget(0) == manager &&
                    button.onClick.GetPersistentMethodName(0) == pair.Item2, "Button click target changed");
                var xr = button.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name == "XRSimpleInteractable");
                Require(xr, "XR control missing");
                var calls = new SerializedObject(xr).FindProperty("m_SelectEntered.m_PersistentCalls.m_Calls");
                Require(calls.arraySize == 1 && calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_Target").objectReferenceValue == manager &&
                    calls.GetArrayElementAtIndex(0).FindPropertyRelative("m_MethodName").stringValue == pair.Item2, "XR event target changed");
                report.AppendLine($"step={step} button={button.name} parent={button.transform.parent.name} uGUI+XR=valid");
            }
        }
        var intro = (RectTransform)view.steps[1].transform.Find("Practice glass");
        var start = (RectTransform)view.primaryButtons[1].transform;
        Require(intro.sizeDelta == new Vector2(804, 282) && start.sizeDelta == new Vector2(340, 56), "Figma size mismatch");
        Require(start.parent == intro && start.anchoredPosition == new Vector2(234, -188), "Figma button position mismatch");
        Require(Mathf.Approximately(intro.GetComponent<TranslucentImage>().foregroundOpacity, RehearBlurDiagnostics.IntroWhiteTint), "Practice glass tint mismatch");
        report.AppendLine($"introPanel=804x282 radius48 border2 tint{RehearBlurDiagnostics.IntroWhiteTint}\nintroButton=340x56 local234,188\nfont=Bold36/Medium26/Medium22");
        ValidateTransitions(report);
        return report.ToString();
    }

    static void ValidateTransitions(StringBuilder report)
    {
        // A non-active harness exercises the real manager without starting audio,
        // moving the XR rig, loading scenes or touching the user's live tutorial.
        var harness = new GameObject("Tutorial transition validation") { hideFlags = HideFlags.HideAndDontSave };
        harness.SetActive(false);
        try
        {
            var manager = harness.AddComponent<TutorialManager>();
            var field = typeof(TutorialManager).GetField("currentStep", BindingFlags.NonPublic | BindingFlags.Instance);
            var time = typeof(TutorialManager).GetField("lastButtonTime", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 5; i++)
            {
                time.SetValue(manager, -10f);
                manager.OnPrimaryButtonPressed();
                int expected = i == 0 ? 1 : i < 4 ? 2 : 3;
                Require(Convert.ToInt32(field.GetValue(manager)) == expected, "Tutorial transition failed at click " + i);
            }
            report.AppendLine("managerTransitions=Guide->Intro->Trigger->3 clicks->Slide PASS");
        }
        finally { UnityEngine.Object.DestroyImmediate(harness); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
