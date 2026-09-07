using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
internal static class RehearTutorialDirectionSetup
{
    const string Request = "Temp/RehearTutorialDirection.request";
    const string ArrowSpritePath = "Assets/04_Images/Textures/UI/Next Arrow.png";
    static double next;
    static RehearTutorialDirectionSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        string command = File.ReadAllText(Request).Trim();
        File.Delete(Request);
        try { Apply(command); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialDirection.txt", "FAILED\n" + e); }
    }

    [MenuItem("Rehear/Tutorial/Preview Desk Direction Hints")]
    static void PreviewDesk() { Apply("desk"); }
    [MenuItem("Rehear/Tutorial/Preview Script Direction Hints")]
    static void PreviewScript() { Apply("script"); }

    static void Apply(string command)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || Lightmapping.isRunning)
            throw new InvalidOperationException("Stop Play mode and wait for compilation/baking before previewing.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        if (command == "style" || command == "sprite")
        {
            // Do not reset placements or the current preview when restyling existing hints.
            var hints = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TutorialDirectionArrow>(true)).ToArray();
            if (hints.Length != 4) throw new InvalidOperationException("Expected four existing direction hints.");
            var sprite = LoadArrowSprite();
            Undo.RecordObjects(hints, "Use supplied Next Arrow sprite");
            foreach (var hint in hints)
            {
                StyleArrow(hint, sprite);
                EditorUtility.SetDirty(hint);
            }
            if (hints.Any(h => h.sprite != sprite || h.color != Color.white || h.raycastTarget))
                throw new InvalidOperationException("Direction sprite assignment failed.");
            string pageReport = BindAndCheckPageHints(scene, hints);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
            File.WriteAllText("Temp/RehearTutorialDirection.txt", "arrows=4\nsprite=Next Arrow\noriginalImageColor=true\nraycastTargets=0\nplacementsAndPreview=unchanged\nsaved=true\n" + pageReport);
            return;
        }
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var desk = (RectTransform)all.Single(t => t.name == "DeskScreen");
        var script = (RectTransform)all.Single(t => t.name == "Panel_Script_New");
        var view = all.Select(t => t.GetComponent<TutorialFigmaView>()).Single(v => v);
        Vector3 deskPosition = desk.position, scriptPosition = script.position;
        Quaternion deskRotation = desk.rotation, scriptRotation = script.rotation;
        Vector3 deskScale = desk.lossyScale, scriptScale = script.lossyScale;
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add tutorial joystick direction hints");
        try
        {
            Undo.RecordObject(view, "Bind direction hints");
            view.deskDirectionHints = Root(desk, "SlideDirectionHints").gameObject;
            view.scriptDirectionHints = Root(script, "ScriptDirectionHints").gameObject;
            Arrow(view.deskDirectionHints.transform, "Arrow_Left", new Vector2(0, .5f), new Vector2(-5.5f, 0), 180);
            Arrow(view.deskDirectionHints.transform, "Arrow_Right", new Vector2(1, .5f), new Vector2(5.5f, 0), 0);
            Arrow(view.scriptDirectionHints.transform, "Arrow_Up", new Vector2(.5f, 1), new Vector2(0, 5.5f), 90);
            Arrow(view.scriptDirectionHints.transform, "Arrow_Down", new Vector2(.5f, 0), new Vector2(0, -5.5f), 270);
            Undo.RecordObjects(view.steps.Where(s => s).Cast<UnityEngine.Object>().Concat(new UnityEngine.Object[] { script.gameObject }).ToArray(), "Preview direction hints");
            // Exercise all transitions, including legacy early-return button paths.
            for (int step = 0; step < 9; step++)
            {
                view.Show(step);
                if (view.deskDirectionHints.activeSelf != (step == 3) || view.scriptDirectionHints.activeSelf != (step == 5))
                    throw new InvalidOperationException("Incorrect direction-hint visibility at step " + step);
            }
            var arrows = new[] { view.deskDirectionHints, view.scriptDirectionHints }
                .SelectMany(r => r.GetComponentsInChildren<TutorialDirectionArrow>(true)).ToArray();
            if (arrows.Length != 4 || arrows.Any(a => a.raycastTarget || a.GetComponent<UnityEngine.UI.Selectable>()))
                throw new InvalidOperationException("Hints must be four non-interactive graphics.");
            bool scriptPreview = command == "script";
            view.Show(scriptPreview ? 5 : 3);
            script.gameObject.SetActive(scriptPreview);
            BindAndCheckPageHints(scene, arrows);
            if (desk.position != deskPosition || script.position != scriptPosition ||
                desk.rotation != deskRotation || script.rotation != scriptRotation ||
                desk.lossyScale != deskScale || script.lossyScale != scriptScale)
                throw new InvalidOperationException("Existing display transforms changed.");
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
            Selection.activeGameObject = scriptPreview ? script.gameObject : desk.gameObject;
            var target = scriptPreview ? script : desk;
            if (SceneView.lastActiveSceneView)
                SceneView.lastActiveSceneView.LookAt(target.position, target.rotation, .55f);
            Undo.CollapseUndoOperations(group);
            File.WriteAllText("Temp/RehearTutorialDirection.txt", "arrows=4\nstepTransitions=9 PASS\nraycastTargets=0\nexistingTransformsUnchanged=true\nsaved=true\npreview=" + command);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    static RectTransform Root(RectTransform parent, string name)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create direction hints");
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
        }
        Undo.RecordObject(rect, "Place direction hints");
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity; rect.localScale = Vector3.one;
        return rect;
    }

    static void Arrow(Transform parent, string name, Vector2 anchor, Vector2 offset, float rotation)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TutorialDirectionArrow));
            Undo.RegisterCreatedObjectUndo(go, "Create arrow");
            rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
        }
        var arrow = rect.GetComponent<TutorialDirectionArrow>();
        Undo.RecordObjects(new UnityEngine.Object[] { rect, arrow }, "Style arrow");
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition3D = new Vector3(offset.x, offset.y, -.05f);
        rect.sizeDelta = new Vector2(6, 6);
        rect.localRotation = Quaternion.Euler(0, 0, rotation); rect.localScale = Vector3.one;
        StyleArrow(arrow, LoadArrowSprite());
    }

    static Sprite LoadArrowSprite()
    {
        var importer = AssetImporter.GetAtPath(ArrowSpritePath) as TextureImporter;
        if (!importer) throw new InvalidOperationException("Missing supplied Next Arrow image.");
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single ||
            !importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArrowSpritePath);
        if (!sprite) throw new InvalidOperationException("Next Arrow sprite import failed.");
        return sprite;
    }

    static string BindAndCheckPageHints(UnityEngine.SceneManagement.Scene scene, TutorialDirectionArrow[] arrows)
    {
        var up = arrows.Single(a => a.name == "Arrow_Up").gameObject;
        var down = arrows.Single(a => a.name == "Arrow_Down").gameObject;
        var panel = up.transform.parent.parent;
        var scroller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ScriptScroller>(true))
            .Single(s => {
                var text = new SerializedObject(s).FindProperty("scriptText").objectReferenceValue as TMPro.TMP_Text;
                return text && text.transform.IsChildOf(panel);
            });
        Undo.RecordObject(scroller, "Bind script page direction hints");
        Undo.RecordObjects(new UnityEngine.Object[] { up, down }, "Update page hint visibility");
        var so = new SerializedObject(scroller);
        so.FindProperty("previousPageHint").objectReferenceValue = up;
        so.FindProperty("nextPageHint").objectReferenceValue = down;
        so.ApplyModifiedProperties();
        int originalPage = scroller.CurrentPage;
        scroller.RefreshPagination();
        var textObject = (TMPro.TMP_Text)so.FindProperty("scriptText").objectReferenceValue;
        // Keep inactive previews inactive; runtime RefreshPagination still updates the hints.
        if (!textObject.gameObject.activeInHierarchy) return "pageHints=bound (inactive preview)";
        try
        {
            scroller.ResetToFirstPage();
            for (int page = 1; page <= scroller.PageCount; page++)
            {
                if (scroller.CurrentPage != page || up.activeSelf != (page > 1) || down.activeSelf != (page < scroller.PageCount))
                    throw new InvalidOperationException("Incorrect page hint visibility at page " + page);
                scroller.NextPage();
            }
            if (scroller.CurrentPage != scroller.PageCount) throw new InvalidOperationException("Past last page.");
            for (int page = scroller.PageCount; page > 1; page--) scroller.PreviousPage();
            scroller.PreviousPage();
            if (scroller.CurrentPage != 1 || up.activeSelf) throw new InvalidOperationException("Before first page.");
            return "pageBoundaries=PASS\npages=" + scroller.PageCount;
        }
        finally
        {
            scroller.ResetToFirstPage();
            for (int page = 1; page < Mathf.Min(originalPage, scroller.PageCount); page++) scroller.NextPage();
            EditorUtility.SetDirty(scroller);
        }
    }

    static void StyleArrow(TutorialDirectionArrow arrow, Sprite sprite)
    {
        arrow.sprite = sprite;
        arrow.overrideSprite = null;
        arrow.type = UnityEngine.UI.Image.Type.Simple;
        arrow.preserveAspect = true;
        arrow.color = Color.white;
        arrow.material = null;
        arrow.raycastTarget = false;
        arrow.SetAllDirty();
    }
}
