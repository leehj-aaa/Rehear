using System;
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
internal static class RehearFigmaTutorialSetup
{
    private const string Request = "Temp/RehearFigmaTutorial.request";
    private const string Report = "Temp/RehearFigmaTutorial-validation.txt";
    private const string Materials = "Assets/Settings/TutorialUI";
    private static readonly Color Ink = new Color32(3, 8, 18, 255);
    private static readonly Color Body = new Color32(53, 56, 65, 255);
    private static readonly Color Blue = new Color32(0, 51, 255, 255);
    private static double readyAfter;

    static RehearFigmaTutorialSetup()
    {
        if (!File.Exists(Request)) return;
        readyAfter = EditorApplication.timeSinceStartup + 3;
        EditorApplication.update += Pending;
    }
    private static void Pending()
    {
        if (EditorApplication.timeSinceStartup < readyAfter || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        EditorApplication.update -= Pending;
        if (!File.Exists(Request)) return;
        File.Delete(Request);
        Begin();
    }

    [MenuItem("Rehear/Build Figma Tutorial, Bake Pretendard And Lighting")]
    public static void Begin()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/01_Scene/Scene_00_5_Tutorial.unity")
        { Debug.LogError("Open the tutorial scene before running Figma setup."); return; }
        File.WriteAllText(Report, "started=" + DateTime.UtcNow.ToString("O") + "\n");
        RehearPretendardFontBake.Begin(() =>
        {
            try { Build(); RehearTutorialBlurSetup.ApplyAndBake(); }
            catch (Exception e) { File.AppendAllText(Report, "FAILED\n" + e); Debug.LogException(e); }
        });
    }

    [MenuItem("Rehear/Build Native Figma Tutorial UI")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the tutorial scene in Edit mode.");
        if (!RehearPretendardFontBake.Font("Bold") || !RehearPretendardFontBake.Font("Medium"))
            throw new InvalidOperationException("Bake Pretendard fonts first.");
        var roots = scene.GetRootGameObjects();
        var manager = roots.SelectMany(r => r.GetComponentsInChildren<TutorialManager>(true)).Single();
        var canvas = roots.SelectMany(r => r.GetComponentsInChildren<Canvas>(true)).Single(c => c.name == "TutorialUI");
        if (canvas.transform.Find("Figma UI design"))
            throw new InvalidOperationException("Native Figma UI already exists; retained without overwriting edits.");
        if (!AssetDatabase.IsValidFolder(Materials)) AssetDatabase.CreateFolder("Assets/Settings", "TutorialUI");
        foreach (var path in Directory.GetFiles("Assets/Textures/UI/FigmaTutorial", "*.png"))
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        Undo.RegisterFullObjectHierarchyUndo(canvas.gameObject, "Build Figma tutorial UI");
        var root = Rect(canvas.transform, "Figma UI design", 0, 0, 1200, 866);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = new Vector2(0, -60);
        root.localScale = Vector3.one * .25f;
        var view = root.gameObject.AddComponent<TutorialFigmaView>();
        view.steps = new GameObject[9];
        for (int i = 0; i < 9; i++) view.steps[i] = Rect(root, "Step " + i, 0, 0, 1200, 866).gameObject;
        Guide(view.steps[0].transform);
        Practice(view.steps[1].transform, 282, "기본 조작을 연습해볼까요?", "오른손 컨트롤러로 간단한 조작을 따라 해보세요", -1);
        Practice(view.steps[2].transform, 223, "아래 버튼을 선택해보세요", "컨트롤러로 버튼을 가리키고 트리거를 눌러보세요", 0);
        Practice(view.steps[3].transform, 223, "잘했어요!", "이제 조이스틱을 좌우로 기울여 앞에 있는 슬라이드를 3회 넘겨보세요", 1);
        Practice(view.steps[4].transform, 223, "잠시 뒤를 돌아봐 주세요!", "뒤쪽의 큰 화면에서도 확인하실 수 있어요\n돌아서 3회 넘겨보세요", 1);
        Practice(view.steps[5].transform, 223, "좋아요!", "이번에는 조이스틱을 위아래로 기울여 대본을 3회 넘겨보세요", 2);
        Practice(view.steps[6].transform, 223, "거의 다 왔어요!", "그립 버튼을 눌러 세션을 잠시 멈춰보세요", 3);
        Pause(view.steps[7].transform, roots.SelectMany(r => r.GetComponentsInChildren<PresentationController>(true)).Single());
        var complete = Glass(view.steps[8].transform, "Complete glass", 198, 281.5f, 804, 303);
        Label(complete, "Title", "모든 준비가 완료됐어요!", 33, 50, 738, 46, 36, "Bold", Ink);
        Label(complete, "Description", "기본 조작을 모두 익혔어요\n이제 Re:hear와 실전 같은 연습을 시작해볼까요?", 33, 109, 738, 66, 26, "Medium", Body);
        Progress(view.steps[8].transform, 249.5f, 4);

        var so = new SerializedObject(manager);
        var primary = (Button)so.FindProperty("primaryButton").objectReferenceValue;
        var secondary = (Button)so.FindProperty("secondaryButton").objectReferenceValue;
        if (!primary || !secondary) throw new InvalidOperationException("Existing tutorial buttons missing.");
        primary.transform.SetParent(root, false);
        secondary.transform.SetParent(root, false);
        var primaryText = (TMP_Text)so.FindProperty("primaryButtonText").objectReferenceValue;
        var secondaryText = (TMP_Text)so.FindProperty("secondaryButtonText").objectReferenceValue;
        view.primary = (RectTransform)primary.transform;
        view.secondary = (RectTransform)secondary.transform;
        view.primaryMaterial = StyleButton(primary, primaryText, "Primary", Blue, 735, 0, "튜토리얼 시작하기");
        view.secondaryMaterial = StyleButton(secondary, secondaryText, "Secondary", Color.clear, 735, 0, "건너뛰기");
        so.FindProperty("figmaView").objectReferenceValue = view;
        so.ApplyModifiedProperties();
        var stage = (Image)so.FindProperty("stageImage").objectReferenceValue;
        if (stage) stage.enabled = false;
        foreach (var field in new[] { "progressImage", "timerStopPanel", "tutorial5_1Object" })
        {
            var obj = so.FindProperty(field).objectReferenceValue;
            if (obj is Component component) component.gameObject.SetActive(false);
            else if (obj is GameObject go) go.SetActive(false);
        }
        RehearTutorialStepButtons.Configure(view, manager);
        view.Show(0);
        primary.gameObject.SetActive(true);
        secondary.gameObject.SetActive(true);
        canvas.GetComponent<CurvedUISettings>().AddEffectToChildren();
        Canvas.ForceUpdateCanvases();
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true);
        EditorUtility.SetDirty(manager);
        PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save native tutorial UI.");
        File.AppendAllText(Report, $"nativeViews=9\nTMPLabels={root.GetComponentsInChildren<TMP_Text>(true).Length}\n" +
            $"blurPanels={root.GetComponentsInChildren<TranslucentImage>(true).Length}\nsource=Figma UI design 2323:39215\n" +
            $"guide=2323:39216\ncontrollerAndIcons=individualFigmaExports\nbuttonsRetainExistingEvents=true\nsaved={DateTime.UtcNow:O}\n");
        Selection.activeGameObject = root.gameObject;
        Debug.Log("Rehear: Native Figma UI saved with editable Pretendard TMP, original exported artwork, and curved panels.");
    }

    private static void Guide(Transform parent)
    {
        var p = Glass(parent, "Controller guide glass", 0, 0, 1200, 866, RehearBlurDiagnostics.GuideWhiteTint);
        Label(p, "Title", "컨트롤러 조작 방법", 49, 65, 1102, 46, 36, "Bold", Ink);
        Label(p, "Description", "시작 전, 오른손 컨트롤러의 기본 조작을 확인해 주세요", 49, 122, 1102, 34, 24, "Medium", Body);
        Icon(p, "controller", 333, 67, 535, 720);
        Icon(p, "line-trigger", 405, 314.67f, 55.33f, 10.66f);
        Icon(p, "line-grip", 405, 424.41f, 84.33f, 135.1f);
        Icon(p, "line-stick", 670.67f, 242.67f, 119.33f, 248.33f);
        Callout(p, 49, 246, "select", "UI 버튼 선택", "화면의 버튼을 가리키고\n트리거를 눌러 선택하세요");
        Callout(p, 49, 478, "pause", "일시정지", "그립을 움켜쥐면 진행 중인 세션을\n잠시 중단할 수 있어요");
        Callout(p, 789, 180, "slide", "슬라이드 이동", "조이스틱을 좌우로 기울여\n슬라이드를 넘길 수 있어요");
        Callout(p, 789, 412, "script", "대본 페이지 이동", "조이스틱을 위아래로 기울여\n대본을 넘길 수 있어요");
    }
    private static void Callout(Transform parent, float x, float y, string icon, string title, string body)
    {
        var p = Solid(parent, "Callout_" + icon, x, y, 356, 154, new Color(1, 1, 1, .2f), 40, 0);
        Icon(p, icon, 28, 24, 36, 36);
        Label(p, "Title", title, 76, 24, 260, 36, 26, "Bold", Blue, TextAlignmentOptions.MidlineLeft);
        Label(p, "Description", body, 28, 68, 310, 64, 22, "Medium", Body, TextAlignmentOptions.TopLeft);
    }
    private static void Practice(Transform parent, float height, string title, string body, int progress)
    {
        float y = (866 - height) * .5f;
        var p = Glass(parent, "Practice glass", 198, y, 804, height);
        Label(p, "Title", title, 33, 67, 738, 46, 36, "Bold", Ink);
        Label(p, "Description", body, 25, 126, 754, 67, 26, "Medium", Body);
        if (progress >= 0) Progress(parent, y - 32, progress);
    }
    private static void Progress(Transform parent, float y, int count)
    {
        for (int i = 0; i < 4; i++)
            Solid(parent, "Progress " + i, 532 + i * 40, y, 16, 16,
                i < count ? Blue : new Color(1, 1, 1, .45f), 8, 0);
    }
    private static void Pause(Transform parent, PresentationController controller)
    {
        var p = Glass(parent, "Pause glass", 331.5f, 135, 537, 498, .16f);
        Icon(p, "pause-large", 210.5f, 47, 116, 116);
        Label(p, "Title", "일시정지", 33, 183, 471, 46, 36, "Bold", Color.white);
        var restart = PauseButton(p, "Restart", "다시 시작하기", "replay", 262);
        UnityEventTools.AddPersistentListener(restart.onClick, controller.RestartSession);
        var stop = PauseButton(p, "Stop", "세션 종료하기", "exit", 331);
        UnityEventTools.AddPersistentListener(stop.onClick, controller.StopSession);
        Label(p, "Hint", "그립 버튼을 눌러 진행 중인 세션을 계속하세요", 20, 423, 497, 30, 20, "Medium", Color.white);
        var helper = Glass(parent, "Resume helper glass", 198, 650, 804, 135);
        Label(helper, "Description", "여기에서 세션을 다시 시작하거나 종료할 수 있어요\n그립 버튼을 다시 눌러 이어서 진행해볼까요?", 25, 30, 754, 75, 26, "Medium", Body);
        MatchPauseDesign(parent);
    }

    // Figma UI design 2323:39413. Keep live blur and existing click events.
    internal static void MatchPauseDesign(Transform step)
    {
        Undo.RegisterFullObjectHierarchyUndo(step.gameObject, "Match paused UI to Figma");
        var panel = (RectTransform)step.Find("Pause glass");
        var helper = (RectTransform)step.Find("Resume helper glass");
        TutorialFigmaView.Place(panel, 331.5f, 135, 537, 498);
        // Original panel bottom=750, helper top=830: 80px clear separation.
        TutorialFigmaView.Place(helper, 198, 713, 804, 135);
        SetGlass(panel, RehearBlurDiagnostics.PauseWhiteTint);
        SetGlass(helper, RehearBlurDiagnostics.ResumeWhiteTint);
        SetIcon(panel.Find("pause-large"), "pause-large", 210.5f, 47, 116, 116);
        SetText(panel.Find("Title"), "일시정지", 33, 183, 471, 46, 36, "Bold", Ink);
        SetPauseButton(panel.Find("Restart"), "처음부터 다시하기", "replay", 262);
        SetPauseButton(panel.Find("Stop"), "세션 종료하기", "exit", 331);
        SetText(panel.Find("Hint"), "그립 버튼을 눌러 진행 중인 세션을 계속하세요", 20, 423, 497, 30, 20, "Medium", Color.white);
        SetText(helper.Find("Description"), "여기에서 세션을 다시 시작하거나 종료할 수 있어요\n그립 버튼을 다시 눌러 이어서 진행해볼까요?", 25, 30, 754, 75, 26, "Medium", Body);
        helper.Find("Description").GetComponent<TMP_Text>().lineSpacing = 5.2f;
        foreach (var text in step.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate(true, true);
        foreach (var curve in step.GetComponentsInChildren<CurvedUIVertexEffect>(true)) curve.SetDirty();
        Canvas.ForceUpdateCanvases();
    }

    private static void SetGlass(RectTransform rect, float tint)
    {
        var glass = rect.GetComponent<TranslucentImage>();
        Undo.RecordObject(glass.material, "Paused glass tint");
        glass.foregroundOpacity = tint;
        glass.material.SetFloat("_GlassTint", tint);
        glass.material.SetFloat("_UseBlur", 1);
        glass.material.SetFloat("_Radius", 48);
        glass.material.SetFloat("_BorderWidth", 2);
        glass.material.SetVector("_PanelSize", new Vector4(rect.rect.width, rect.rect.height, 0, 0));
        EditorUtility.SetDirty(glass.material);
        AssetDatabase.SaveAssetIfDirty(glass.material);
        glass.SetAllDirty();
    }

    private static TMP_Text SetText(Transform target, string value, float x, float y, float w, float h, float size, string weight, Color color)
    {
        TutorialFigmaView.Place((RectTransform)target, x, y, w, h);
        var text = target.GetComponent<TMP_Text>();
        SetLabel(text, value, size, weight, color, TextAlignmentOptions.Center);
        text.characterSpacing = -1;
        text.lineSpacing = 0;
        return text;
    }

    private static void SetIcon(Transform target, string asset, float x, float y, float w, float h)
    {
        TutorialFigmaView.Place((RectTransform)target, x, y, w, h);
        var image = target.GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Textures/UI/FigmaTutorial/{asset}.png");
        if (!image.sprite) throw new InvalidOperationException("Missing Figma icon: " + asset);
        image.material = null;
        image.color = Color.white;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.SetAllDirty();
    }

    private static void SetPauseButton(Transform target, string value, string icon, float y)
    {
        TutorialFigmaView.Place((RectTransform)target, 33, y, 467, 56);
        var image = target.GetComponent<Image>();
        image.material = PanelMaterial("Paused Button 467x56", 467, 56, 40, 2, false);
        image.color = Color.clear;
        image.canvasRenderer.cullTransparentMesh = false;
        AssetDatabase.SaveAssetIfDirty(image.material);
        var label = SetText(target.Find("Label"), value, 0, 0, 250, 56, 24, "Medium", Color.white);
        float width = label.GetPreferredValues(value, 1000, 56).x;
        float left = (467 - (24 + 10 + width)) * .5f;
        TutorialFigmaView.Place(label.rectTransform, left + 34, 0, width, 56);
        SetIcon(target.Find(icon), icon, left, 16, 24, 24);
        image.SetAllDirty();
    }
    private static Button PauseButton(Transform parent, string name, string text, string icon, float y)
    {
        var rect = Solid(parent, name, 33, y, 471, 56, Color.clear, 40, 2);
        var image = rect.GetComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        Label(rect, "Label", text, 115, 0, 250, 56, 24, "Medium", Color.white);
        Icon(rect, icon, 108, 16, 24, 24);
        return button;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        TutorialFigmaView.Place(rect, x, y, w, h);
        return rect;
    }
    private static Material PanelMaterial(string key, float w, float h, float radius, float border, bool blur)
    {
        string path = $"{Materials}/{key}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var shader = Shader.Find("Rehear/UI/Rounded Translucent Panel");
            if (!shader) throw new InvalidOperationException("Rounded UI shader missing.");
            material = new Material(shader) { name = key };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetVector("_PanelSize", new Vector4(w, h, 0, 0));
        material.SetFloat("_Radius", radius);
        material.SetFloat("_BorderWidth", border);
        material.SetFloat("_UseBlur", blur ? 1 : 0);
        EditorUtility.SetDirty(material);
        return material;
    }
    private static RectTransform Glass(Transform parent, string name, float x, float y, float w, float h, float tint = .16f)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var image = rect.gameObject.AddComponent<TranslucentImage>();
        image.material = PanelMaterial($"Glass {w}x{h}", w, h, 48, 2, true);
        image.color = Color.white;
        image.foregroundOpacity = tint;
        image.material.SetFloat("_GlassTint", tint);
        image.raycastTarget = false;
        return rect;
    }
    private static RectTransform Solid(Transform parent, string name, float x, float y, float w, float h, Color color, float radius, float border)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var image = rect.gameObject.AddComponent<Image>();
        image.material = PanelMaterial($"Solid {w}x{h} r{radius} b{border}", w, h, radius, border, false);
        image.color = color;
        // The shader can draw an outline even when the interior's vertex alpha is zero.
        image.canvasRenderer.cullTransparentMesh = false;
        image.raycastTarget = false;
        return rect;
    }
    private static TMP_Text Label(Transform parent, string name, string text, float x, float y, float w, float h, float size, string weight, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var rect = Rect(parent, name, x, y, w, h);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        SetLabel(label, text, size, weight, color, align);
        return label;
    }
    private static void SetLabel(TMP_Text label, string text, float size, string weight, Color color, TextAlignmentOptions align)
    {
        label.font = RehearPretendardFontBake.Font(weight);
        label.fontSharedMaterial = label.font.material;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.alignment = align;
        label.color = color;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
    }
    private static void Icon(Transform parent, string name, float x, float y, float w, float h)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Textures/UI/FigmaTutorial/{name}.png");
        if (!sprite) throw new InvalidOperationException("Missing Figma exported sprite: " + name);
        var rect = Rect(parent, name, x, y, w, h);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.raycastTarget = false;
    }
    private static Material StyleButton(Button button, TMP_Text label, string name, Color color, float width, float border, string text)
    {
        var image = button.GetComponent<Image>();
        image.sprite = null;
        image.type = Image.Type.Simple;
        image.color = color;
        image.raycastTarget = true;
        image.canvasRenderer.cullTransparentMesh = false;
        var material = PanelMaterial(name, width, 56, 40, border, false);
        image.material = material;
        button.targetGraphic = image;
        SetLabel(label, text, 22, "Medium", Color.white, TextAlignmentOptions.Center);
        var rect = label.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return material;
    }
}
