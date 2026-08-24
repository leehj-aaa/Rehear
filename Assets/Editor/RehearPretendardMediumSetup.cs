using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

[InitializeOnLoad]
internal static class RehearPretendardMediumSetup
{
    private const string SourcePath = "Assets/07_Fonts/Pretendard-Medium.otf";
    private const string AssetPath = "Assets/07_Fonts/Pretendard-Medium SDF.asset";
    private const string ScenePath = "Assets/01_Scene/Scene_00.unity";

    static RehearPretendardMediumSetup()
    {
        EditorApplication.delayCall += CreateAndAssign;
    }

    [MenuItem("Rehear/Create And Assign Pretendard Medium TMP Font")]
    private static void CreateAndAssign()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
        if (fontAsset == null)
        {
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (sourceFont == null)
            {
                Debug.LogError("Pretendard Medium 원본 폰트를 찾지 못했습니다: " + SourcePath);
                return;
            }

            fontAsset = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);

            if (fontAsset == null)
            {
                Debug.LogError("Pretendard Medium TMP 폰트 에셋 생성에 실패했습니다.");
                return;
            }

            fontAsset.name = "Pretendard-Medium SDF";
            AssetDatabase.CreateAsset(fontAsset, AssetPath);
            foreach (var atlas in fontAsset.atlasTextures.Where(atlas => atlas != null))
                AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            if (fontAsset.material != null)
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        // Bake the opening button's Korean glyphs now while keeping the asset
        // dynamic and multi-atlas capable for additional Korean UI strings.
        if (!fontAsset.TryAddCharacters("시작하기", out var missingCharacters, true))
            Debug.LogWarning("Pretendard Medium에 포함되지 않은 글리프: " + missingCharacters);

        EditorUtility.SetDirty(fontAsset);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != ScenePath)
            return;

        var label = Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(text => text.name == "Text (TMP)" && text.transform.parent != null &&
                                    text.transform.parent.name == "Btn_Scene00_to_Scene01");
        if (label == null)
        {
            Debug.LogError("Scene_00 시작하기 TextMeshPro 오브젝트를 찾지 못했습니다.");
            return;
        }

        Undo.RecordObject(label, "Assign Pretendard Medium TMP font");
        label.font = fontAsset;
        label.fontSharedMaterial = fontAsset.material;
        EditorUtility.SetDirty(label);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Rehear: 시작하기 텍스트에 Pretendard Medium TMP SDF를 적용했습니다.", label);
    }
}
