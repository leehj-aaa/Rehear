using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
internal static class RehearTutorialScriptStyleSetup
{
    const string Request = "Temp/RehearTutorialScriptStyle.request";
    const string TextMaterialPath = "Assets/Settings/TutorialUI/Script Text White.mat";
    const string BackMaterialPath = "Assets/Settings/TutorialUI/Script Screen Black.mat";
    static double next;
    static RehearTutorialScriptStyleSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning) return;
        File.Delete(Request);
        try { Apply(); }
        catch (Exception e) { File.WriteAllText("Temp/RehearTutorialScriptStyle.txt", "FAILED\n" + e); }
    }

    static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/01_Scene/Scene_00_5_Tutorial.unity") throw new InvalidOperationException("Open tutorial scene.");
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        var panel = (RectTransform)all.Single(t => t.name == "Panel_Script_New");
        var viewport = panel.Find("Viewport");
        var counter = panel.parent.GetComponent<MeshRenderer>();
        var texts = panel.GetComponentsInChildren<TMP_Text>(true);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/07_Fonts/PretendardTMP/Pretendard-Regular SDF.asset");
        if (!font || !viewport || !counter || counter.name != "Counter" || texts.Length == 0)
            throw new InvalidOperationException("Missing script surface or Pretendard asset.");
        foreach (var text in texts)
            if (!font.HasCharacters(new string(text.text.Where(c => !char.IsControl(c)).ToArray()), out uint[] missing, false, false))
                throw new InvalidOperationException("Pretendard missing characters: " + string.Join(",", missing));
        var materials = counter.sharedMaterials;
        int screenIndex = Array.FindIndex(materials, m => m && (m.name == "Screen" || m.name == "Script Screen Black"));
        if (screenIndex < 0) throw new InvalidOperationException("Podium screen material not found.");
        Vector3 position = panel.position, scale = panel.lossyScale;
        Quaternion rotation = panel.rotation;
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Style script with white Pretendard on black");
        try
        {
            var textMaterial = AssetDatabase.LoadAssetAtPath<Material>(TextMaterialPath);
            if (!textMaterial)
            {
                textMaterial = new Material(font.material) { name = "Script Text White" };
                AssetDatabase.CreateAsset(textMaterial, TextMaterialPath);
            }
            Undo.RecordObject(textMaterial, "Clean text face");
            textMaterial.SetColor("_FaceColor", Color.white);
            foreach (string property in new[] { "_OutlineWidth", "_OutlineSoftness", "_FaceDilate", "_UnderlayDilate", "_UnderlaySoftness", "_GlowPower" })
                if (textMaterial.HasProperty(property)) textMaterial.SetFloat(property, 0);
            foreach (string keyword in new[] { "OUTLINE_ON", "UNDERLAY_ON", "UNDERLAY_INNER", "GLOW_ON", "BEVEL_ON" })
                textMaterial.DisableKeyword(keyword);
            var backMaterial = AssetDatabase.LoadAssetAtPath<Material>(BackMaterialPath);
            if (!backMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader) throw new InvalidOperationException("URP Unlit shader unavailable.");
                backMaterial = new Material(shader) { name = "Script Screen Black" };
                AssetDatabase.CreateAsset(backMaterial, BackMaterialPath);
            }
            Undo.RecordObject(backMaterial, "Black physical screen backing");
            backMaterial.SetColor("_BaseColor", Color.black);
            Undo.RecordObject(counter, "Use local script screen backing");
            materials[screenIndex] = backMaterial;
            counter.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(counter);
            var background = panel.GetComponent<Image>();
            Undo.RecordObject(background, "Remove inset border sprite");
            background.sprite = null; background.type = Image.Type.Simple;
            background.material = null; background.color = Color.black; background.raycastTarget = false;
            var viewportImage = viewport.GetComponent<Image>();
            if (viewportImage)
            {
                Undo.RecordObject(viewportImage, "Remove duplicate rounded border");
                viewportImage.enabled = false;
            }
            // Keep RectMask2D active: it clips text, not the arrows outside the viewport.
            foreach (var effect in panel.GetComponentsInChildren<Shadow>(true))
            {
                Undo.RecordObject(effect, "Remove script UI shadow/outline"); effect.enabled = false;
            }
            foreach (var text in texts)
            {
                Undo.RecordObject(text, "Apply Pretendard regular white");
                text.font = font; text.fontSharedMaterial = textMaterial;
                text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
                text.color = Color.white; text.enableVertexGradient = false;
                text.overrideColorTags = true; text.raycastTarget = false;
                text.ForceMeshUpdate(true, true);
                EditorUtility.SetDirty(text);
            }
            var scroller = all.Select(t => t.GetComponent<ScriptScroller>()).Single(s => s);
            scroller.RefreshPagination();
            scroller.NextPage(); scroller.PreviousPage();
            var body = texts.Single(t => t.name == "Text_Script_New");
            if (body.textInfo.pageCount < 4 || body.pageToDisplay != 1)
                throw new InvalidOperationException("Tutorial pagination check failed.");
            if (panel.position != position || panel.rotation != rotation || panel.lossyScale != scale)
                throw new InvalidOperationException("Panel alignment changed.");
            EditorUtility.SetDirty(textMaterial); EditorUtility.SetDirty(backMaterial);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Save failed.");
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = panel.gameObject;
            if (SceneView.lastActiveSceneView) SceneView.lastActiveSceneView.LookAt(panel.position, panel.rotation, .55f);
            File.WriteAllText("Temp/RehearTutorialScriptStyle.txt",
                $"font={font.name}\ntextColor=white\nbackground=opaque black\nborderSprites=removed\nviewportMask=preserved\noutline=off\nmissingGlyphs=0\npages={body.textInfo.pageCount}\npageRoundTrip=PASS\ntransformUnchanged=true\nsaved=true\n");
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }
}
